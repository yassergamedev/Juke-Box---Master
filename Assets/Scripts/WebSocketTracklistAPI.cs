using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WebSocketSharp;
using System.Text;

/// <summary>
/// WebSocket API client for tracklist operations
/// Handles real-time tracklist changes via WebSocket server
/// </summary>
public class WebSocketTracklistAPI : MonoBehaviour
{
    [Header("WebSocket Server Settings")]
    public string webSocketUrl = "ws://localhost:8082";
    public string httpApiBaseUrl = "http://localhost:8082/api";
    
    [Header("Connection Settings")]
    public float reconnectInterval = 5f;
    public int maxReconnectAttempts = 10;
    
    private WebSocket webSocket;
    private bool isConnected = false;
    private Coroutine reconnectCoroutine;
    private int reconnectAttempts = 0;
    
    // Events for tracklist updates
    public static event Action<TracklistUpdate> OnTracklistUpdate;
    public static event Action<string> OnConnectionStatusChanged;
    
    // Current tracklist state
    private List<TracklistItem> currentTracklist = new List<TracklistItem>();
    private TracklistItem currentSong = null;
    private bool isPlaying = false;
    private bool isPaused = false;
    
    // Message queue for thread-safe processing
    private Queue<string> messageQueue = new Queue<string>();
    private object queueLock = new object();
    private const int maxMessageQueueSize = 500; // Prevent memory leak
    
    private void Start()
    {
        ConnectToWebSocket();
    }
    
    private float wsUpdateTimer = 0f;
    private const float wsUpdateInterval = 0.05f; // Process every 50ms instead of every frame
    
    private void Update()
    {
        // Throttle message processing to reduce blocking
        wsUpdateTimer += Time.deltaTime;
        if (wsUpdateTimer >= wsUpdateInterval)
        {
            wsUpdateTimer = 0f;
            ProcessQueuedMessages();
        }
    }
    
    private void ProcessQueuedMessages()
    {
        // Limit processing per frame to prevent coroutine explosion
        const int maxPerFrame = 2; // Reduced from 3 to prevent blocking
        int processed = 0;
        
        lock (queueLock)
        {
            while (messageQueue.Count > 0 && processed < maxPerFrame)
            {
                string message = messageQueue.Dequeue();
                StartCoroutine(ProcessWebSocketMessageOnMainThread(message));
                processed++;
            }
        }
    }
    
    private void ConnectToWebSocket()
    {
        try
        {
            webSocket = new WebSocket(webSocketUrl);
            
            webSocket.OnOpen += OnWebSocketOpen;
            webSocket.OnMessage += OnWebSocketMessage;
            webSocket.OnError += OnWebSocketError;
            webSocket.OnClose += OnWebSocketClose;
            
            webSocket.Connect();
            
            Debug.Log("[WS_API] Attempting to connect to WebSocket server...");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS_API] Error connecting to WebSocket: {ex.Message}");
            StartReconnectTimer();
        }
    }
    
    private void OnWebSocketOpen(object sender, EventArgs e)
    {
        isConnected = true;
        reconnectAttempts = 0;
        Debug.Log("[WS_API] Connected to WebSocket server");
        HubLogger.LogSuccess("WebSocket connected", LogCategory.WebSocket);
        OnConnectionStatusChanged?.Invoke("Connected");
        
        if (reconnectCoroutine != null)
        {
            StopCoroutine(reconnectCoroutine);
            reconnectCoroutine = null;
        }
        
        // Register as master/hub or slave after connection
        RegisterWithServer();
    }
    
    /// <summary>
    /// Registers this client with the WebSocket server as either master/hub or slave
    /// </summary>
    private void RegisterWithServer()
    {
        if (webSocket == null || !webSocket.IsAlive)
        {
            Debug.LogWarning("[WS_API] Cannot register - WebSocket not connected");
            return;
        }
        
        // Determine role based on AlbumManager
        AlbumManager albumManager = FindObjectOfType<AlbumManager>();
        string role = "master"; // Default to master
        
        if (albumManager != null && albumManager.isSlave)
        {
            role = "slave";
        }
        
        // Create registration message as JSON string
        string jsonMessage = $"{{\"type\":\"register\",\"role\":\"{role}\"}}";
        
        try
        {
            webSocket.Send(jsonMessage);
            Debug.Log($"[WS_API] Registered with server as: {role}");
            HubLogger.LogSuccess($"Registered with WebSocket server as: {role}", LogCategory.WebSocket);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS_API] Failed to register with server: {ex.Message}");
            HubLogger.LogFailure($"Failed to register with WebSocket server: {ex.Message}", LogCategory.WebSocket);
        }
    }
    
    private void OnWebSocketMessage(object sender, MessageEventArgs e)
    {
        try
        {
            // Check if message data is null or empty
            if (e == null || string.IsNullOrEmpty(e.Data))
            {
                Debug.LogError("[WS_API] Received null or empty WebSocket message");
                return;
            }
            
            Debug.Log($"[WS_API] Received WebSocket message: {e.Data}");
            
            // Check if the message is valid JSON
            if (!IsValidJson(e.Data))
            {
                Debug.LogError($"[WS_API] Invalid JSON received: {e.Data}");
                return;
            }
            
            // Queue the message for processing on main thread
            lock (queueLock)
            {
                // Prevent memory leak - limit queue size
                if (messageQueue.Count >= maxMessageQueueSize)
                {
                    // Remove oldest message if queue is full
                    messageQueue.Dequeue();
                    Debug.LogWarning("[WS_API] Message queue full, dropping oldest message");
                }
                messageQueue.Enqueue(e.Data);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS_API] Error processing WebSocket message: {ex.Message}");
            Debug.LogError($"[WS_API] Stack trace: {ex.StackTrace}");
        }
    }
    
    private System.Collections.IEnumerator ProcessWebSocketMessageOnMainThread(string jsonData)
    {
        // Wait one frame to ensure we're on the main thread
        yield return null;
        
        Debug.Log($"[WS_API] Raw WebSocket message: {jsonData}");
        
        // Check if this is a validation request
        if (jsonData.Contains("validation_request"))
        {
            Debug.Log($"[WS_API] Detected validation request, processing on main thread");
            
            ValidationRequest validationRequest = null;
            bool parseSuccess = false;
            
            try
            {
                validationRequest = JsonUtility.FromJson<ValidationRequest>(jsonData);
                parseSuccess = true;
                Debug.Log($"[WS_API] Successfully parsed validation request: {validationRequest.title} by {validationRequest.artist}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WS_API] Error parsing validation request: {ex.Message}");
                Debug.LogError($"[WS_API] JSON data: {jsonData}");
            }
            
            if (parseSuccess && validationRequest != null)
            {
                Debug.Log($"[WS_API] Processing validation request: {validationRequest.title} by {validationRequest.artist} from album: {validationRequest.album}");
                
                // Validate the song exists and get its length
                yield return StartCoroutine(ValidateAndUpdateSongCoroutine(validationRequest));
            }
            else
            {
                Debug.LogError($"[WS_API] Failed to parse validation request or result is null");
            }
        }
        else
        {
            Debug.Log($"[WS_API] Detected tracklist update, processing on main thread");
            
            TracklistUpdate tracklistUpdate = null;
            bool parseSuccess = false;
            
            try
            {
                tracklistUpdate = JsonUtility.FromJson<TracklistUpdate>(jsonData);
                parseSuccess = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WS_API] Error parsing tracklist update: {ex.Message}");
            }
            
            if (parseSuccess && tracklistUpdate != null)
            {
                Debug.Log($"[WS_API] Processing WebSocket message: {tracklistUpdate.operationType} - {tracklistUpdate.songTitle}");
                
                // Update local state
                ProcessTracklistUpdate(tracklistUpdate);
                
                // Notify subscribers
                Debug.Log($"[WS_API] Invoking OnTracklistUpdate event for: {tracklistUpdate.operationType}");
                OnTracklistUpdate?.Invoke(tracklistUpdate);
            }
        }
    }
    
    
    
    private System.Collections.IEnumerator ValidateAndUpdateSongCoroutine(ValidationRequest request)
    {
        Debug.Log($"[WS_API] Starting validation for song: {request.title} from album: {request.album}");
        
        bool validationSuccess = false;
        int audioLength = 180; // Default fallback
        
        try
        {
            // Find the album folder
            string albumPath = FindAlbumFolder(request.album);
            if (string.IsNullOrEmpty(albumPath))
            {
                Debug.LogWarning($"[WS_API] Album not found: {request.album}");
                Debug.LogWarning($"[WS_API] Available albums will be logged for debugging");
                // Log available albums for debugging
                LogAvailableAlbums();
            }
            else
            {
                Debug.Log($"[WS_API] Found album folder: {albumPath}");
                
                // Find the audio file
                string audioPath = FindSongFilePath(albumPath, request.title);
                if (string.IsNullOrEmpty(audioPath))
                {
                    Debug.LogWarning($"[WS_API] Audio file not found: {request.title} in album: {request.album}");
                    Debug.LogWarning($"[WS_API] Available files in album will be logged for debugging");
                    // Log available files for debugging
                    LogAvailableFilesInAlbum(albumPath);
                }
                else
                {
                    Debug.Log($"[WS_API] Found audio file: {audioPath}");
                    
                    // Get audio length
                    audioLength = GetAudioFileLength(audioPath);
                    if (audioLength <= 0)
                    {
                        audioLength = 180; // Default fallback
                        Debug.LogWarning($"[WS_API] Could not determine audio length, using default: {audioLength}s");
                    }
                    else
                    {
                        Debug.Log($"[WS_API] Audio length determined: {audioLength}s");
                    }
                    
                    validationSuccess = true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS_API] Error during validation: {ex.Message}");
            Debug.LogError($"[WS_API] Stack trace: {ex.StackTrace}");
        }
        
        if (validationSuccess)
        {
            Debug.Log($"[WS_API] Song validation successful: {request.title}, length: {audioLength}s");
            
            // Add the validated song to the master's queue
            Debug.Log($"[WS_API] Adding validated song to master queue: {request.title}");
            yield return StartCoroutine(AddValidatedSongToMasterQueue(request, audioLength));
        }
        else
        {
            Debug.LogWarning($"[WS_API] Song validation failed: {request.title} from album: {request.album}");
            // Send validation response for failed validation
            yield return StartCoroutine(SendValidationResponseCoroutine(request.tracklistId, validationSuccess, audioLength));
        }
    }
    
    private System.Collections.IEnumerator AddValidatedSongToMasterQueue(ValidationRequest request, int audioLength)
    {
        Debug.Log($"[WS_API] Adding validated song to master queue: {request.title} by {request.artist}");
        
        // Find the TrackQueueManager to add the song
        TrackQueueManager trackQueueManager = FindObjectOfType<TrackQueueManager>();
        if (trackQueueManager == null)
        {
            Debug.LogError("[WS_API] TrackQueueManager not found - cannot add song to master queue");
            yield break;
        }
        
        // Use the same AddSongToTracklistAsync function that slaves use
        yield return StartCoroutine(AddSongToTracklistLikeSlaveCoroutine(request, audioLength));
        
        // Then add the song to the Unity queue
        yield return StartCoroutine(trackQueueManager.AddSongToQueueByName(request.title, audioLength, true));
        
        Debug.Log($"[WS_API] Successfully added validated song to master queue: {request.title}");
    }
    
    private System.Collections.IEnumerator AddSongToTracklistLikeSlaveCoroutine(ValidationRequest request, int audioLength)
    {
        Debug.Log($"[WS_API] Adding song to tracklist like slave: {request.title} by {request.artist}");

        // Find the actual song ID from the albums collection first
        string actualSongId = "";
        bool findComplete = false;
        bool findSuccess = false;

        Task.Run(async () => {
            try
            {
                // Find the song in the songs collection by album and title
                var songs = await MongoDBManager.Instance.GetSongsByAlbumAsync(request.album);
                foreach (var song in songs)
                {
                    if (song.Title == request.title)
                    {
                        actualSongId = song.Id;
                        findSuccess = true;
                        break;
                    }
                }
                findComplete = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WS_API] Error finding song ID: {ex.Message}");
                findSuccess = false;
                findComplete = true;
            }
        });

        yield return new WaitUntil(() => findComplete);

        if (!findSuccess)
        {
            Debug.LogError($"[WS_API] Could not find song ID for: {request.title} in album: {request.album}");
            yield break;
        }

        Debug.Log($"[WS_API] Found song ID: {actualSongId} for: {request.title}");

        // Now add to tracklist using the real song ID
        bool addComplete = false;
        bool addSuccess = false;
        string errorMessage = "";

        Task.Run(async () => {
            try
            {
                // Use the same AddSongToTracklistAsync function that slaves use
                var result = await MongoDBManager.Instance.AddSongToTracklistAsync(
                    actualSongId, // Use the real song ID
                    request.title,
                    request.artist,
                    request.album,
                    audioLength,
                    "validation", // requestedBy
                    "master", // masterId
                    1 // priority
                );
                
                addSuccess = result != null;
                addComplete = true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                addSuccess = false;
                addComplete = true;
            }
        });

        yield return new WaitUntil(() => addComplete);

        if (addSuccess)
        {
            Debug.Log($"[WS_API] Successfully added song to tracklist: {request.title}");
        }
        else
        {
            Debug.LogError($"[WS_API] Failed to add song to tracklist: {request.title}. Error: {errorMessage}");
        }
    }

    private System.Collections.IEnumerator UpdateExistingTracklistEntryCoroutine(string tracklistId, bool existsAtMaster, int length)
    {
        Debug.Log($"[WS_API] Updating existing tracklist entry: {tracklistId} with existsAtMaster={existsAtMaster}, length={length}");
        
        bool updateComplete = false;
        bool updateSuccess = false;
        string errorMessage = "";
        
        // Use MongoDBManager to update the existing tracklist entry
        Task.Run(async () => {
            try
            {
                var result = await MongoDBManager.Instance.UpdateExistsAtMasterAsync(tracklistId, existsAtMaster, length);
                updateSuccess = result;
                updateComplete = true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                updateSuccess = false;
                updateComplete = true;
            }
        });
        
        // Wait for the update to complete
        yield return new WaitUntil(() => updateComplete);
        
        if (updateSuccess)
        {
            Debug.Log($"[WS_API] Successfully updated tracklist entry: {tracklistId}");
        }
        else
        {
            Debug.LogError($"[WS_API] Failed to update tracklist entry: {tracklistId}. Error: {errorMessage}");
        }
    }
    
    private System.Collections.IEnumerator SendValidationResponseCoroutine(string tracklistId, bool existsAtMaster, int length)
    {
        // Send direct HTTP request to websocket server to trigger broadcast
        var requestData = new
        {
            tracklistId = tracklistId,
            existsAtMaster = existsAtMaster,
            length = length
        };
        
        string jsonData = JsonUtility.ToJson(requestData);
        Debug.Log($"[WS_API] Sending validation broadcast request: {jsonData}");
        
        // Use direct HTTP request to websocket server (port 8082)
        using (var request = new UnityEngine.Networking.UnityWebRequest("http://localhost:8082/api/validate", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"[WS_API] Successfully triggered validation broadcast for tracklistId: {tracklistId}");
            }
            else
            {
                Debug.LogError($"[WS_API] Failed to trigger validation broadcast: {request.error}");
            }
        }
    }
    
    private void ProcessTracklistUpdate(TracklistUpdate update)
    {
        switch (update.operationType.ToLower())
        {
            case "add":
                AddSongToLocalTracklist(update);
                break;
            case "play":
                SetCurrentSong(update);
                isPlaying = true;
                isPaused = false;
                break;
            case "pause":
                isPaused = true;
                break;
            case "resume":
                isPaused = false;
                break;
            case "skip":
                SkipCurrentSongLocal();
                break;
            case "remove":
                RemoveSongFromLocalTracklist(update.songId);
                break;
        }
    }
    
    private void AddSongToLocalTracklist(TracklistUpdate update)
    {
        var newItem = new TracklistItem
        {
            id = update.songId,
            title = update.songTitle,
            artist = update.artist,
            album = update.album,
            duration = update.duration,
            status = update.status
        };
        
        currentTracklist.Add(newItem);
        Debug.Log($"[WS_API] Added song to local tracklist: {newItem.title}");
    }
    
    private void SetCurrentSong(TracklistUpdate update)
    {
        currentSong = new TracklistItem
        {
            id = update.songId,
            title = update.songTitle,
            artist = update.artist,
            album = update.album,
            duration = update.duration,
            status = update.status
        };
        
        Debug.Log($"[WS_API] Current song set to: {currentSong.title}");
    }
    
    private void SkipCurrentSongLocal()
    {
        if (currentSong != null)
        {
            Debug.Log($"[WS_API] Skipping current song: {currentSong.title}");
            currentSong = null;
        }
    }
    
    private void RemoveSongFromLocalTracklist(string songId)
    {
        currentTracklist.RemoveAll(item => item.id == songId);
        Debug.Log($"[WS_API] Removed song from local tracklist: {songId}");
    }
    
    private void OnWebSocketError(object sender, ErrorEventArgs e)
    {
        Debug.LogError($"[WS_API] WebSocket error: {e.Message}");
        HubLogger.LogFailure($"WebSocket error: {e.Message}", LogCategory.WebSocket);
        isConnected = false;
        OnConnectionStatusChanged?.Invoke("Error");
        StartReconnectTimer();
    }
    
    private void OnWebSocketClose(object sender, CloseEventArgs e)
    {
        Debug.Log($"[WS_API] WebSocket closed: {e.Reason}");
        HubLogger.Log($"WebSocket closed: {e.Reason}", LogCategory.WebSocket);
        isConnected = false;
        OnConnectionStatusChanged?.Invoke("Disconnected");
        StartReconnectTimer();
    }
    
    private void StartReconnectTimer()
    {
        if (reconnectCoroutine == null && reconnectAttempts < maxReconnectAttempts)
        {
            reconnectCoroutine = StartCoroutine(ReconnectTimer());
        }
    }
    
    private IEnumerator ReconnectTimer()
    {
        reconnectAttempts++;
        yield return new WaitForSeconds(reconnectInterval);
        
        Debug.Log($"[WS_API] Attempting to reconnect... (Attempt {reconnectAttempts}/{maxReconnectAttempts})");
        ConnectToWebSocket();
        
        reconnectCoroutine = null;
    }
    
    // Public API methods for HTTP requests
    public void AddSongToTracklist(string songId, string title, string artist, string album, int duration)
    {
        StartCoroutine(AddSongToTracklistCoroutine(songId, title, artist, album, duration));
    }
    
    private IEnumerator AddSongToTracklistCoroutine(string songId, string title, string artist, string album, int duration)
    {
        var requestData = new
        {
            songId = songId,
            title = title,
            artist = artist,
            album = album,
            duration = duration
        };
        
        string jsonData = JsonUtility.ToJson(requestData);
        
        using (var request = new UnityEngine.Networking.UnityWebRequest($"{httpApiBaseUrl}/tracklist", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"[WS_API] Successfully added song to tracklist: {title}");
            }
            else
            {
                Debug.LogError($"[WS_API] Failed to add song to tracklist: {request.error}");
            }
        }
    }
    
    public void PauseCurrentSong()
    {
        StartCoroutine(PauseCurrentSongCoroutine());
    }
    
    private IEnumerator PauseCurrentSongCoroutine()
    {
        Debug.Log("[WS_API] PauseCurrentSongCoroutine invoked");
        if (currentSong == null)
        {
            Debug.LogWarning("[WS_API] No current song to pause (currentSong is null)");
            yield break;
        }
        
        Debug.Log($"[WS_API] Preparing pause request for: id={currentSong.id}, title={currentSong.title}, artist={currentSong.artist}, album={currentSong.album}");
        var requestData = new
        {
            tracklistId = currentSong.id,
            songTitle = currentSong.title,
            artist = currentSong.artist,
            album = currentSong.album
        };
        
        string jsonData = JsonUtility.ToJson(requestData);
        Debug.Log($"[WS_API] Pause request payload: {jsonData}");
        
        using (var request = new UnityEngine.Networking.UnityWebRequest($"{httpApiBaseUrl}/pause", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            Debug.Log($"[WS_API] Sending POST {httpApiBaseUrl}/pause");
            
            yield return request.SendWebRequest();
            
            var responseText = request.downloadHandler != null ? request.downloadHandler.text : "<no body>";
            Debug.Log($"[WS_API] /pause response code: {(int)request.responseCode}, result: {request.result}, body: {responseText}");
            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"[WS_API] Successfully paused current song: {currentSong.title}");
            }
            else
            {
                Debug.LogError($"[WS_API] Failed to pause current song: {request.error} (code {(int)request.responseCode})");
            }
        }
    }
    
    public void ResumeCurrentSong()
    {
        StartCoroutine(ResumeCurrentSongCoroutine());
    }
    
    private IEnumerator ResumeCurrentSongCoroutine()
    {
        Debug.Log("[WS_API] ResumeCurrentSongCoroutine invoked");
        if (currentSong == null)
        {
            Debug.LogWarning("[WS_API] No current song to resume (currentSong is null)");
            yield break;
        }
        
        Debug.Log($"[WS_API] Preparing resume request for: id={currentSong.id}, title={currentSong.title}, artist={currentSong.artist}, album={currentSong.album}");
        var requestData = new
        {
            tracklistId = currentSong.id,
            songTitle = currentSong.title,
            artist = currentSong.artist,
            album = currentSong.album
        };
        
        string jsonData = JsonUtility.ToJson(requestData);
        Debug.Log($"[WS_API] Resume request payload: {jsonData}");
        
        using (var request = new UnityEngine.Networking.UnityWebRequest($"{httpApiBaseUrl}/resume", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            Debug.Log($"[WS_API] Sending POST {httpApiBaseUrl}/resume");
            
            yield return request.SendWebRequest();
            
            var responseText = request.downloadHandler != null ? request.downloadHandler.text : "<no body>";
            Debug.Log($"[WS_API] /resume response code: {(int)request.responseCode}, result: {request.result}, body: {responseText}");
            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"[WS_API] Successfully resumed current song: {currentSong.title}");
            }
            else
            {
                Debug.LogError($"[WS_API] Failed to resume current song: {request.error} (code {(int)request.responseCode})");
            }
        }
    }
    
    public void SkipCurrentSong()
    {
        StartCoroutine(SkipCurrentSongCoroutine());
    }
    
    public void SetCurrentPlayingSong(string tracklistId, string songTitle, string artist, string album)
    {
        currentSong = new TracklistItem
        {
            id = tracklistId,
            title = songTitle,
            artist = artist,
            album = album,
            status = "playing"
        };
        isPlaying = true;
        isPaused = false;
        
        Debug.Log($"[WS_API] Set current playing song: {songTitle} by {artist} from {album} (status: playing)");
    }
    
    private IEnumerator SkipCurrentSongCoroutine()
    {
        Debug.Log("[WS_API] SkipCurrentSongCoroutine invoked");
        if (currentSong == null)
        {
            Debug.LogWarning("[WS_API] No current song to skip (currentSong is null)");
            yield break;
        }
        
        Debug.Log($"[WS_API] Preparing skip request for: id={currentSong.id}, title={currentSong.title}, artist={currentSong.artist}, album={currentSong.album}");

        var requestData = new
        {
            tracklistId = currentSong.id,
            songTitle = currentSong.title,
            artist = currentSong.artist,
            album = currentSong.album
        };
        
        string jsonData = JsonUtility.ToJson(requestData);
        Debug.Log($"[WS_API] Skip request payload: {jsonData}");
        
        using (var request = new UnityEngine.Networking.UnityWebRequest($"{httpApiBaseUrl}/skip", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            Debug.Log($"[WS_API] Sending POST {httpApiBaseUrl}/skip");
            
            yield return request.SendWebRequest();
            
            var responseText = request.downloadHandler != null ? request.downloadHandler.text : "<no body>";
            Debug.Log($"[WS_API] /skip response code: {(int)request.responseCode}, result: {request.result}, body: {responseText}");
            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"[WS_API] Successfully skipped current song: {currentSong.title}");
            }
            else
            {
                Debug.LogError($"[WS_API] Failed to skip current song: {request.error} (code {(int)request.responseCode})");
            }
        }
    }
    
    // Getters for current state
    public List<TracklistItem> GetCurrentTracklist() => currentTracklist;
    public TracklistItem GetCurrentSong() => currentSong;
    public bool IsPlaying() => isPlaying;
    public bool IsPaused() => isPaused;
    public bool IsConnected() => isConnected;
    
    // Helper methods for validation
    private string FindAlbumFolder(string albumName)
    {
        string basePath = PlayerPrefs.GetString("FriendlyAlbumsPath", "");
        Debug.Log($"[WS_API] Base albums path: '{basePath}'");
        
        if (string.IsNullOrEmpty(basePath)) 
        {
            Debug.LogError("[WS_API] FriendlyAlbumsPath not set in PlayerPrefs");
            return null;
        }
        
        if (!System.IO.Directory.Exists(basePath))
        {
            Debug.LogError($"[WS_API] Albums directory does not exist: {basePath}");
            return null;
        }
        
        string[] albumFolders = System.IO.Directory.GetDirectories(basePath);
        Debug.Log($"[WS_API] Found {albumFolders.Length} album folders");
        
        foreach (string folder in albumFolders)
        {
            string folderName = System.IO.Path.GetFileName(folder);
            Debug.Log($"[WS_API] Checking folder: '{folderName}' against album: '{albumName}'");
            
            if (folderName.Contains(albumName) || albumName.Contains(folderName))
            {
                Debug.Log($"[WS_API] Match found: '{folderName}'");
                return folder;
            }
        }
        
        Debug.LogWarning($"[WS_API] No album folder found for: '{albumName}'");
        return null;
    }
    
    private string FindSongFilePath(string albumPath, string songTitle)
    {
        if (string.IsNullOrEmpty(albumPath)) 
        {
            Debug.LogError("[WS_API] Album path is null or empty");
            return null;
        }
        
        Debug.Log($"[WS_API] Looking for song '{songTitle}' in album path: {albumPath}");
        
        string[] supportedExtensions = { ".mp3", ".wav", ".ogg" };
        foreach (string ext in supportedExtensions)
        {
            string fullPath = System.IO.Path.Combine(albumPath, songTitle + ext);
            Debug.Log($"[WS_API] Checking file: {fullPath}");
            if (System.IO.File.Exists(fullPath))
            {
                Debug.Log($"[WS_API] Found audio file: {fullPath}");
                return fullPath;
            }
        }
        
        // List all files in the album folder for debugging
        try
        {
            string[] allFiles = System.IO.Directory.GetFiles(albumPath);
            Debug.Log($"[WS_API] Available files in album folder ({allFiles.Length} files):");
            foreach (string file in allFiles)
            {
                Debug.Log($"[WS_API]   - {System.IO.Path.GetFileName(file)}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS_API] Error listing files in album folder: {ex.Message}");
        }
        
        Debug.LogWarning($"[WS_API] No audio file found for song: '{songTitle}'");
        return null;
    }
    
    private int GetAudioFileLength(string audioPath)
    {
        // This is a simplified version - you might want to use Unity's AudioClip loading
        // For now, return a default value
        return 180; // 3 minutes default
    }

    private bool IsValidJson(string jsonString)
    {
        try
        {
            // Try to parse the JSON to see if it's valid
            JsonUtility.FromJson<TracklistUpdate>(jsonString);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    private void LogAvailableAlbums()
    {
        try
        {
            string basePath = PlayerPrefs.GetString("FriendlyAlbumsPath", "");
            if (string.IsNullOrEmpty(basePath))
            {
                Debug.LogWarning("[WS_API] FriendlyAlbumsPath not set in PlayerPrefs");
                return;
            }
            
            if (!System.IO.Directory.Exists(basePath))
            {
                Debug.LogWarning($"[WS_API] Base path does not exist: {basePath}");
                return;
            }
            
            string[] albumFolders = System.IO.Directory.GetDirectories(basePath);
            Debug.Log($"[WS_API] Available albums ({albumFolders.Length}):");
            foreach (string folder in albumFolders)
            {
                Debug.Log($"[WS_API]   - {System.IO.Path.GetFileName(folder)}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS_API] Error logging available albums: {ex.Message}");
        }
    }
    
    private void LogAvailableFilesInAlbum(string albumPath)
    {
        try
        {
            if (!System.IO.Directory.Exists(albumPath))
            {
                Debug.LogWarning($"[WS_API] Album path does not exist: {albumPath}");
                return;
            }
            
            string[] files = System.IO.Directory.GetFiles(albumPath);
            Debug.Log($"[WS_API] Available files in album ({files.Length}):");
            foreach (string file in files)
            {
                Debug.Log($"[WS_API]   - {System.IO.Path.GetFileName(file)}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WS_API] Error logging available files: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        OnTracklistUpdate = null;
        OnConnectionStatusChanged = null;
        
        // Close WebSocket connection
        if (webSocket != null)
        {
            try
            {
                webSocket.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WS_API] Error closing WebSocket: {ex.Message}");
            }
            webSocket = null;
        }
        
        // Stop reconnect coroutine
        if (reconnectCoroutine != null)
        {
            StopCoroutine(reconnectCoroutine);
            reconnectCoroutine = null;
        }
        
        // Clear message queue to prevent memory leak
        lock (queueLock)
        {
            messageQueue.Clear();
        }
        
        // Clear state
        isConnected = false;
        reconnectAttempts = 0;
    }
}

[Serializable]
public class TracklistUpdate
{
    public string operationType; // "add", "play", "pause", "resume", "skip", "remove"
    public string songId;
    public string songTitle;
    public string artist;
    public string album;
    public int duration;
    public string status;
    public bool existsAtMaster;
    public int? length;
    public long timestamp;
}

[Serializable]
public class TracklistItem
{
    public string id;
    public string title;
    public string artist;
    public string album;
    public int duration;
    public string status;
}

[Serializable]
public class ValidationRequest
{
    public string type;
    public string tracklistId;
    public string title;
    public string artist;
    public string album;
    public string timestamp;
}
