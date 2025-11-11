using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Linq;
using System;
using System.Threading.Tasks;
using MongoDBModels;
using MongoDB.Driver;
using MongoDB.Bson;

public class TracklistLoadingResult
{
    public List<TracklistEntryDocument> ValidQueuedSongs { get; set; }
    public List<TracklistEntryDocument> ValidPlayingSongs { get; set; }
    public string ErrorMessage { get; set; }
    public bool IsComplete { get; set; }
}

public class TracklistMonitoringResult
{
    public List<TracklistEntryDocument> NewSongs { get; set; }
    public string ErrorMessage { get; set; }
    public bool IsComplete { get; set; }
}

public class TracklistClearResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public bool IsComplete { get; set; }
}

public class ValidationResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
}

public class TrackQueueManager : MonoBehaviour
{
    public Transform SongContainer;
    public Song SongPrefab;

    public TMP_Text timeText;
    public TMP_Text PlayedSongName;

    [Header("Loading Prefabs")]
    public GameObject loadingPrefab; // Main loading prefab
    public GameObject albumLoadingPrefab; // For album loading
    public GameObject mongoDBLoadingPrefab; // For MongoDB operations
    public GameObject websocketLoadingPrefab; // For WebSocket operations
    
    [Header("Loading UI")]
    public Canvas loadingCanvas; // Canvas for loading UI
    public Transform loadingParent; // Parent transform for loading prefabs
    
    [Header("Friendly Songs UI")]
    public Button chooseAlbumButton; // Button to select friendly songs folder
    public Button playFriendlyButton; // Button to start playing friendly songs
    public Button backToNormalButton; // Button to exit friendly mode and return to normal albums
    
    [Header("Album Management UI")]
    public Button scanAlbumsButton; // Button to scan for new albums/songs and update MongoDB


    private int currentSongIndex;
    private bool isPaused = false;

    private AudioSource audioSource;
    private AlbumManager albumManager;

    public List<(Song, GameObject)> queueList = new List<(Song, GameObject)>();
    public bool isSlavePlaying = false;
    public bool isFirstSong  = true;
    private MasterNetworkHandler masterNetworkHandler;
    private bool isPlaying = false;
    private Coroutine playbackCoroutine = null; // Store the coroutine for control
    private MongoDBIntegration mongoDBIntegration;
    private MongoDBManager mongoDBManager;
    private MongoDBSlaveController mongoDBSlaveController;
    private MongoDBMasterController mongoDBMasterController;
    public WebSocketTracklistAPI webSocketAPI;
    private Coroutine tracklistPollingCoroutine;
    private TracklistEntryDocument currentPlayingTrack;
    
    // Loading UI tracking
    private GameObject currentLoadingInstance;
    private Dictionary<GameObject, GameObject> activeLoadingInstances = new Dictionary<GameObject, GameObject>();
    
    // Simple auto-add tracking
    private float timeSinceStartup = 0f;
    private bool hasAddedStartupSong = false;
    private float startupDelay = 5f; // 5 seconds after startup
    
    // Continuous queue monitoring
    private float queueCheckTimer = 0f;
    private float queueCheckInterval = 2f; // Check every 2 seconds
    
    // Auto-add cooldown to prevent multiple simultaneous additions
    private float lastAutoAddTime = 0f;
    private float autoAddCooldown = 5f; // 5 seconds cooldown between auto-adds
    
    // Friendly songs mode tracking
    private bool isFriendlyMode = false;
    private void Start()
    {
        Debug.Log("[TRACKQUEUE] Starting TrackQueueManager...");
        
        albumManager = FindObjectOfType<AlbumManager>();
        if (albumManager == null)
        {
            Debug.LogError("[TRACKQUEUE] AlbumManager not found in the scene!");
            return;
        }

        mongoDBManager = MongoDBManager.Instance;
        if (mongoDBManager == null)
        {
            Debug.LogError("[TRACKQUEUE] MongoDBManager not found in the scene!");
            return;
        }
        
        Debug.Log("[TRACKQUEUE] TrackQueueManager initialized successfully");

        // Setup friendly songs button listeners
        SetupFriendlySongsButtons();
        
        // Setup scan albums button listener
        SetupScanAlbumsButton();

        // Keep running and playing audio when app is unfocused or in background
        Application.runInBackground = true;
        AudioListener.pause = false;

        // Show loading for startup processes
        ShowLoading(mongoDBLoadingPrefab, "Initializing tracklist...");
        
        // Clear tracklist entries on startup
        StartCoroutine(ClearTracklistOnStartupAsync());
        
        // Load existing tracklist entries on startup (non-blocking)
        StartCoroutine(LoadExistingTracklistOnStartupAsync());
        
        // Start real-time change stream monitoring (replaces polling)
        StartRealTimeMonitoring();
        
        // Subscribe to WebSocket tracklist updates
        if (webSocketAPI == null)
        {
            webSocketAPI = FindObjectOfType<WebSocketTracklistAPI>();
        }
        
        if (webSocketAPI != null)
        {
            WebSocketTracklistAPI.OnTracklistUpdate += OnWebSocketTracklistUpdate;
            Debug.Log("[TRACKQUEUE] Subscribed to WebSocket tracklist updates");
        }
        else
        {
            Debug.LogError("[TRACKQUEUE] WebSocketTracklistAPI not found! Please add WebSocketTracklistAPI component to the scene and assign it in the Inspector.");
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            Debug.LogError("No AudioSource component found on this GameObject!");
        }
        masterNetworkHandler = FindAnyObjectByType<MasterNetworkHandler>();
        mongoDBIntegration = FindObjectOfType<MongoDBIntegration>();
        mongoDBManager = MongoDBManager.Instance;
        mongoDBSlaveController = FindObjectOfType<MongoDBSlaveController>();
        mongoDBMasterController = FindObjectOfType<MongoDBMasterController>();
        webSocketAPI = FindObjectOfType<WebSocketTracklistAPI>();
        
        // Start polling for tracklist updates
        // DISABLED: This was causing double processing with MongoDBMasterController
        // StartTracklistPolling();
    }

    private void Update()
    {
        // Debug log to track queue count changes
        if (queueList.Count > 0)
        {
            // Queue monitoring - debug logs removed for cleaner console
        }
        
        if (audioSource.isPlaying && !albumManager.isSlave)
        {
            UpdateUI();
        }
        else
        {
            if (queueList.Count > 0)
            {
                if (albumManager.isSlave && queueList[currentSongIndex].Item1.SongLength > 0 && isSlavePlaying)
                {
                    albumManager.UpdateDebugText("Updating Slave UI");
                    UpdateSlaveUI();
                }
            }
          
        }
        
        // Auto-add random song if idle for 10 seconds on startup
        if (!albumManager.isSlave) // Only for master
        {
            UpdateSimpleAutoAdd();
        }
    }
    
    private void UpdateSimpleAutoAdd()
    {
        timeSinceStartup += Time.deltaTime;
        queueCheckTimer += Time.deltaTime;
        
        // Add random song 5 seconds after startup if queue is empty
        if (timeSinceStartup > startupDelay && !hasAddedStartupSong && queueList.Count == 0)
        {
            Debug.Log("[TRACKQUEUE] 5 seconds passed with empty queue - adding random song");
            AddRandomSongSimple();
            hasAddedStartupSong = true; // Only trigger once
        }
        
        // Continuous queue monitoring as backup (every 2 seconds)
        if (queueCheckTimer > queueCheckInterval)
        {
            queueCheckTimer = 0f;
            
            if (queueList.Count == 0 && !albumManager.isSlave && CanAutoAdd())
            {
                Debug.Log("[TRACKQUEUE] Continuous monitoring detected empty queue - adding random song");
                AddRandomSongSimple();
            }
        }
    }
    
    private bool CanAutoAdd()
    {
        float timeSinceLastAdd = Time.time - lastAutoAddTime;
        return timeSinceLastAdd >= autoAddCooldown;
    }
    
    private void AddRandomSongSimple()
    {
        if (!CanAutoAdd())
        {
            float timeRemaining = autoAddCooldown - (Time.time - lastAutoAddTime);
            Debug.Log($"[TRACKQUEUE] Auto-add on cooldown. Must wait {timeRemaining:F1}s more (cooldown: {autoAddCooldown}s)");
            return;
        }
        
        // Update cooldown timer BEFORE attempting to add (ensures 5-second wait)
        lastAutoAddTime = Time.time;
        Debug.Log($"[TRACKQUEUE] Auto-add allowed. Starting cooldown timer (next auto-add in {autoAddCooldown}s)");
        AddRandomSongWithRetry(3); // Try up to 3 times
    }
    
    private void AddRandomSongWithRetry(int maxRetries)
    {
        // If in friendly mode, use friendly songs instead
        if (isFriendlyMode)
        {
            Debug.Log("[TRACKQUEUE] In friendly mode - adding friendly song");
            AddRandomFriendlySong();
            return;
        }
        
        if (albumManager.albums.Count == 0)
        {
            Debug.LogWarning("[TRACKQUEUE] No albums available for random song selection");
            return;
        }
        
        if (string.IsNullOrEmpty(albumManager.AlbumBasePath) || !Directory.Exists(albumManager.AlbumBasePath))
        {
            Debug.LogError("[TRACKQUEUE] AlbumBasePath is not set or doesn't exist");
            return;
        }
        
        var random = new System.Random();
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Pick a random album number within bounds (1-based indexing to match albumNumber)
                int albumNumber = random.Next(1, albumManager.albums.Count + 1);
                Album randomAlbum = albumManager.albums[albumNumber - 1];
                
                if (randomAlbum.Songs.Count == 0)
                {
                    Debug.LogWarning($"[TRACKQUEUE] Album {randomAlbum.albumName} has no songs (attempt {attempt})");
                    continue;
                }
                
                // Pick a random song number within bounds (1-based indexing)
                int songNumber = random.Next(1, randomAlbum.Songs.Count + 1);
                Song randomSong = randomAlbum.Songs[songNumber - 1];
                
                // Generate keypad input format: "DD-DD" (album number - song number)
                string albumNumberStr = albumNumber.ToString("00");
                string songNumberStr = songNumber.ToString("00");
                string keypadInput = $"{albumNumberStr}-{songNumberStr}";
                
                Debug.Log($"[TRACKQUEUE] Auto-add attempt {attempt}: Generated keypad input {keypadInput} (Album: {randomAlbum.albumName}, Song index: {songNumber})");
                
                // Step 4: Check if the file actually exists in the albums directory BEFORE adding to tracklist
                string songName = randomSong.SongName;
                
                // Find the album folder path
                string albumFolderPath = albumManager.FindAlbumFolder(randomAlbum.albumName);
                if (string.IsNullOrEmpty(albumFolderPath))
                {
                    Debug.LogWarning($"[TRACKQUEUE] Album folder not found for: {randomAlbum.albumName} (attempt {attempt})");
                    continue;
                }
                
                // Check if the song file exists
                string songFilePath = albumManager.FindSongFilePath(albumFolderPath, songName);
                if (string.IsNullOrEmpty(songFilePath) || !File.Exists(songFilePath))
                {
                    Debug.LogWarning($"[TRACKQUEUE] Song file not found: {songName} in album {randomAlbum.albumName} (attempt {attempt})");
                    continue;
                }
                
                // Step 5: File exists - now add to MongoDB tracklist using the normal procedure
                Debug.Log($"[TRACKQUEUE] File verified exists: {songFilePath}. Adding to tracklist with keypad input: {keypadInput}");
                _ = AddSongToQueue(keypadInput, "auto-add");
                
                // If we get here, the song was added successfully
                Debug.Log($"[TRACKQUEUE] Successfully added random song on attempt {attempt}");
                return;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TRACKQUEUE] Failed to add random song on attempt {attempt}: {ex.Message}");
                if (attempt == maxRetries)
                {
                    Debug.LogError($"[TRACKQUEUE] All {maxRetries} attempts failed to add random song");
                }
            }
        }
    }
    
    // New method for friendly songs using MongoDB system
    public async void AddRandomFriendlySong()
    {
        Debug.Log("[TRACKQUEUE] Adding random friendly song...");
        
        string friendlyFolderPath = PlayerPrefs.GetString("FriendlyAlbumsPath", "");
        if (string.IsNullOrEmpty(friendlyFolderPath) || !Directory.Exists(friendlyFolderPath))
        {
            Debug.LogError("[TRACKQUEUE] FriendlyAlbumsPath is not set or folder doesn't exist");
            albumManager.UpdateDebugText("Please select a friendly songs folder first!");
            return;
        }
        
        try
        {
            // Get all friendly songs from MongoDB
            var allSongs = await mongoDBManager.GetAllSongsAsync();
            var friendlySongs = allSongs.Where(s => s.FamilyFriendly == true).ToList();
            
            if (friendlySongs.Count == 0)
            {
                Debug.LogWarning("[TRACKQUEUE] No friendly songs found in MongoDB");
                albumManager.UpdateDebugText("No friendly songs found in database!");
                return;
            }
            
            // Pick a random friendly song
            var random = new System.Random();
            var randomSong = friendlySongs[random.Next(friendlySongs.Count)];
            
            Debug.Log($"[TRACKQUEUE] Selected random friendly song: {randomSong.Title} from album: {randomSong.Album}");
            
            // Find the album folder
            string albumPath = albumManager.FindAlbumFolder(randomSong.Album);
            if (string.IsNullOrEmpty(albumPath))
            {
                Debug.LogWarning($"[TRACKQUEUE] Album folder not found for: {randomSong.Album}");
                albumManager.UpdateDebugText($"Album folder not found for: {randomSong.Album}");
                return;
            }
            
            // Find the audio file
            string audioPath = albumManager.FindSongFilePath(albumPath, randomSong.Title);
            if (string.IsNullOrEmpty(audioPath))
            {
                Debug.LogWarning($"[TRACKQUEUE] Audio file not found for: {randomSong.Title}");
                albumManager.UpdateDebugText($"Audio file not found for: {randomSong.Title}");
                return;
            }
            
            // Get the song length
            int songDuration = albumManager.GetAudioFileLength(audioPath);
            if (songDuration <= 0)
            {
                Debug.LogWarning($"[TRACKQUEUE] Could not get audio length for: {randomSong.Title}");
                songDuration = 180; // Default duration
            }
            
            Debug.Log($"[TRACKQUEUE] Adding friendly song to tracklist: {randomSong.Title}");
            
            // Add to MongoDB tracklist (this will trigger real-time updates)
            string masterId = albumManager.isSlave ? "slave" : "master";
            var tracklistEntry = await mongoDBManager.AddSongToTracklistAsync(
                randomSong.Id,
                randomSong.Title,
                randomSong.Artist ?? "Unknown Artist",
                randomSong.Album,
                songDuration,
                "friendly-mode",
                masterId,
                1 // Default priority
            );
            
            if (tracklistEntry != null)
            {
                Debug.Log($"[TRACKQUEUE] Successfully added friendly song to tracklist: {randomSong.Title}");
                albumManager.UpdateDebugText($"Added friendly song: {randomSong.Title}");
            }
            else
            {
                Debug.LogError($"[TRACKQUEUE] Failed to add friendly song to tracklist: {randomSong.Title}");
                albumManager.UpdateDebugText($"Failed to add friendly song: {randomSong.Title}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error adding friendly song: {ex.Message}");
            albumManager.UpdateDebugText($"Error adding friendly song: {ex.Message}");
        }
    }
    
    // Setup friendly songs button listeners
    private void SetupFriendlySongsButtons()
    {
        if (chooseAlbumButton != null)
        {
            chooseAlbumButton.onClick.AddListener(OnChooseAlbumButtonClicked);
        }
        else
        {
            Debug.LogWarning("[TRACKQUEUE] ChooseAlbumButton not assigned!");
        }
        
        if (playFriendlyButton != null)
        {
            playFriendlyButton.onClick.AddListener(OnPlayFriendlyButtonClicked);
        }
        else
        {
            Debug.LogWarning("[TRACKQUEUE] PlayFriendlyButton not assigned!");
        }
        
        if (backToNormalButton != null)
        {
            backToNormalButton.onClick.AddListener(OnBackToNormalButtonClicked);
        }
        else
        {
            Debug.LogWarning("[TRACKQUEUE] BackToNormalButton not assigned!");
        }
    }
    
    // Setup scan albums button listener
    private void SetupScanAlbumsButton()
    {
        if (scanAlbumsButton != null)
        {
            scanAlbumsButton.onClick.AddListener(OnScanAlbumsButtonClicked);
            Debug.Log("[TRACKQUEUE] ScanAlbumsButton listener set up");
        }
        else
        {
            Debug.LogWarning("[TRACKQUEUE] ScanAlbumsButton not assigned!");
        }
    }
    
    // Handle scan albums button click
    private void OnScanAlbumsButtonClicked()
    {
        Debug.Log("[TRACKQUEUE] Scan Albums button clicked");
        if (albumManager != null)
        {
            albumManager.ScanForAlbums();
        }
        else
        {
            Debug.LogError("[TRACKQUEUE] AlbumManager not found! Cannot scan albums.");
        }
    }
    
    // Handle choose album button click
    private void OnChooseAlbumButtonClicked()
    {
        Debug.Log("[TRACKQUEUE] Choose Album button clicked");
        albumManager.SelectSongsFolder();
    }
    
    // Handle play friendly button click
    private void OnPlayFriendlyButtonClicked()
    {
        Debug.Log("[TRACKQUEUE] Play Friendly button clicked");
        StartFriendlyMode();
    }
    
    // Handle back to normal button click
    private void OnBackToNormalButtonClicked()
    {
        Debug.Log("[TRACKQUEUE] Back to Normal button clicked");
        ExitFriendlyMode();
    }
    
    // Exit friendly mode and return to normal albums
    private async void ExitFriendlyMode()
    {
        Debug.Log("[TRACKQUEUE] Exiting friendly mode...");
        
        try
        {
            // Stop current playback
            StopAllPlayback();
            
            // Clear current queue
            ClearSongQueue();
            
            // Additional UI clearing - destroy any remaining song GameObjects
            if (SongContainer != null)
            {
                foreach (Transform child in SongContainer)
                {
                    if (child != null)
                    {
                        Debug.Log($"[TRACKQUEUE] Destroying remaining UI element: {child.name}");
                        Destroy(child.gameObject);
                    }
                }
            }
            
            // Clear MongoDB tracklist
            await mongoDBManager.ClearTracklistAsync();
            
            // Disable friendly mode
            isFriendlyMode = false;
            
            Debug.Log("[TRACKQUEUE] Friendly mode disabled - returning to normal albums");
            albumManager.UpdateDebugText("Returned to normal album mode");
            
            // Add first random song from normal albums
            AddRandomSongSimple();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error exiting friendly mode: {ex.Message}");
            albumManager.UpdateDebugText($"Error exiting friendly mode: {ex.Message}");
        }
    }
    
    // Start friendly mode - clear queue and start playing friendly songs
    private async void StartFriendlyMode()
    {
        Debug.Log("[TRACKQUEUE] Starting friendly mode...");
        
        // Check if friendly folder is selected
        string friendlyFolderPath = PlayerPrefs.GetString("FriendlyAlbumsPath", "");
        if (string.IsNullOrEmpty(friendlyFolderPath) || !Directory.Exists(friendlyFolderPath))
        {
            Debug.LogError("[TRACKQUEUE] FriendlyAlbumsPath is not set or folder doesn't exist");
            albumManager.UpdateDebugText("Please select a friendly songs folder first!");
            return;
        }
        
        try
        {
            // Stop ALL playback and coroutines
            StopAllPlayback();
            
            // Stop any running playback coroutines
            if (playbackCoroutine != null)
            {
                StopCoroutine(playbackCoroutine);
                playbackCoroutine = null;
            }
            
            // Stop audio source
            if (audioSource != null)
            {
                audioSource.Stop();
                audioSource.clip = null;
            }
            
            // Clear current queue (this should destroy UI elements)
            ClearSongQueue();
            
            // Additional UI clearing - destroy any remaining song GameObjects
            if (SongContainer != null)
            {
                foreach (Transform child in SongContainer)
                {
                    if (child != null)
                    {
                        Debug.Log($"[TRACKQUEUE] Destroying remaining UI element: {child.name}");
                        Destroy(child.gameObject);
                    }
                }
            }
            
            // Clear MongoDB tracklist
            await mongoDBManager.ClearTracklistAsync();
            
            // Enable friendly mode
            isFriendlyMode = true;
            
            Debug.Log("[TRACKQUEUE] Friendly mode enabled - adding first friendly song");
            albumManager.UpdateDebugText("Friendly mode started - adding songs...");
            
            // Add first friendly song
            AddRandomFriendlySong();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error starting friendly mode: {ex.Message}");
            albumManager.UpdateDebugText($"Error starting friendly mode: {ex.Message}");
        }
    }

    public float slaveCurrentTime = 0f;

    private void StartTracklistPolling()
    {
        if (tracklistPollingCoroutine != null)
        {
            StopCoroutine(tracklistPollingCoroutine);
        }
        tracklistPollingCoroutine = StartCoroutine(PollTracklistUpdates());
    }

    private IEnumerator PollTracklistUpdates()
    {
        while (true)
        {
            yield return new WaitForSeconds(2f); // Poll every 2 seconds
            
            if (mongoDBManager != null)
            {
                _ = CheckForTracklistUpdates();
            }
        }
    }

    private async Task CheckForTracklistUpdates()
    {
        try
        {
            // Check if there's a song that should be playing but isn't
            var playingSongs = await mongoDBManager.GetPlayingSongsAsync();
            var queuedSongs = await mongoDBManager.GetQueuedSongsAsync();

            // If no song is playing but there are queued songs, start the next one
            if (playingSongs.Count == 0 && queuedSongs.Count > 0 && !isPlaying)
            {
                await PlayNextSongFromTracklist();
            }
            // If a song is marked as playing but we're not playing anything, sync up
            else if (playingSongs.Count > 0 && !audioSource.isPlaying && !isPaused)
            {
                var trackToPlay = playingSongs[0];
                await LoadAndPlayTrack(trackToPlay);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error checking tracklist updates: {ex.Message}");
        }
    }

    private async Task PlayNextSongFromTracklist()
    {
        try
        {
            var nextTrack = await mongoDBManager.GetNextSongAsync();
            if (nextTrack != null)
            {
                await LoadAndPlayTrack(nextTrack);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error playing next song from tracklist: {ex.Message}");
        }
    }

    private async Task LoadAndPlayTrack(TracklistEntryDocument track)
    {
        try
        {
            currentPlayingTrack = track;
            
            // Create UI representation
            if (SongPrefab != null && SongContainer != null)
            {
                Song songInstance = Instantiate(SongPrefab, SongContainer);
                songInstance.Initialize(track.Title, track.Artist, "", track.Id);
                queueList.Add((songInstance, songInstance.gameObject));
            }

            // Update UI
            PlayedSongName.text = track.Title;
            
            // Start playback
            if (!albumManager.isSlave)
            {
                audioSource.volume = 1f;
                // Note: In a real implementation, you'd load the actual audio file here
                // For now, we'll simulate playback duration using length if available, otherwise duration
                int playbackDuration = (track.Length.HasValue && track.Length > 0) ? track.Length.Value : track.Duration;
                StartCoroutine(SimulatePlayback(playbackDuration));
            }
            else
            {
                // Slave mode - just track time
                slaveCurrentTime = 0f;
                isSlavePlaying = true;
                int playbackDuration = (track.Length.HasValue && track.Length > 0) ? track.Length.Value : track.Duration;
                StartCoroutine(SimulateSlavePlayback(playbackDuration));
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading and playing track: {ex.Message}");
        }
    }

    private IEnumerator SimulatePlayback(int duration)
    {
        isPlaying = true;
        float elapsed = 0f;

        while (elapsed < duration && isPlaying)
        {
            if (!isPaused)
            {
                elapsed += Time.deltaTime;
                UpdateUI();
            }
            yield return null;
        }

        // Song finished — call async method safely (do not await in coroutine)
        FireAndForget(MarkCurrentSongAsPlayed());
        isPlaying = false;
    }

    private IEnumerator SimulateSlavePlayback(int duration)
    {
        float elapsed = 0f;

        while (elapsed < duration && isSlavePlaying)
        {
            if (!isPaused)
            {
                elapsed += Time.deltaTime;
                slaveCurrentTime = elapsed;
                UpdateSlaveUI();
            }
            yield return null;
        }

        // Song finished — call async method safely (do not await in coroutine)
        FireAndForget(MarkCurrentSongAsPlayed());
        isSlavePlaying = false;
    }

    private async Task MarkCurrentSongAsPlayed()
    {
        if (currentPlayingTrack != null)
        {
            await mongoDBManager.MarkSongAsPlayedAsync(currentPlayingTrack.Id);
            currentPlayingTrack = null;
        }
    }

    private void UpdateUI()
    {
        if (audioSource.clip != null)
        {
            float currentTime = audioSource.time;
            float totalTime = audioSource.clip.length;
            timeText.text = FormatTime(currentTime) + "/" + FormatTime(totalTime);
        }
     
    }

    private void OnWebSocketTracklistUpdate(TracklistUpdate update)
    {
        Debug.Log($"[TRACKQUEUE] WebSocket tracklist update received: {update.operationType} - {update.songTitle} - Status: {update.status}");
        
        switch (update.operationType.ToLower())
        {
            case "insert":
            case "add":
                // Song added to tracklist via HTTP - add to Unity queue
                StartCoroutine(AddSongFromWebSocketUpdate(update));
                break;
            case "update":
                // Song updated in tracklist
                Debug.Log($"[TRACKQUEUE] Song updated: {update.songTitle} - Status: {update.status}");
                HandleSongStatusUpdate(update);
                break;
            case "play":
                // Song started playing
                Debug.Log($"[TRACKQUEUE] Song started playing: {update.songTitle}");
                break;
            case "pause":
                // Song paused
                Debug.Log($"[TRACKQUEUE] WebSocket pause command - isPlaying: {isPlaying}, isPaused: {isPaused}");
                if (isPlaying && !isPaused)
                {
                    Debug.Log($"[TRACKQUEUE] Executing pause command");
                    PauseResumeSong();
                }
                break;
            case "resume":
                // Song resumed
                Debug.Log($"[TRACKQUEUE] WebSocket resume command - isPlaying: {isPlaying}, isPaused: {isPaused}");
                if (isPlaying && isPaused)
                {
                    Debug.Log($"[TRACKQUEUE] Executing resume command");
                    PauseResumeSong();
                }
                break;
            case "skip":
                // Song skipped
                Debug.Log($"[TRACKQUEUE] WebSocket skip command - isPlaying: {isPlaying}");
                if (isPlaying)
                {
                    Debug.Log($"[TRACKQUEUE] Executing skip command");
                    SkipCurrentSong();
                }
                break;
        }
    }

    private void HandleSongStatusUpdate(TracklistUpdate update)
    {
        // Handle status changes from other clients
        switch (update.status?.ToLower())
        {
            case "playing":
                Debug.Log($"[TRACKQUEUE] Song started playing on another client: {update.songTitle}");
                break;
            case "paused":
                Debug.Log($"[TRACKQUEUE] Song paused on another client: {update.songTitle}");
                break;
            case "skipped":
                Debug.Log($"[TRACKQUEUE] Song skipped on another client: {update.songTitle}");
                break;
        }
    }

    private IEnumerator AddSongFromWebSocketUpdate(TracklistUpdate update)
    {
        // Check if song is already in Unity queue to prevent infinite loop
        if (IsSongAlreadyInUnityQueueByName(update.songTitle))
        {
            Debug.Log($"[TRACKQUEUE] Song already in Unity queue, skipping WebSocket add: {update.songTitle}");
            yield break;
        }
        
        // Show loading for WebSocket song addition
        ShowLoading(websocketLoadingPrefab, $"Receiving song: {update.songTitle}");
        
        // Find the song in albums and add to Unity queue
        if (albumManager != null)
        {
            string albumPath = albumManager.FindAlbumFolder(update.album);
            if (!string.IsNullOrEmpty(albumPath))
            {
                string audioPath = albumManager.FindSongFilePath(albumPath, update.songTitle);
                if (!string.IsNullOrEmpty(audioPath))
                {
                    int audioLength = albumManager.GetAudioFileLength(audioPath);
                    
                    // Create MongoDBModels.SongDocument for the song
                    var mongoSong = new MongoDBModels.SongDocument
                    {
                        Id = update.songId ?? System.Guid.NewGuid().ToString(),
                        Title = update.songTitle,
                        Album = update.album,
                        FamilyFriendly = true
                    };
                    
                    // Use AddSongToQueueWithPathAndMongoDB to follow the same path as manual input
                    yield return StartCoroutine(AddSongToQueueWithPathAndMongoDB(
                        update.songTitle, 
                        audioPath, 
                        audioLength, 
                        mongoSong, 
                        update.album, 
                        update.artist, 
                        "websocket"
                    ));
                    
                    Debug.Log($"[TRACKQUEUE] Added song from WebSocket: {update.songTitle}");
                }
                else
                {
                    Debug.LogWarning($"[TRACKQUEUE] Audio file not found for WebSocket song: {update.songTitle}");
                }
            }
            else
            {
                Debug.LogWarning($"[TRACKQUEUE] Album not found for WebSocket song: {update.album}");
            }
        }
        
        // Hide loading after WebSocket operation
        HideLoading(websocketLoadingPrefab);
        
        yield return null;
    }

    private void StartRealTimeMonitoring()
    {
        Debug.Log("[TRACKQUEUE] Starting real-time MongoDB change stream monitoring...");
        
        // Set the main thread dispatcher for MongoDB change stream
        mongoDBManager.SetMainThreadDispatcher(this);
        
        // Subscribe to MongoDB change stream events
        MongoDBManager.OnTracklistChanged += OnTracklistChangeDetected;
        
        // Start the change stream in the background
        _ = mongoDBManager.StartTracklistChangeStream();
    }

    private void OnTracklistChangeDetected(ChangeStreamDocument<TracklistEntryDocument> change)
    {
        Debug.Log($"[TRACKQUEUE] Real-time change detected: {change.OperationType}");
        
        // Process the change on the main thread
        StartCoroutine(ProcessRealTimeChange(change));
    }

    private IEnumerator ProcessRealTimeChange(ChangeStreamDocument<TracklistEntryDocument> change)
    {
        try
        {
            if (change.OperationType == ChangeStreamOperationType.Insert)
            {
                // New song added to tracklist
                var newSong = change.FullDocument;
                if (newSong != null)
                {
                    Debug.Log($"[TRACKQUEUE] Real-time: New song added - {newSong.Title}");
                    
                    // Start async validation
                    StartCoroutine(ValidateAndAddSongRealTimeCoroutine(newSong));
                }
            }
            else if (change.OperationType == ChangeStreamOperationType.Update || 
                     change.OperationType == ChangeStreamOperationType.Replace)
            {
                // Song updated in tracklist
                var updatedSong = change.FullDocument;
                if (updatedSong != null)
                {
                    Debug.Log($"[TRACKQUEUE] Real-time: Song updated - {updatedSong.Title}");
                    
                    // Handle status changes (e.g., queued -> playing)
                    if (updatedSong.Status == "playing" && !IsSongAlreadyInUnityQueue(updatedSong))
                    {
                        StartCoroutine(ValidateAndAddSongRealTimeCoroutine(updatedSong));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error processing real-time change: {ex.Message}");
        }
        
        yield return null;
    }

    private IEnumerator ValidateAndAddSongRealTimeCoroutine(TracklistEntryDocument song)
    {
        // If song already exists at master, add it directly
        if (song.ExistsAtMaster)
        {
            StartCoroutine(LoadTrackFromMongoDB(song, song.Status == "playing"));
            Debug.Log($"[TRACKQUEUE] Real-time: Successfully added {song.Title} to Unity queue");
            yield break;
        }
        
        // Otherwise, validate it first using async wrapper
        var validationResult = new ValidationResult();
        yield return StartCoroutine(ValidateAndUpdateSongIfNeededCoroutine(song, validationResult));
        
        if (validationResult.Success)
        {
            StartCoroutine(LoadTrackFromMongoDB(song, song.Status == "playing"));
            Debug.Log($"[TRACKQUEUE] Real-time: Successfully added {song.Title} to Unity queue");
        }
        else
        {
            Debug.LogWarning($"[TRACKQUEUE] Real-time: Failed to validate {song.Title}");
        }
    }

    private IEnumerator ValidateAndUpdateSongIfNeededCoroutine(TracklistEntryDocument song, ValidationResult result)
    {
        Debug.Log($"[TRACKQUEUE] Validating song: {song.Title} - Album: {song.Album}");
        
        string albumPath = null;
        string audioPath = null;
        int audioLength = 0;
        bool validationError = false;
        string errorMessage = "";
        
        try
        {
            // Find album folder
            albumPath = albumManager.FindAlbumFolder(song.Album);
            if (string.IsNullOrEmpty(albumPath))
            {
                Debug.LogWarning($"[TRACKQUEUE] Album folder not found for: {song.Album}");
                validationError = true;
                errorMessage = "Album folder not found";
            }
            else
            {
                // Find audio file
                audioPath = albumManager.FindSongFilePath(albumPath, song.Title);
                if (string.IsNullOrEmpty(audioPath))
                {
                    Debug.LogWarning($"[TRACKQUEUE] Audio file not found for: {song.Title} in {albumPath}");
                    validationError = true;
                    errorMessage = "Audio file not found";
                }
                else
                {
                    // File exists, get the audio file length
                    Debug.Log($"[TRACKQUEUE] Found valid audio file for {song.Title}: {audioPath}");
                    audioLength = albumManager.GetAudioFileLength(audioPath);
                    if (audioLength <= 0)
                    {
                        Debug.LogWarning($"[TRACKQUEUE] Could not get audio length for {song.Title}");
                        validationError = true;
                        errorMessage = "Could not get audio length";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error validating song {song.Title}: {ex.Message}");
            validationError = true;
            errorMessage = ex.Message;
        }
        
        // Update MongoDB based on validation results (outside try-catch)
        if (validationError)
        {
            yield return StartCoroutine(UpdateExistsAtMasterCoroutine(song.Id, false));
            result.Success = false;
        }
        else
        {
            Debug.Log($"[TRACKQUEUE] Audio file length: {audioLength} seconds for {song.Title}");
            yield return StartCoroutine(UpdateExistsAtMasterCoroutine(song.Id, true, audioLength));
            result.Success = true;
        }
    }

    private IEnumerator UpdateExistsAtMasterCoroutine(string tracklistId, bool existsAtMaster, int length = 0)
    {
        bool updateComplete = false;
        bool updateSuccess = false;
        string errorMessage = null;

        // Start async update
        _ = UpdateExistsAtMasterAsync(tracklistId, existsAtMaster, length, updateComplete, updateSuccess, errorMessage);

        // Wait for update to complete
        yield return new WaitUntil(() => updateComplete);

        if (!updateSuccess)
        {
            Debug.LogError($"[TRACKQUEUE] Failed to update existsAtMaster: {errorMessage}");
        }
    }

    private IEnumerator DeleteTracklistEntryCoroutine(string tracklistId)
    {
        bool deleteComplete = false;
        bool deleteSuccess = false;
        string errorMessage = null;

        // Start async delete
        _ = DeleteTracklistEntryAsync(tracklistId, deleteComplete, deleteSuccess, errorMessage);

        // Wait for delete to complete
        yield return new WaitUntil(() => deleteComplete);

        if (!deleteSuccess)
        {
            Debug.LogError($"[TRACKQUEUE] Failed to delete tracklist entry: {errorMessage}");
        }
        else
        {
            Debug.Log($"[TRACKQUEUE] Successfully deleted tracklist entry: {tracklistId}");
        }
    }

    private async Task UpdateExistsAtMasterAsync(string tracklistId, bool existsAtMaster, int length, bool complete, bool success, string error)
    {
        try
        {
            bool result = await mongoDBManager.UpdateExistsAtMasterAsync(tracklistId, existsAtMaster, length);
            success = result;
            complete = true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            success = false;
            complete = true;
        }
    }

    private async Task DeleteTracklistEntryAsync(string tracklistId, bool complete, bool success, string error)
    {
        try
        {
            bool result = await mongoDBManager.DeleteTracklistEntryAsync(tracklistId);
            success = result;
            complete = true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            success = false;
            complete = true;
        }
    }

    private async Task<bool> ValidateAndAddSongRealTime(TracklistEntryDocument song)
    {
        try
        {
            // If song already exists at master, add it directly
            if (song.ExistsAtMaster)
            {
                StartCoroutine(LoadTrackFromMongoDB(song, song.Status == "playing"));
                return true;
            }
            
            // Otherwise, validate it first
            if (await ValidateAndUpdateSongIfNeeded(song))
            {
                StartCoroutine(LoadTrackFromMongoDB(song, song.Status == "playing"));
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error validating song in real-time: {ex.Message}");
            return false;
        }
    }

    private IEnumerator ClearTracklistOnStartupAsync()
    {
        Debug.Log("[TRACKQUEUE] Clearing tracklist entries on startup...");
        albumManager.UpdateDebugText("Clearing tracklist...");

        // Wait a moment for MongoDB to be fully initialized
        yield return new WaitForSeconds(1f);

        bool clearSuccess = false;
        string errorMessage = null;

        // Use async operation without blocking
        var clearResult = new TracklistClearResult();
        _ = ClearTracklistDataAsync(clearResult);

        // Wait for clear operation to complete
        yield return new WaitUntil(() => clearResult.IsComplete);

        clearSuccess = clearResult.Success;
        errorMessage = clearResult.ErrorMessage;

        if (clearSuccess)
        {
            Debug.Log("[TRACKQUEUE] Successfully cleared tracklist entries");
            albumManager.UpdateDebugText("Tracklist cleared successfully");
        }
        else
        {
            Debug.LogError($"[TRACKQUEUE] Failed to clear tracklist: {errorMessage}");
            albumManager.UpdateDebugText($"Failed to clear tracklist: {errorMessage}");
        }
        
        // Hide loading after clear operation completes
        HideLoading(mongoDBLoadingPrefab);
        
        // Validate and clean up existing tracklist entries
        StartCoroutine(ValidateAndCleanupTracklistAsync());
    }

    private async Task ClearTracklistDataAsync(TracklistClearResult result)
    {
        try
        {
            bool clearResult = await mongoDBManager.ClearTracklistAsync();
            result.Success = clearResult;
            if (!clearResult)
            {
                result.ErrorMessage = "Clear operation returned false";
            }
            result.IsComplete = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            result.Success = false;
            result.IsComplete = true;
            Debug.LogError($"[TRACKQUEUE] Error clearing tracklist: {ex.Message}");
        }
    }

    private IEnumerator ValidateAndCleanupTracklistAsync()
    {
        Debug.Log("[TRACKQUEUE] Starting tracklist validation and cleanup...");
        ShowLoading(mongoDBLoadingPrefab, "Validating tracklist entries...");
        
        bool validationComplete = false;
        int totalEntries = 0;
        int invalidEntries = 0;
        string errorMessage = "";

        Task.Run(async () => {
            try
            {
                // Get all tracklist entries
                var allEntries = await mongoDBManager.GetAllTracklistEntriesAsync();
                totalEntries = allEntries.Count;
                
                Debug.Log($"[TRACKQUEUE] Found {totalEntries} tracklist entries to validate");
                
                foreach (var entry in allEntries)
                {
                    // Check if the audio file exists
                    bool audioExists = await ValidateAudioFileExists(entry);
                    
                    if (!audioExists)
                    {
                        Debug.Log($"[TRACKQUEUE] Audio file not found for: {entry.Title} by {entry.Artist} - removing from tracklist");
                        
                        // Delete the entry from MongoDB
                        await mongoDBManager.DeleteTracklistEntryAsync(entry.Id);
                        invalidEntries++;
                    }
                }
                
                validationComplete = true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                validationComplete = true;
            }
        });

        yield return new WaitUntil(() => validationComplete);

        if (string.IsNullOrEmpty(errorMessage))
        {
            Debug.Log($"[TRACKQUEUE] Tracklist cleanup completed: {invalidEntries}/{totalEntries} invalid entries removed");
            albumManager.UpdateDebugText($"Tracklist cleaned: {invalidEntries} invalid entries removed");
        }
        else
        {
            Debug.LogError($"[TRACKQUEUE] Error during tracklist cleanup: {errorMessage}");
            albumManager.UpdateDebugText($"Tracklist cleanup failed: {errorMessage}");
        }
        
        HideLoading(mongoDBLoadingPrefab);
    }

    private async Task<bool> ValidateAudioFileExists(TracklistEntryDocument entry)
    {
        try
        {
            // Find the album folder
            string albumPath = albumManager.FindAlbumFolder(entry.Album);
            if (string.IsNullOrEmpty(albumPath))
            {
                Debug.Log($"[TRACKQUEUE] Album folder not found: {entry.Album}");
                return false;
            }

            // Find the audio file
            string audioPath = albumManager.FindSongFilePath(albumPath, entry.Title);
            if (string.IsNullOrEmpty(audioPath))
            {
                Debug.Log($"[TRACKQUEUE] Audio file not found: {entry.Title} in {entry.Album}");
                return false;
            }

            // Check if file actually exists on disk
            bool fileExists = System.IO.File.Exists(audioPath);
            if (!fileExists)
            {
                Debug.Log($"[TRACKQUEUE] File does not exist on disk: {audioPath}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error validating audio file for {entry.Title}: {ex.Message}");
            return false;
        }
    }

    private IEnumerator LoadExistingTracklistOnStartupAsync()
    {
        Debug.Log("[TRACKQUEUE] Loading existing tracklist entries on startup...");
        albumManager.UpdateDebugText("Loading existing tracklist...");

        // Wait a moment for MongoDB to be fully initialized
        yield return new WaitForSeconds(2f);

        List<TracklistEntryDocument> validQueuedSongs = null;
        List<TracklistEntryDocument> validPlayingSongs = null;
        string errorMessage = null;

        // Use WaitUntil to wait for async operations without blocking
        var loadingResult = new TracklistLoadingResult();
        
        // Start async loading in background
        _ = LoadTracklistDataAsync(loadingResult);
        
        // Wait until loading is complete
        yield return new WaitUntil(() => loadingResult.IsComplete);
        
        // Get results from the wrapper
        validQueuedSongs = loadingResult.ValidQueuedSongs;
        validPlayingSongs = loadingResult.ValidPlayingSongs;
        errorMessage = loadingResult.ErrorMessage;

        if (errorMessage != null)
        {
            yield break;
        }

        // Process playing songs first (they should start playing immediately)
        foreach (var track in validPlayingSongs)
        {
            Debug.Log($"[TRACKQUEUE] Loading playing song: {track.Title}");
            yield return StartCoroutine(LoadTrackFromMongoDB(track, true));
        }

        // Process queued songs (add to queue but don't start playing yet)
        foreach (var track in validQueuedSongs)
        {
            Debug.Log($"[TRACKQUEUE] Loading queued song: {track.Title}");
            yield return StartCoroutine(LoadTrackFromMongoDB(track, false));
        }

        // If we loaded any playing songs, start playback
        if (validPlayingSongs.Count > 0 && !albumManager.isSlave)
        {
            Debug.Log("[TRACKQUEUE] Starting playback of loaded playing songs");
            PlayQueue();
        }

        albumManager.UpdateDebugText($"Loaded {validQueuedSongs.Count + validPlayingSongs.Count} songs from tracklist");
    }

    private async Task LoadTracklistDataAsync(TracklistLoadingResult result)
    {
        try
        {
            // Get all queued and playing songs
            var queuedSongs = await mongoDBManager.GetQueuedSongsAsync();
            var playingSongs = await mongoDBManager.GetPlayingSongsAsync();
            
            Debug.Log($"[TRACKQUEUE] Raw tracklist data - Queued: {queuedSongs.Count}, Playing: {playingSongs.Count}");
            
            // Debug: Log details of each song
            foreach (var song in queuedSongs)
            {
                Debug.Log($"[TRACKQUEUE] Queued song: {song.Title} - ExistsAtMaster: {song.ExistsAtMaster}, Length: {song.Length?.ToString() ?? "null"}");
            }
            foreach (var song in playingSongs)
            {
                Debug.Log($"[TRACKQUEUE] Playing song: {song.Title} - ExistsAtMaster: {song.ExistsAtMaster}, Length: {song.Length?.ToString() ?? "null"}");
            }
            
            // Filter songs that exist at master, or validate them if they don't
            var validQueuedSongs = new List<TracklistEntryDocument>();
            var validPlayingSongs = new List<TracklistEntryDocument>();
            
            // Process queued songs
            foreach (var song in queuedSongs)
            {
                if (song.ExistsAtMaster)
                {
                    validQueuedSongs.Add(song);
                }
                else
                {
                    // Try to validate this song
                    Debug.Log($"[TRACKQUEUE] Attempting to validate queued song: {song.Title}");
                    if (await ValidateAndUpdateSongIfNeeded(song))
                    {
                        validQueuedSongs.Add(song);
                    }
                }
            }
            
            // Process playing songs
            foreach (var song in playingSongs)
            {
                if (song.ExistsAtMaster)
                {
                    validPlayingSongs.Add(song);
                }
                else
                {
                    // Try to validate this song
                    Debug.Log($"[TRACKQUEUE] Attempting to validate playing song: {song.Title}");
                    if (await ValidateAndUpdateSongIfNeeded(song))
                    {
                        validPlayingSongs.Add(song);
                    }
                }
            }
            
            result.ValidQueuedSongs = validQueuedSongs;
            result.ValidPlayingSongs = validPlayingSongs;

            Debug.Log($"[TRACKQUEUE] After filtering - Valid queued: {result.ValidQueuedSongs.Count}, Valid playing: {result.ValidPlayingSongs.Count}");
            result.IsComplete = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error loading existing tracklist: {ex.Message}");
            albumManager.UpdateDebugText($"Error loading tracklist: {ex.Message}");
            result.ErrorMessage = ex.Message;
            result.IsComplete = true;
        }
    }

    private async Task<bool> ValidateAndUpdateSongIfNeeded(TracklistEntryDocument song)
    {
        try
        {
            Debug.Log($"[TRACKQUEUE] Validating song: {song.Title} - Album: {song.Album}");
            
            // Find album folder
            string albumPath = albumManager.FindAlbumFolder(song.Album);
            if (string.IsNullOrEmpty(albumPath))
            {
                Debug.LogWarning($"[TRACKQUEUE] Album folder not found for: {song.Album}");
                await mongoDBManager.UpdateExistsAtMasterAsync(song.Id, false);
                return false;
            }
            
            // Find audio file
            string audioPath = albumManager.FindSongFilePath(albumPath, song.Title);
            if (string.IsNullOrEmpty(audioPath))
            {
                Debug.LogWarning($"[TRACKQUEUE] Audio file not found for: {song.Title} in {albumPath}");
                await mongoDBManager.UpdateExistsAtMasterAsync(song.Id, false);
                return false;
            }
            
            // File exists, get the audio file length and update both existsAtMaster and length
            Debug.Log($"[TRACKQUEUE] Found valid audio file for {song.Title}: {audioPath}");
            int audioLength = albumManager.GetAudioFileLength(audioPath);
            if (audioLength > 0)
            {
                Debug.Log($"[TRACKQUEUE] Audio file length: {audioLength} seconds for {song.Title}");
                await mongoDBManager.UpdateExistsAtMasterAsync(song.Id, true, audioLength);
                return true;
            }
            else
            {
                Debug.LogWarning($"[TRACKQUEUE] Could not get audio length for {song.Title}, setting existsAtMaster to false");
                await mongoDBManager.UpdateExistsAtMasterAsync(song.Id, false);
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error validating song {song.Title}: {ex.Message}");
            await mongoDBManager.UpdateExistsAtMasterAsync(song.Id, false);
            return false;
        }
    }

    private async Task LoadMonitoringDataAsync(TracklistMonitoringResult result)
    {
        try
        {
            // Get all queued songs
            var queuedSongs = await mongoDBManager.GetQueuedSongsAsync();
            
            Debug.Log($"[TRACKQUEUE] Monitoring - Found {queuedSongs.Count} queued songs in MongoDB");
            
            // Process songs and validate if needed
            var validSongs = new List<TracklistEntryDocument>();
            
            foreach (var song in queuedSongs)
            {
                if (song.ExistsAtMaster)
                {
                    validSongs.Add(song);
                }
                else
                {
                    // Try to validate this song
                    Debug.Log($"[TRACKQUEUE] Monitoring - Attempting to validate queued song: {song.Title}");
                    if (await ValidateAndUpdateSongIfNeeded(song))
                    {
                        validSongs.Add(song);
                    }
                }
            }
            
            Debug.Log($"[TRACKQUEUE] Monitoring - After validation: {validSongs.Count} valid songs");
            
            // Filter out songs already in Unity queue
            result.NewSongs = validSongs.Where(song => !IsSongAlreadyInUnityQueue(song)).ToList();
            
            Debug.Log($"[TRACKQUEUE] Monitoring - New songs to add: {result.NewSongs.Count}");
            result.IsComplete = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error monitoring tracklist changes: {ex.Message}");
            result.ErrorMessage = ex.Message;
            result.IsComplete = true;
        }
    }

    private IEnumerator LoadTrackFromMongoDB(TracklistEntryDocument track, bool shouldPlay)
    {
        // Find the album folder for this track
        string albumPath = albumManager.FindAlbumFolder(track.Album);
        if (string.IsNullOrEmpty(albumPath))
        {
            Debug.LogWarning($"[TRACKQUEUE] Album folder not found for: {track.Album}");
            yield break;
        }

        // Find the audio file for this track
        string audioPath = albumManager.FindSongFilePath(albumPath, track.Title);
        if (string.IsNullOrEmpty(audioPath))
        {
            Debug.LogWarning($"[TRACKQUEUE] Audio file not found for: {track.Title}");
            yield break;
        }

        Debug.Log($"[TRACKQUEUE] Found valid path for {track.Title}: {audioPath}");

        // Set currentPlayingTrack so skip functionality works
        currentPlayingTrack = track;

        // Add to Unity queue WITHOUT adding to MongoDB (since this is already a tracklist entry)
        int songDuration = (track.Length.HasValue && track.Length > 0) ? track.Length.Value : track.Duration;
        yield return StartCoroutine(AddSongToQueueWithPathOnly(track.Title, audioPath, songDuration, false));

        // If this was a playing song, mark it as playing in MongoDB
        if (shouldPlay && mongoDBMasterController != null)
        {
            try
            {
                _ = NotifyMongoDBSongPlaying(track.Title);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TRACKQUEUE] Error notifying MongoDB of playing song {track.Title}: {ex.Message}");
            }
        }
    }

    private IEnumerator MonitorTracklistChanges()
    {
        // Deprecated: polling-based monitoring is disabled.
        // Real-time updates are handled via MongoDB Change Streams and/or WebSocket events.
        Debug.Log("[TRACKQUEUE] Polling monitor disabled. Using real-time Change Streams/WebSocket updates instead.");
        yield break;
    }

    private bool IsSongAlreadyInUnityQueue(TracklistEntryDocument track)
    {
        bool isAlreadyInQueue = queueList.Any(q => q.Item1.SongName == track.Title);
        if (isAlreadyInQueue)
        {
            Debug.Log($"[TRACKQUEUE] Song '{track.Title}' is already in Unity queue (Queue count: {queueList.Count})");
        }
        return isAlreadyInQueue;
    }

    private bool IsSongAlreadyInUnityQueueByName(string songTitle)
    {
        bool isAlreadyInQueue = queueList.Any(q => q.Item1.SongName == songTitle);
        if (isAlreadyInQueue)
        {
            Debug.Log($"[TRACKQUEUE] Song '{songTitle}' is already in Unity queue (Queue count: {queueList.Count})");
        }
        return isAlreadyInQueue;
    }

    // Loading prefab helper methods
    private void ShowLoading(GameObject loadingPrefab, string message = "")
    {
        if (loadingPrefab == null)
        {
            Debug.LogWarning("[LOADING] Loading prefab is null, cannot show loading");
            return;
        }

        // Hide any existing loading instance for this prefab
        if (activeLoadingInstances.ContainsKey(loadingPrefab))
        {
            HideLoading(loadingPrefab);
        }

        // Get the parent transform (use loadingParent if assigned, otherwise find Canvas)
        Transform parentTransform = loadingParent;
        if (parentTransform == null)
        {
            // Find or create a Canvas for loading UI
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                // Create a new Canvas for loading UI
                GameObject canvasGO = new GameObject("LoadingCanvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000; // High sorting order to appear on top
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
            }
            parentTransform = canvas.transform;
        }

        // Instantiate the loading prefab
        GameObject loadingInstance = Instantiate(loadingPrefab, parentTransform);
        
        // Center it on screen
        RectTransform rectTransform = loadingInstance.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
        }
        else
        {
            // For non-UI GameObjects, position in world space
            loadingInstance.transform.position = Vector3.zero;
        }

        // Store the instance
        activeLoadingInstances[loadingPrefab] = loadingInstance;
        currentLoadingInstance = loadingInstance;

        if (!string.IsNullOrEmpty(message))
        {
            Debug.Log($"[LOADING] {message}");
        }
    }

    private void HideLoading(GameObject loadingPrefab)
    {
        if (loadingPrefab == null) return;

        if (activeLoadingInstances.ContainsKey(loadingPrefab))
        {
            GameObject instance = activeLoadingInstances[loadingPrefab];
            if (instance != null)
            {
                Destroy(instance);
            }
            activeLoadingInstances.Remove(loadingPrefab);
        }

        if (currentLoadingInstance != null && currentLoadingInstance == activeLoadingInstances.Values.FirstOrDefault())
        {
            currentLoadingInstance = null;
        }
    }

    public IEnumerator AddSongToQueueByName(string songFileName, float length = 0f, bool isFromSlave = false)
    {
        Debug.Log($"[TRACKQUEUE] AddSongToQueueByName called - Song: {songFileName}, Length: {length}, FromSlave: {isFromSlave}");
        albumManager.UpdateDebugText($"Trying to add song: {songFileName}");

        // Use lazy path resolution - search for the song in album folders
        string fullPath = null;
        
        // First try to find in album folders using AlbumManager
        if (albumManager != null && !string.IsNullOrEmpty(albumManager.AlbumBasePath))
        {
            Debug.Log($"[TRACKQUEUE] Searching for song '{songFileName}' in album folders...");
            var albumFolders = Directory.GetDirectories(albumManager.AlbumBasePath);
            
            foreach (var albumFolder in albumFolders)
            {
                string foundPath = albumManager.FindSongFilePath(albumFolder, songFileName);
                if (!string.IsNullOrEmpty(foundPath))
                {
                    fullPath = foundPath;
                    Debug.Log($"[TRACKQUEUE] Found song in album folder: {fullPath}");
                    break;
                }
            }
        }
        
        // Fallback to old method if not found in album folders
        if (string.IsNullOrEmpty(fullPath))
        {
            Debug.Log($"[TRACKQUEUE] Song not found in album folders, trying old method...");
        string folderPath = PlayerPrefs.GetString("FriendlyAlbumsPath", "");

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
                Debug.LogError($"[TRACKQUEUE] FriendlyAlbumsPath is invalid or does not exist: {folderPath}");
            albumManager.UpdateDebugText("FriendlyAlbumsPath is invalid or does not exist.");
            yield break;
        }

        string[] supportedExtensions = { ".mp3", ".wav", ".ogg" };

            // First try to find in root folder (for backward compatibility)
            fullPath = Directory.GetFiles(folderPath)
                                   .FirstOrDefault(f =>
                                       Path.GetFileName(f).Equals(songFileName, StringComparison.OrdinalIgnoreCase) &&
                                       supportedExtensions.Contains(Path.GetExtension(f).ToLower()));

            // If not found in root, search in all subfolders (album folders)
        if (string.IsNullOrEmpty(fullPath))
        {
                Debug.Log($"[TRACKQUEUE] Song '{songFileName}' not found in root folder, searching subfolders...");
                var allFiles = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                fullPath = allFiles.FirstOrDefault(f =>
                    Path.GetFileName(f).Equals(songFileName, StringComparison.OrdinalIgnoreCase) &&
                    supportedExtensions.Contains(Path.GetExtension(f).ToLower()));
            }
        }

        if (string.IsNullOrEmpty(fullPath))
        {
            Debug.LogError($"[TRACKQUEUE] Song '{songFileName}' not found in any location");
            albumManager.UpdateDebugText($"Song '{songFileName}' not found in any location.");
            yield break;
        }

        Debug.Log($"[TRACKQUEUE] Found song file: {fullPath}");

        if (SongPrefab == null || SongContainer == null)
        {
            Debug.LogError($"[TRACKQUEUE] SongPrefab or SongContainer is null! SongPrefab: {SongPrefab}, SongContainer: {SongContainer}");
            albumManager.UpdateDebugText("SongPrefab or SongContainer is null!");
            yield break;
        }

        string fullFileName = Path.GetFileNameWithoutExtension(fullPath);
        
        // Split by dash and take only the first part for the song name
        string songName;
        if (fullFileName.Contains(" - "))
        {
            var parts = fullFileName.Split(new string[] { " - " }, 2, StringSplitOptions.None);
            songName = parts[1].Trim();
        }
        else
        {
            songName = fullFileName;
        }
        
        Debug.Log($"[TRACKQUEUE] Creating song instance: {songName} (from: {fullFileName})");

        Song songInstance = Instantiate(SongPrefab, SongContainer);
        songInstance.Initialize(songName, "Unknown Artist", fullPath, songName); // Use songName as identifier
        queueList.Add((songInstance, songInstance.gameObject));
        
        Debug.Log($"[TRACKQUEUE] Added song to queue list: {songName} (GameObject: {songInstance.gameObject.name})");
        Debug.Log($"[TRACKQUEUE] Total songs in queue: {queueList.Count}");
        
        // Debug: List all songs in queue after adding
        Debug.Log($"[TRACKQUEUE] Current queue contents:");
        for (int i = 0; i < queueList.Count; i++)
        {
            Debug.Log($"[TRACKQUEUE]   [{i}] {queueList[i].Item1.SongName}");
        }

        if (isFromSlave)
        {
            songInstance.SongLength = length;
            albumManager.UpdateDebugText($"Slave added song with length: {songInstance.SongLength}.");
        }
        else
        {
            albumManager.UpdateDebugText($"Master loading song to get length: {songInstance.SongName}");

            yield return songInstance.StartCoroutine(songInstance.LoadAudioClipFromPath());

            AudioClip clip = songInstance.GetAudioClip();
            if (clip == null)
            {
                albumManager.UpdateDebugText("Error: AudioClip is NULL. Skipping song.");
                Debug.Log($"[TRACKQUEUE] Removing song due to NULL AudioClip: {songInstance.SongName}");
                queueList.Remove((songInstance, songInstance.gameObject));
                Debug.Log($"[TRACKQUEUE] Queue count after removal: {queueList.Count}");
                yield break;
            }

            songInstance.SongLength = clip.length;
            masterNetworkHandler?.SendSongWithLengthToSlave(songFileName, songInstance.SongLength);
        }

        if (!isPlaying && !albumManager.isSlave)
        {
            Debug.Log($"[TRACKQUEUE] Starting playback - Queue count: {queueList.Count}, isPlaying: {isPlaying}, isSlave: {albumManager.isSlave}");
            PlayQueue();
        }
        else
        {
            Debug.Log($"[TRACKQUEUE] Not starting playback - Queue count: {queueList.Count}, isPlaying: {isPlaying}, isSlave: {albumManager.isSlave}");
        }
    }

    private void UpdateSlaveUI()
    {
        albumManager.UpdateDebugText("Updating Slave UI Clock");
        slaveCurrentTime += Time.deltaTime;

        float totalTime = queueList[currentSongIndex].Item1.SongLength;
        if (slaveCurrentTime >= totalTime)
        {
            slaveCurrentTime = totalTime;
            SkipSongSlave(); // Automatically skip to the next song when it finishes
            return; // Exit the function to prevent further updates on the finished song
        }

        timeText.text = FormatTime(slaveCurrentTime) + "/" + FormatTime(totalTime);
        albumManager.UpdateDebugText("Slave - normal time used");
    }

    private string FormatTime(float timeInSeconds)
    {
        int minutes = Mathf.FloorToInt(timeInSeconds / 60);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60);
        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }
    public async Task AddSongToQueue(string keypadInput, string requestedBy = "user")
    {
        try
        {
            Debug.Log($"[TRACKQUEUE] AddSongToQueue called - Input: {keypadInput}, RequestedBy: {requestedBy}");
            albumManager.UpdateDebugText($"Adding song to queue: {keypadInput}");

            if (keypadInput.Length != 5 || keypadInput[2] != '-')
            {
                Debug.LogWarning($"[TRACKQUEUE] Invalid input format: {keypadInput}. Expected: DD-DD");
                albumManager.UpdateDebugText("Invalid input format. Expected format: DD-DD");
                return;
            }

            if (!int.TryParse(keypadInput.Substring(0, 2), out int albumIndex) ||
                !int.TryParse(keypadInput.Substring(3, 2), out int songIndex))
            {
                albumManager.UpdateDebugText($"Failed to parse album or song index from input: {keypadInput}");
                return;
            }

            if (albumIndex < 0 || albumIndex >= albumManager.albums.Count)
            {
                albumManager.UpdateDebugText($"Album index {albumIndex} is out of range.");
                return;
            }

            Album album = albumManager.albums[albumIndex - 1];

            if (songIndex <= 0 || songIndex > album.Songs.Count)
            {
                albumManager.UpdateDebugText($"Song index {songIndex} is out of range for album: {album.albumName}");
                return;
            }

            Song selectedSong = album.Songs[songIndex - 1];
            Debug.Log($"[TRACKQUEUE] STEP 1: Validating audio file exists for: {selectedSong.SongName}");

            // Find the corresponding MongoDB song
            var mongoSongs = await mongoDBManager.GetAllSongsAsync();
            var mongoSong = mongoSongs.Find(s => s.Title.Contains(selectedSong.SongName) && s.Album == album.albumName);

            if (mongoSong == null)
            {
                albumManager.UpdateDebugText($"Song not found in MongoDB: {selectedSong.SongName}");
                return;
            }

            // STEP 2: Find audio path and validate it exists
            Debug.Log($"[TRACKQUEUE] STEP 2: Finding and validating audio file");
            
            string albumPath = albumManager.FindAlbumFolder(album.albumName);
            string audioPath = albumManager.FindSongFilePath(albumPath, selectedSong.SongName);
            int audioLength = 180; // Default duration
            
            if (!string.IsNullOrEmpty(audioPath))
            {
                audioLength = albumManager.GetAudioFileLength(audioPath);
                if (audioLength <= 0)
                {
                    audioLength = 180; // Fallback to default
                }
            }
            else
            {
                Debug.LogError($"[TRACKQUEUE] Audio file not found for song: {selectedSong.SongName}");
                albumManager.UpdateDebugText($"Audio file not found for song: {selectedSong.SongName}");
                return;
            }
            
            // STEP 3: Add to Unity queue and start playing FIRST
            Debug.Log($"[TRACKQUEUE] STEP 3: Adding to Unity queue and starting playback");
            StartCoroutine(AddSongToQueueWithPathAndMongoDB(selectedSong.SongName, audioPath, audioLength, mongoSong, album.albumName, selectedSong.Artist, requestedBy));
            
            Debug.Log($"[TRACKQUEUE] Song addition initiated for: {selectedSong.SongName} - will add to MongoDB after loading completes");
        }
        catch (Exception ex)
        {
            albumManager.UpdateDebugText($"Error adding song to queue: {ex.Message}");
            Debug.LogError($"Error adding song to queue: {ex.Message}");
        }
    }
    

    // New method: Add song to Unity queue, wait for it to start playing, THEN add to MongoDB tracklist
    public IEnumerator AddSongToQueueWithPathAndMongoDB(string songName, string audioPath, int audioLength, 
        MongoDBModels.SongDocument mongoSong, string albumName, string artist, string requestedBy)
    {
        Debug.Log($"[TRACKQUEUE] AddSongToQueueWithPathAndMongoDB: Starting for {songName}");
        
        // Show loading for song addition process
        ShowLoading(loadingPrefab, $"Adding song: {songName}");
        
        // First, add to Unity queue and start playing
        yield return StartCoroutine(AddSongToQueueWithPath(songName, audioPath, audioLength, false));
        
        // Wait a moment for the song to actually start playing
        yield return new WaitForSeconds(0.5f);
        
        // Now add to MongoDB tracklist (this will trigger slave to start playing)
        Debug.Log($"[TRACKQUEUE] Song loaded and playing, now adding to MongoDB tracklist");
        
        // Use a coroutine wrapper for the async MongoDB operations
        yield return StartCoroutine(AddToMongoDBTracklistCoroutine(mongoSong, songName, artist, albumName, audioLength, requestedBy));
        
        // Hide loading after completion
        HideLoading(loadingPrefab);
        
        Debug.Log($"[TRACKQUEUE] AddSongToQueueWithPathAndMongoDB: Completed for {songName}");
    }
    
    // Coroutine wrapper for MongoDB operations
    private IEnumerator AddToMongoDBTracklistCoroutine(MongoDBModels.SongDocument mongoSong, string songName, string artist, 
        string albumName, int audioLength, string requestedBy)
    {
        bool mongoOperationComplete = false;
        MongoDBModels.TracklistEntryDocument tracklistEntry = null;
        
        // Start the async operation
        AddToMongoDBTracklistAsync(mongoSong, songName, artist, albumName, audioLength, requestedBy, 
            result => { tracklistEntry = result; mongoOperationComplete = true; });
        
        // Wait for completion
        yield return new WaitUntil(() => mongoOperationComplete);
        
        if (tracklistEntry != null)
        {
            albumManager.UpdateDebugText($"Added {songName} to MongoDB tracklist");
            Debug.Log($"[TRACKQUEUE] Updated existsAtMaster=true and length={audioLength} for {songName}");
            
            // Notify WebSocket server about the current playing song
            if (webSocketAPI != null)
            {
                webSocketAPI.SetCurrentPlayingSong(tracklistEntry.Id, songName, artist, albumName);
            }
        }
        else
        {
            Debug.LogError("[TRACKQUEUE] Failed to add song to MongoDB tracklist");
            albumManager.UpdateDebugText("Failed to add song to tracklist");
        }
    }
    
    // Async method for MongoDB operations
    private async void AddToMongoDBTracklistAsync(MongoDBModels.SongDocument mongoSong, string songName, string artist, 
        string albumName, int audioLength, string requestedBy, System.Action<MongoDBModels.TracklistEntryDocument> callback)
    {
        try
        {
            string masterId = albumManager.isSlave ? "slave" : "master";
            var tracklistEntry = await mongoDBManager.AddSongToTracklistAsync(
                mongoSong.Id,
                songName,
                artist,
                albumName,
                audioLength,
                requestedBy,
                masterId,
                1 // Default priority
            );

            if (tracklistEntry != null)
            {
                // Update existsAtMaster and length after successful insertion
                await mongoDBManager.UpdateExistsAtMasterAsync(tracklistEntry.Id, true, audioLength);
                
                // If this is the first song in the queue, set status to "playing"
                if (queueList.Count == 1)
                {
                    await mongoDBManager.UpdateTracklistStatusAsync(tracklistEntry.Id, "playing");
                    Debug.Log($"[TRACKQUEUE] First song in queue - set status to 'playing': {songName}");
                }
                
                callback?.Invoke(tracklistEntry);
            }
            else
            {
                callback?.Invoke(null);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error in AddToMongoDBTracklistAsync: {ex.Message}");
            callback?.Invoke(null);
        }
    }

    // New method: Add song to Unity queue WITHOUT adding to MongoDB tracklist
    // This is used when processing songs from MongoDB tracklist to avoid infinite loops
    public async Task AddSongToUnityQueueFromMongoDB(string keypadInput, string requestedBy = "user")
    {
        try
        {
            Debug.Log($"[TRACKQUEUE] AddSongToUnityQueueFromMongoDB called - Input: {keypadInput}, RequestedBy: {requestedBy}");
            albumManager.UpdateDebugText($"Adding song to Unity queue from MongoDB: {keypadInput}");

            if (keypadInput.Length != 5 || keypadInput[2] != '-')
            {
                Debug.LogWarning($"[TRACKQUEUE] Invalid input format: {keypadInput}. Expected: DD-DD");
                albumManager.UpdateDebugText("Invalid input format. Expected format: DD-DD");
                return;
            }

            if (!int.TryParse(keypadInput.Substring(0, 2), out int albumIndex) ||
                !int.TryParse(keypadInput.Substring(3, 2), out int songIndex))
            {
                Debug.LogError($"[TRACKQUEUE] Failed to parse album/song index from: {keypadInput}");
                albumManager.UpdateDebugText($"Failed to parse album or song index from input: {keypadInput}");
                return;
            }

            Debug.Log($"[TRACKQUEUE] Parsed - Album: {albumIndex}, Song: {songIndex}");

            if (albumIndex < 0 || albumIndex >= albumManager.albums.Count)
            {
                Debug.LogError($"[TRACKQUEUE] Album index {albumIndex} out of range. Total albums: {albumManager.albums.Count}");
                albumManager.UpdateDebugText($"Album index {albumIndex} is out of range.");
                return;
            }

            Album album = albumManager.albums[albumIndex - 1];
            Debug.Log($"[TRACKQUEUE] Selected album: {album.albumName}");

            if (songIndex <= 0 || songIndex > album.Songs.Count)
            {
                Debug.LogError($"[TRACKQUEUE] Song index {songIndex} out of range for album {album.albumName}. Total songs: {album.Songs.Count}");
                albumManager.UpdateDebugText($"Song index {songIndex} is out of range for album: {album.albumName}");
                return;
            }

            Song selectedSong = album.Songs[songIndex - 1];
            Debug.Log($"[TRACKQUEUE] Selected song: {selectedSong.SongName}");
            Debug.Log($"[TRACKQUEUE] Song audio path: {selectedSong.AudioClipPath}");

            // If no audio path, search for it now
            string audioPath = selectedSong.AudioClipPath;
            if (string.IsNullOrEmpty(audioPath))
            {
                Debug.Log($"[TRACKQUEUE] No audio path found, searching for song: {selectedSong.SongName}");
                albumManager.UpdateDebugText($"Searching for audio file: {selectedSong.SongName}");
                
                // Find the album folder and audio file
                string albumPath = albumManager.FindAlbumFolder(album.albumName);
                if (string.IsNullOrEmpty(albumPath))
                {
                    Debug.LogError($"[TRACKQUEUE] Album folder not found for: {album.albumName}");
                    albumManager.UpdateDebugText($"Album folder not found for: {album.albumName}");
                    return;
                }
                
                audioPath = albumManager.FindSongFilePath(albumPath, selectedSong.SongName);
                if (string.IsNullOrEmpty(audioPath))
                {
                    Debug.LogError($"[TRACKQUEUE] Audio file not found for song: {selectedSong.SongName}");
                    albumManager.UpdateDebugText($"Audio file not found for song: {selectedSong.SongName}");
                    return;
                }
                
                Debug.Log($"[TRACKQUEUE] Found audio path: {audioPath}");
            }

            // Add directly to Unity queue using the audio path
            Debug.Log($"[TRACKQUEUE] Adding song to Unity queue: {selectedSong.SongName}");
            Debug.Log($"[TRACKQUEUE] Queue count before adding: {queueList.Count}");
            StartCoroutine(AddSongToQueueWithPath(selectedSong.SongName, audioPath, 180, false));

        Debug.Log($"[TRACKQUEUE] Successfully added {selectedSong.SongName} to Unity queue from MongoDB");
        albumManager.UpdateDebugText($"Added {selectedSong.SongName} to Unity queue from MongoDB");
        }
        catch (Exception ex)
        {
        albumManager.UpdateDebugText($"Error adding song to Unity queue: {ex.Message}");
        Debug.LogError($"Error adding song to Unity queue: {ex.Message}");
    }
}

// New method: Add song to Unity queue using the actual file path
    // Version that doesn't add to MongoDB - used when processing existing tracklist entries
    public IEnumerator AddSongToQueueWithPathOnly(string songName, string audioPath, float length = 0f, bool isFromSlave = false)
    {
        Debug.Log($"[TRACKQUEUE] AddSongToQueueWithPathOnly called - Song: {songName}, Path: {audioPath}, Length: {length}, FromSlave: {isFromSlave}");
        albumManager.UpdateDebugText($"Adding song to queue (no MongoDB): {songName}");

        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            Debug.LogError($"[TRACKQUEUE] Audio file does not exist: {audioPath}");
            albumManager.UpdateDebugText($"Audio file does not exist: {audioPath}");
            yield break;
        }

        if (SongPrefab == null || SongContainer == null)
        {
            Debug.LogError($"[TRACKQUEUE] SongPrefab or SongContainer is null! SongPrefab: {SongPrefab}, SongContainer: {SongContainer}");
            albumManager.UpdateDebugText("SongPrefab or SongContainer is null!");
            yield break;
        }

        Debug.Log($"[TRACKQUEUE] Creating song instance: {songName} with path: {audioPath}");

        Song songInstance = Instantiate(SongPrefab, SongContainer);
        songInstance.Initialize(songName, "Unknown Artist", audioPath, songName);
        queueList.Add((songInstance, songInstance.gameObject));
        
        Debug.Log($"[TRACKQUEUE] Added song to queue list: {songName} (GameObject: {songInstance.gameObject.name})");
        Debug.Log($"[TRACKQUEUE] Total songs in queue: {queueList.Count}");

        if (isFromSlave)
        {
            songInstance.SongLength = length;
            albumManager.UpdateDebugText($"Slave added song with length: {songInstance.SongLength}.");
        }
        else
        {
            albumManager.UpdateDebugText($"Master loading song to get length: {songInstance.SongName}");

            yield return songInstance.StartCoroutine(songInstance.LoadAudioClipFromPath());

            AudioClip clip = songInstance.GetAudioClip();
            if (clip == null)
            {
                albumManager.UpdateDebugText("Error: AudioClip is NULL. Skipping song.");
                Debug.Log($"[TRACKQUEUE] Removing song due to NULL AudioClip: {songInstance.SongName}");
                queueList.Remove((songInstance, songInstance.gameObject));
                Debug.Log($"[TRACKQUEUE] Queue count after removal: {queueList.Count}");
                yield break;
            }

            songInstance.SongLength = clip.length;
            masterNetworkHandler?.SendSongWithLengthToSlave(songName, songInstance.SongLength);
        }

        if (!isPlaying && !albumManager.isSlave)
        {
            Debug.Log($"[TRACKQUEUE] Starting playback from AddSongToQueueWithPathOnly - Queue count: {queueList.Count}");
            PlayQueue();
        }
        else
        {
            Debug.Log($"[TRACKQUEUE] Not starting playback from AddSongToQueueWithPathOnly - Queue count: {queueList.Count}, isPlaying: {isPlaying}, isSlave: {albumManager.isSlave}");
        }
    }

public IEnumerator AddSongToQueueWithPath(string songName, string audioPath, float length = 0f, bool isFromSlave = false)
{
    Debug.Log($"[TRACKQUEUE] AddSongToQueueWithPath called - Song: {songName}, Path: {audioPath}, Length: {length}, FromSlave: {isFromSlave}");
    albumManager.UpdateDebugText($"Adding song to queue: {songName}");

    if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
    {
        Debug.LogError($"[TRACKQUEUE] Audio file does not exist: {audioPath}");
        albumManager.UpdateDebugText($"Audio file does not exist: {audioPath}");
        yield break;
    }

    if (SongPrefab == null || SongContainer == null)
    {
        Debug.LogError($"[TRACKQUEUE] SongPrefab or SongContainer is null! SongPrefab: {SongPrefab}, SongContainer: {SongContainer}");
        albumManager.UpdateDebugText("SongPrefab or SongContainer is null!");
        yield break;
    }

    Debug.Log($"[TRACKQUEUE] Creating song instance: {songName} with path: {audioPath}");

    Song songInstance = Instantiate(SongPrefab, SongContainer);
    songInstance.Initialize(songName, "Unknown Artist", audioPath, songName); // Use songName as identifier
    
    // Load the audio clip to get the actual length
    Debug.Log($"[TRACKQUEUE] Loading audio clip for length calculation: {songName}");
    yield return StartCoroutine(songInstance.LoadAudioClipFromPath());
    
    if (songInstance.AudioClip != null)
    {
        songInstance.SongLength = songInstance.AudioClip.length;
        Debug.Log($"[TRACKQUEUE] Song length set to: {songInstance.SongLength} seconds");
    }
    else
    {
        Debug.LogError($"[TRACKQUEUE] Failed to load audio clip for: {songName}");
        albumManager.UpdateDebugText($"Failed to load audio for: {songName}");
        Destroy(songInstance.gameObject);
        yield break;
    }
    
    queueList.Add((songInstance, songInstance.gameObject));
    
    Debug.Log($"[TRACKQUEUE] Added song to queue list: {songName} (GameObject: {songInstance.gameObject.name})");
    Debug.Log($"[TRACKQUEUE] Total songs in queue: {queueList.Count}");
    
    // Debug: List all songs in queue after adding
    Debug.Log($"[TRACKQUEUE] Current queue contents:");
    for (int i = 0; i < queueList.Count; i++)
    {
        var (song, gameObject) = queueList[i];
        Debug.Log($"[TRACKQUEUE]   {i + 1}. {song?.SongName} (GameObject: {gameObject?.name})");
    }
    
    // Start playback if not already playing
    if (!isPlaying && !albumManager.isSlave)
    {
        Debug.Log($"[TRACKQUEUE] Starting playback from AddSongToQueueWithPath - Queue count: {queueList.Count}");
        PlayQueue();
        }
    }

    private IEnumerator PlaySongQueue()
    {
        while (queueList.Count > 0)
        {
            currentSongIndex = 0;
            Song nextSong = queueList[currentSongIndex].Item1;

            albumManager.UpdateDebugText($"Loading song: {nextSong.SongName}");


            albumManager.UpdateDebugText("Checking AudioClip...");
            AudioClip clip = nextSong.GetAudioClip();
            if (clip == null)
            {
                albumManager.UpdateDebugText("Error: AudioClip is NULL. Skipping song.");
                Debug.Log($"[TRACKQUEUE] Removing song at index {currentSongIndex} due to NULL AudioClip");
                queueList.RemoveAt(currentSongIndex);
                Debug.Log($"[TRACKQUEUE] Queue count after removal: {queueList.Count}");
                continue;
            }

            albumManager.UpdateDebugText("Setting up AudioSource...");
            audioSource.clip = clip;
            if (albumManager.isSlave) audioSource.volume = 0;

            albumManager.UpdateDebugText("Playing song...");
            PlayedSongName.text = nextSong.SongName;

            // Set up time display
            UpdateUI();

            // Notify MongoDB that song is playing and get the tracklist entry
            // Must be done BEFORE starting playback so currentPlayingTrack is set for deletion
            if (mongoDBMasterController != null)
            {
                // Find the corresponding tracklist entry and mark as playing
                _ = NotifyMongoDBSongPlaying(nextSong.SongName);
                
                // Also set currentPlayingTrack so skip and completion work
                _ = SetCurrentPlayingTrackFromMongoDB(nextSong.SongName);
            }

            // Start playback
            Debug.Log($"[TRACKQUEUE] Starting audio playback: {nextSong.SongName}");
            audioSource.loop = false;
            audioSource.Play();

            // Update time display during playback
            while (audioSource.isPlaying || isPaused)
            {
                if (!isPaused)
                {
                    UpdateUI();
                    
                    // Check if song is about to finish (within 0.1 seconds) and delete from MongoDB
                    if (audioSource.time >= audioSource.clip.length - 0.1f && currentPlayingTrack != null)
                    {
                        string songTitle = currentPlayingTrack.Title;
                        string tracklistId = currentPlayingTrack.Id;
                        
                        Debug.Log($"[TRACKQUEUE] Song about to finish, deleting from MongoDB: {songTitle}");
                        
                        // Call FireAndForget to properly handle async without blocking
                        FireAndForget(DeleteSkippedSongFromMongoDB(tracklistId, songTitle));
                        
                        // Also notify WebSocket server for real-time broadcasting
                        if (webSocketAPI != null)
                        {
                            webSocketAPI.SkipCurrentSong();
                        }
                        
                        currentPlayingTrack = null; // Clear to prevent duplicate deletion
                    }
                }
                yield return null;
            }

            // If playback was stopped externally (pause/skip), exit loop early
            if (!isPlaying) yield break;

            // Remove song after it�s done playing
            Debug.Log($"[TRACKQUEUE] Song finished playing, removing: {queueList[currentSongIndex].Item1.SongName}");
            Destroy(queueList[currentSongIndex].Item2);
            queueList.RemoveAt(currentSongIndex);
            Debug.Log($"[TRACKQUEUE] Queue count after song finished: {queueList.Count}");
        }

        StopAllPlayback(); // Stop everything when queue is empty
        
        // Auto-add random song when queue becomes empty (only for master)
        if (!albumManager.isSlave && CanAutoAdd())
        {
            Debug.Log("[TRACKQUEUE] Queue became empty - auto-adding random song");
            AddRandomSongSimple();
        }
    }



    private IEnumerator PlayNextSong(string keypadInput, bool isFromMaster)
    {
        slaveCurrentTime = 0;

        if (queueList.Count == 0)
        {
            albumManager.UpdateDebugText("Queue is empty.");
            yield break;
        }

        if (currentSongIndex >= 0 && currentSongIndex < queueList.Count)
        {
            albumManager.UpdateDebugText("Stopping previous song...");
            queueList[currentSongIndex].Item1.StopPlayback();
        }

        if (currentSongIndex < queueList.Count)
        {
            Song nextSong = queueList[currentSongIndex].Item1;
            albumManager.UpdateDebugText($"Loading song: {nextSong.SongName}");

            yield return nextSong.StartCoroutine(nextSong.LoadAudioClipFromPath());

            albumManager.UpdateDebugText("Checking AudioClip...");
            AudioClip clip = nextSong.GetAudioClip();
            if (clip == null)
            {
                albumManager.UpdateDebugText("Error: AudioClip is NULL. " + nextSong.AudioClipPath);
                yield break;
            }

            albumManager.UpdateDebugText("Setting up AudioSource...");
            audioSource.clip = clip;

            if (albumManager.isSlave)
            {
                audioSource.volume = 0;
            }

            albumManager.UpdateDebugText("Playing song...");
            audioSource.Play();
            audioSource.loop = false;

            PlayedSongName.text = nextSong.SongName;

            StartCoroutine(WaitForSongToEnd());

            if (!albumManager.isSlave && isFromMaster)
            {
                // Send song length to slave when it's a master request
                masterNetworkHandler.SendSongWithLengthToSlave(keypadInput, clip.length);
            }
            else
            {
                // If not from master, just send the length
                if (!albumManager.isSlave && !isFromMaster)
                {
                    masterNetworkHandler.SendSongLengthToSlave(clip.length);
                }
            }

            currentSongIndex++;
        }
        else
        {
            albumManager.UpdateDebugText("No more songs in the queue.");
            PlayedSongName.text = "";
        }
    }




    private IEnumerator WaitForSongToEnd()
    {
        while (audioSource.isPlaying)
        {
            yield return null;
        }

        if (queueList.Count > 0)
        {
            queueList.RemoveAt(0);
        }

        if (queueList.Count > 0)  // Check before calling PlayNextSong
        {
            currentSongIndex = 0;
            StartCoroutine(PlayNextSong("",false));
        }
        else
        {
            albumManager.UpdateDebugText("Queue is empty. No more songs to play.");
            PlayedSongName.text = "";
        }
    }


    public void PlayPreviousSong()
    {
        if (currentSongIndex > 0)
        {
            currentSongIndex -= 2;
            StartCoroutine(PlayNextSong("", false));
        }
    }

 /*   public void PlayNextSongManually()
    {
        StopCoroutine(PlayNextSong("", false));
        StartCoroutine(PlayNextSong("", false));
*//*
    }*/
    public void SetSongLength(int length)
    {
        queueList[currentSongIndex].Item1.SongLength = length;
    }



    public void PlayQueue()
    {
        Debug.Log($"[TRACKQUEUE] PlayQueue called - isPlaying: {isPlaying}, queueCount: {queueList.Count}");
        
        if (isPlaying) 
        {
            Debug.Log("[TRACKQUEUE] Already playing, skipping PlayQueue");
            return; 
        }

        if (queueList.Count == 0)
        {
            Debug.LogWarning("[TRACKQUEUE] Cannot play queue - queue is empty");
            return;
        }

        isPlaying = true;
        Debug.Log("[TRACKQUEUE] Starting PlaySongQueue coroutine");
        playbackCoroutine = StartCoroutine(PlaySongQueue());
    }

    public async void PauseResumeSong()
    {
       if(isPlaying)
        {
            if (audioSource.isPlaying)
            {
                isPaused = true;
                audioSource.Pause();
                albumManager.UpdateDebugText("Playback paused.");
                
                // Update MongoDB status
                if (currentPlayingTrack != null)
                {
                    await mongoDBManager.UpdateTracklistStatusAsync(currentPlayingTrack.Id, "paused");
                }
                
                // Notify WebSocket server for real-time broadcasting
                if (webSocketAPI != null)
                {
                    webSocketAPI.PauseCurrentSong();
                }
            }
            else if (isPaused)
            {
                isPaused = false;
                audioSource.Play();
                albumManager.UpdateDebugText("Playback resumed.");
                
                // Update MongoDB status
                if (currentPlayingTrack != null)
                {
                    await mongoDBManager.UpdateTracklistStatusAsync(currentPlayingTrack.Id, "playing");
                }
                
                // Notify WebSocket server for real-time broadcasting
                if (webSocketAPI != null)
                {
                    webSocketAPI.ResumeCurrentSong();
                }
            }
            masterNetworkHandler.Pause_Resume();
        }
    }


    public void SkipCurrentSong()
    {
        SkipToNextSong();
    }

    public async void SkipToNextSong()
    {
        if (queueList.Count > 0)
        {
            albumManager.UpdateDebugText("Skipping to next song...");

            // Remove from MongoDB tracklist entirely instead of marking as skipped
            if (currentPlayingTrack != null)
            {
                string songTitle = currentPlayingTrack.Title;
                string tracklistId = currentPlayingTrack.Id;
                
                // Call FireAndForget to properly handle async without blocking
                FireAndForget(DeleteSkippedSongFromMongoDB(tracklistId, songTitle));
            }
            
            // Notify WebSocket server for real-time broadcasting
            if (webSocketAPI != null)
            {
                webSocketAPI.SkipCurrentSong();
            }

            audioSource.Stop(); // Stop current song
            isPaused = false;   // Ensure resume doesn�t interfere

            Destroy(queueList[currentSongIndex].Item2);
            queueList.RemoveAt(0); // Remove current song from queue

            if (queueList.Count > 0)
            {
                // Restart playback with next song
                StopCoroutine(playbackCoroutine);
                playbackCoroutine = StartCoroutine(PlaySongQueue());
            }
            else
            {
                StopAllPlayback(); // If no songs left, stop everything
                
                // Auto-add random song when skip results in empty queue (only for master)
                if (!albumManager.isSlave && CanAutoAdd())
                {
                    Debug.Log("[TRACKQUEUE] Skip resulted in empty queue - auto-adding random song");
                    AddRandomSongSimple();
                }
            }
            masterNetworkHandler.PlayNextSong();
        }
    }

    public void SkipSongSlave()
    {
        albumManager.UpdateDebugText("Skipping to next song (Slave)...");

        if (queueList.Count > 1) // Ensure there�s a next song available
        {
            Destroy(queueList[0].Item2);
            queueList.RemoveAt(0); // Remove the current song

            slaveCurrentTime = 0; // Reset timer
            float nextSongLength = queueList[0].Item1.SongLength; // Get next song length
            timeText.text = FormatTime(0) + "/" + FormatTime(nextSongLength);
            albumManager.UpdateDebugText($"Now playing (Slave): {queueList[0].Item1.SongName}");
        }
        else
        {
            Destroy(queueList[0].Item2);
            slaveCurrentTime = 0;
            timeText.text = FormatTime(0);
            albumManager.UpdateDebugText("No more songs in the queue (Slave).");
            StopAllPlayback(); // Stop UI updates since there are no more songs
        }
    }

    private void StopAllPlayback()
    {
        isPlaying = false;
        isPaused = false;
        audioSource.Stop();
        queueList.Clear();
        albumManager.UpdateDebugText("Queue is empty. Stopping playback.");
        PlayedSongName.text = "";
    }
    // Safely run a Task from non-async code (fire-and-forget but observes exceptions)
    private async void FireAndForget(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Background task error: {ex}");
        }
    }

    private async Task DeleteSkippedSongFromMongoDB(string tracklistId, string songTitle)
    {
        try
        {
            bool deleted = await mongoDBManager.DeleteTracklistEntryAsync(tracklistId);
            if (deleted)
            {
                Debug.Log($"[TRACKQUEUE] Successfully removed skipped song from MongoDB tracklist: {songTitle}");
            }
            else
            {
                Debug.LogWarning($"[TRACKQUEUE] Failed to remove skipped song from MongoDB tracklist: {songTitle}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error deleting skipped song from MongoDB: {ex.Message}");
        }
    }

    private async Task DeleteFinishedSongFromMongoDB(string tracklistId, string songTitle)
    {
        try
        {
            bool deleted = await mongoDBManager.DeleteTracklistEntryAsync(tracklistId);
            if (deleted)
            {
                Debug.Log($"[TRACKQUEUE] Successfully removed finished song from MongoDB tracklist: {songTitle}");
            }
            else
            {
                Debug.LogWarning($"[TRACKQUEUE] Failed to remove finished song from MongoDB tracklist: {songTitle}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error deleting finished song from MongoDB: {ex.Message}");
        }
    }

    private async Task NotifyMongoDBSongPlaying(string songName)
    {
        try
        {
            if (mongoDBManager == null) return;

            // Find the tracklist entry for this song
            var queuedSongs = await mongoDBManager.GetQueuedSongsAsync();
            var playingSong = queuedSongs.FirstOrDefault(s => s.Title == songName);
            
            if (playingSong != null)
            {
                await mongoDBManager.UpdateTracklistStatusAsync(playingSong.Id, TracklistStatus.Playing);
                albumManager.UpdateDebugText($"Marked {songName} as playing in MongoDB");
            }
        }
        catch (Exception ex)
        {
            albumManager.UpdateDebugText($"Error notifying MongoDB of song playing: {ex.Message}");
        }
    }

    private async Task SetCurrentPlayingTrackFromMongoDB(string songName)
    {
        try
        {
            if (mongoDBManager == null) return;

            // Get all queued/playing songs and find the matching one
            var allTracks = await mongoDBManager.GetAllTracklistEntriesAsync();
            var matchingTrack = allTracks.FirstOrDefault(s => s.Title == songName);
            
            if (matchingTrack != null)
            {
                currentPlayingTrack = matchingTrack;
                Debug.Log($"[TRACKQUEUE] Set currentPlayingTrack: {matchingTrack.Title} (ID: {matchingTrack.Id})");
            }
            else
            {
                Debug.LogWarning($"[TRACKQUEUE] Could not find tracklist entry for: {songName}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error setting currentPlayingTrack: {ex.Message}");
        }
    }

    private async Task NotifyMongoDBSongFinished(string songName)
    {
        try
        {
            if (mongoDBManager == null) return;

            // Find the tracklist entry for this song
            var playingSongs = await mongoDBManager.GetPlayingSongsAsync();
            var finishedSong = playingSongs.FirstOrDefault(s => s.Title == songName);
            
            if (finishedSong != null)
            {
                // Delete the finished song from tracklist instead of marking as played
                bool deleted = await mongoDBManager.DeleteTracklistEntryAsync(finishedSong.Id);
                if (deleted)
                {
                    albumManager.UpdateDebugText($"Removed finished song from tracklist: {songName}");
                }
                else
                {
                    albumManager.UpdateDebugText($"Failed to remove finished song from tracklist: {songName}");
                }
            }
        }
        catch (Exception ex)
        {
            albumManager.UpdateDebugText($"Error notifying MongoDB of song finished: {ex.Message}");
        }
    }
    
    
    private async Task<List<MongoDBModels.SongDocument>> GetAllSongsFromMongoDB()
    {
        try
        {
            return await mongoDBManager.GetAllSongsAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TRACKQUEUE] Error getting all songs from MongoDB: {ex.Message}");
            return new List<MongoDBModels.SongDocument>();
        }
    }
    
    private void OnDestroy()
    {
        Debug.Log("[TRACKQUEUE] TrackQueueManager OnDestroy - Cleaning up...");
        
        // Unsubscribe from MongoDB change stream events
        MongoDBManager.OnTracklistChanged -= OnTracklistChangeDetected;
        
        // Unsubscribe from WebSocket events
        WebSocketTracklistAPI.OnTracklistUpdate -= OnWebSocketTracklistUpdate;
        
        if (playbackCoroutine != null)
        {
            StopCoroutine(playbackCoroutine);
        }
        
        // Stop all coroutines including monitoring
        StopAllCoroutines();
        
        // Clean up all loading instances
        foreach (var kvp in activeLoadingInstances)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value);
            }
        }
        activeLoadingInstances.Clear();
        currentLoadingInstance = null;
        
        // Clear all song GameObjects from the queue
        ClearSongQueue();
        
        Debug.Log("[TRACKQUEUE] TrackQueueManager cleanup completed");
    }
    
    private void OnApplicationPause(bool pauseStatus)
    {
        // Do not pause playback on application pause; ensure audio continues
        if (pauseStatus)
        {
            Debug.Log("[TRACKQUEUE] Application paused - continuing playback (no auto-pause)");
        }
        AudioListener.pause = false;
    }
    
    private void OnApplicationFocus(bool hasFocus)
    {
        // Do not pause playback when focus changes; keep audio running
        if (!hasFocus)
        {
            Debug.Log("[TRACKQUEUE] Application lost focus - continuing playback (no auto-pause)");
        }
        AudioListener.pause = false;
    }
    
    private void ClearSongQueue()
    {
        Debug.Log($"[TRACKQUEUE] ClearSongQueue called - Current queue count: {queueList.Count}");
        
        if (queueList.Count == 0)
        {
            Debug.LogWarning("[TRACKQUEUE] Queue is already empty - nothing to clear");
            return;
        }
        
        // Log details about each song being cleared
        for (int i = 0; i < queueList.Count; i++)
        {
            var (song, gameObject) = queueList[i];
            Debug.Log($"[TRACKQUEUE] Clearing song {i + 1}: {song?.SongName} (GameObject: {gameObject?.name})");
        }
        
        // Destroy all song GameObjects
        foreach (var (song, gameObject) in queueList)
        {
            if (gameObject != null)
            {
                Debug.Log($"[TRACKQUEUE] Destroying GameObject: {gameObject.name}");
                Destroy(gameObject);
            }
            else
            {
                Debug.LogWarning($"[TRACKQUEUE] GameObject is null for song: {song?.SongName}");
            }
        }
        
        // Clear the queue list
        queueList.Clear();
        
        Debug.Log("[TRACKQUEUE] Song queue cleared successfully");
    }

}