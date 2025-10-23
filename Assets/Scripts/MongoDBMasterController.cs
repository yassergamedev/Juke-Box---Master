using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Threading.Tasks;
using MongoDBModels;
using System;
using System.Linq;

public class MongoDBMasterController : MonoBehaviour
{
    [Header("MongoDB Settings")]
    public float pollInterval = 1f; // Poll MongoDB every 1 second
    
    [Header("UI Elements")]
    public Text debugText;
    public Text statusText;
    public Text currentSongText;
    public Text queueText;
    public Button refreshQueueButton;
    public Button clearQueueButton;
    public ScrollRect queueScrollRect;
    public GameObject queueItemPrefab;

    private MongoDBManager mongoDBManager;
    private AlbumManager albumManager;
    private TrackQueueManager trackQueueManager;
    private Coroutine pollingCoroutine;
    private string masterId = "master";
    private bool isConnected = false;
    private List<TracklistEntryDocument> currentQueue = new List<TracklistEntryDocument>();

    private void Start()
    {
        Debug.Log("[MONGODB_MASTER] Starting MongoDB Master Controller...");
        
        mongoDBManager = MongoDBManager.Instance;
        albumManager = FindObjectOfType<AlbumManager>();
        trackQueueManager = FindObjectOfType<TrackQueueManager>();

        Debug.Log($"[MONGODB_MASTER] MongoDBManager found: {mongoDBManager != null}");
        Debug.Log($"[MONGODB_MASTER] AlbumManager found: {albumManager != null}");
        Debug.Log($"[MONGODB_MASTER] TrackQueueManager found: {trackQueueManager != null}");

        if (mongoDBManager == null)
        {
            Debug.LogError("[MONGODB_MASTER] MongoDBManager not found! Make sure it's in the scene.");
            UpdateDebugText("MongoDBManager not found! Make sure it's in the scene.");
            return;
        }

        if (albumManager == null)
        {
            Debug.LogError("[MONGODB_MASTER] AlbumManager not found!");
            UpdateDebugText("AlbumManager not found!");
            return;
        }

        if (trackQueueManager == null)
        {
            Debug.LogError("[MONGODB_MASTER] TrackQueueManager not found!");
            UpdateDebugText("TrackQueueManager not found!");
            return;
        }

        // Setup UI
        Debug.Log("[MONGODB_MASTER] Setting up UI...");
        SetupUI();
        
        // Start polling for new songs
        Debug.Log("[MONGODB_MASTER] Starting polling...");
        Debug.Log($"[MONGODB_MASTER] AlbumBasePath: {albumManager.AlbumBasePath}");
        Debug.Log($"[MONGODB_MASTER] Total albums in manager: {albumManager.albums.Count}");
        StartPolling();
        
        UpdateDebugText("Master initialized. Connected to MongoDB.");
        isConnected = true;
        Debug.Log("[MONGODB_MASTER] Master initialized successfully and connected to MongoDB");
    }

    private void SetupUI()
    {
        if (refreshQueueButton != null)
            refreshQueueButton.onClick.AddListener(() => _ = RefreshQueue());
        
        if (clearQueueButton != null)
            clearQueueButton.onClick.AddListener(() => _ = ClearQueue());
    }

    private void StartPolling()
    {
        Debug.Log("[MONGODB_MASTER] Starting polling coroutine...");
        if (pollingCoroutine != null)
        {
            Debug.Log("[MONGODB_MASTER] Stopping existing polling coroutine...");
            StopCoroutine(pollingCoroutine);
        }
        pollingCoroutine = StartCoroutine(PollForNewSongs());
        Debug.Log("[MONGODB_MASTER] Polling coroutine started successfully");
    }

    private IEnumerator PollForNewSongs()
    {
        Debug.Log("[MONGODB_MASTER] PollForNewSongs coroutine started");
        while (isConnected)
        {
            Debug.Log($"[MONGODB_MASTER] Waiting {pollInterval} seconds before next poll...");
            yield return new WaitForSeconds(pollInterval);
            Debug.Log("[MONGODB_MASTER] Polling interval reached, processing new songs...");
            _ = ProcessNewSongs();
        }
        Debug.Log("[MONGODB_MASTER] PollForNewSongs coroutine ended (isConnected = false)");
    }

    private async Task ProcessNewSongs()
    {
        try
        {
            Debug.Log("[MONGODB_MASTER] Processing new songs...");
            
            // Get all queued songs that haven't been validated yet
            var queuedSongs = await mongoDBManager.GetQueuedSongsAsync();
            var unvalidatedSongs = queuedSongs.Where(song => !song.ExistsAtMaster).ToList();
            
            Debug.Log($"[MONGODB_MASTER] Found {queuedSongs.Count} queued songs, {unvalidatedSongs.Count} need validation");
            
            // Process unvalidated songs (validate and update existsAtMaster)
            foreach (var song in unvalidatedSongs)
            {
                Debug.Log($"[MONGODB_MASTER] Validating song: {song.Title} (ID: {song.Id})");
                await ValidateAndUpdateSong(song);
            }
            
            // Get all validated songs that should be added to Unity queue
            var validatedSongs = queuedSongs.Where(song => 
                song.ExistsAtMaster && 
                !currentQueue.Any(existing => existing.Id == song.Id) &&
                !IsSongAlreadyInUnityQueue(song)).ToList();

            Debug.Log($"[MONGODB_MASTER] Found {validatedSongs.Count} validated songs to add to Unity queue");
            
            // Process validated songs (add to Unity queue)
            foreach (var song in validatedSongs)
            {
                Debug.Log($"[MONGODB_MASTER] Adding validated song to Unity queue: {song.Title} (ID: {song.Id})");
                await ProcessNewSong(song);
            }

            // Update current playing song
            await UpdateCurrentPlayingSong();
            
            // Update queue display
            await RefreshQueue();
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error processing new songs: {ex.Message}");
        }
    }

    private async Task ValidateAndUpdateSong(TracklistEntryDocument song)
    {
        try
        {
            Debug.Log($"[MONGODB_MASTER] Validating song: {song.Title} - Album: {song.Album}");
            
            // Find album folder
            string albumPath = albumManager.FindAlbumFolder(song.Album);
            if (string.IsNullOrEmpty(albumPath))
            {
                Debug.LogWarning($"[MONGODB_MASTER] Album folder not found for: {song.Album}");
                await mongoDBManager.UpdateExistsAtMasterAsync(song.Id, false);
                return;
            }
            
            // Find audio file
            string audioPath = albumManager.FindSongFilePath(albumPath, song.Title);
            if (string.IsNullOrEmpty(audioPath))
            {
                Debug.LogWarning($"[MONGODB_MASTER] Audio file not found for: {song.Title} in {albumPath}");
                await mongoDBManager.UpdateExistsAtMasterAsync(song.Id, false);
                return;
            }
            
            // File exists, get the audio file length and update both existsAtMaster and length
            Debug.Log($"[MONGODB_MASTER] Found valid audio file for {song.Title}: {audioPath}");
            int audioLength = albumManager.GetAudioFileLength(audioPath);
            if (audioLength > 0)
            {
                Debug.Log($"[MONGODB_MASTER] Audio file length: {audioLength} seconds for {song.Title}");
                await mongoDBManager.UpdateExistsAtMasterAsync(song.Id, true, audioLength);
            }
            else
            {
                Debug.LogWarning($"[MONGODB_MASTER] Could not get audio length for {song.Title}, setting existsAtMaster to false");
                await mongoDBManager.UpdateExistsAtMasterAsync(song.Id, false);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MONGODB_MASTER] Error validating song {song.Title}: {ex.Message}");
            await mongoDBManager.UpdateExistsAtMasterAsync(song.Id, false);
        }
    }

    private async Task ProcessNewSong(TracklistEntryDocument song)
    {
        try
        {
            Debug.Log($"[MONGODB_MASTER] Processing song: {song.Title} by {song.Artist}");
            
            // First verify the song exists in master's album collection
            if (!await VerifySongExistsInMaster(song))
            {
                Debug.LogWarning($"[MONGODB_MASTER] Song not found in master albums: {song.Title}");
                UpdateDebugText($"Song not found in master albums: {song.Title}");
                // Mark as skipped in MongoDB since master doesn't have it
                await mongoDBManager.UpdateTracklistStatusAsync(song.Id, TracklistStatus.Skipped);
                return;
            }

            Debug.Log($"[MONGODB_MASTER] Song verified in master albums: {song.Title}");

            // Add to local queue
            Debug.Log($"[MONGODB_MASTER] Processing song: {song.Title} (Length: {song.Title.Length})");
            
            if (song.Title.Length == 5 && song.Title[2] == '-')
            {
                // It's a keypad input (DD-DD format)
                Debug.Log($"[MONGODB_MASTER] Adding keypad input to Unity queue: {song.Title}");
                _ = trackQueueManager.AddSongToUnityQueueFromMongoDB(song.Title, "master");
            }
            else
            {
                // It's a song name
                Debug.Log($"[MONGODB_MASTER] Adding song name to Unity queue: {song.Title}");
                StartCoroutine(trackQueueManager.AddSongToQueueByName(song.Title, song.Duration, false));
            }

            // Add to our tracking list
            currentQueue.Add(song);
            Debug.Log($"[MONGODB_MASTER] Added to tracking queue. Total tracked: {currentQueue.Count}");
            
            UpdateDebugText($"New song added to queue: {song.Title} by {song.Artist}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MONGODB_MASTER] Error processing song {song.Title}: {ex.Message}");
            UpdateDebugText($"Error processing new song: {ex.Message}");
        }
    }

    private async Task UpdateCurrentPlayingSong()
    {
        try
        {
            var playingSongs = await mongoDBManager.GetPlayingSongsAsync();
            
            if (playingSongs.Count > 0)
            {
                var currentSong = playingSongs.First();
                if (currentSongText != null)
                {
                    currentSongText.text = $"Now Playing: {currentSong.Title} - {currentSong.Artist}";
                }
            }
            else
            {
                if (currentSongText != null)
                {
                    currentSongText.text = "No song currently playing";
                }
            }
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error updating current song: {ex.Message}");
        }
    }

    public async Task RefreshQueue()
    {
        try
        {
            var queuedSongs = await mongoDBManager.GetQueuedSongsAsync();
            currentQueue = queuedSongs.ToList();
            
            UpdateQueueDisplay(queuedSongs);
            UpdateDebugText($"Queue refreshed. {queuedSongs.Count} songs in queue.");
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error refreshing queue: {ex.Message}");
        }
    }

    private void UpdateQueueDisplay(List<TracklistEntryDocument> songs)
    {
        if (queueScrollRect == null || queueItemPrefab == null) return;

        // Clear existing items
        foreach (Transform child in queueScrollRect.content)
        {
            Destroy(child.gameObject);
        }

        // Display songs
        foreach (var song in songs)
        {
            var item = Instantiate(queueItemPrefab, queueScrollRect.content);
            var text = item.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.text = $"{song.Title} - {song.Artist} ({song.Status})";
            }
        }
    }

    public async Task ClearQueue()
    {
        try
        {
            await mongoDBManager.ClearTracklistAsync();
            currentQueue.Clear();
            
            // Clear local queue
            trackQueueManager.queueList.Clear();
            
            UpdateDebugText("Queue cleared");
            await RefreshQueue();
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error clearing queue: {ex.Message}");
        }
    }

    public async Task MarkSongAsPlaying(string tracklistId)
    {
        try
        {
            await mongoDBManager.UpdateTracklistStatusAsync(tracklistId, TracklistStatus.Playing, masterId);
            UpdateDebugText("Song marked as playing in MongoDB");
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error marking song as playing: {ex.Message}");
        }
    }

    public async Task MarkSongAsPlayed(string tracklistId)
    {
        try
        {
            await mongoDBManager.MarkSongAsPlayedAsync(tracklistId);
            UpdateDebugText("Song marked as played in MongoDB");
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error marking song as played: {ex.Message}");
        }
    }

    public async Task SkipCurrentSong()
    {
        try
        {
            await mongoDBManager.SkipCurrentSongAsync();
            UpdateDebugText("Current song skipped in MongoDB");
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error skipping current song: {ex.Message}");
        }
    }

    private void UpdateDebugText(string message)
    {
        if (debugText != null)
        {
            debugText.text = message;
        }
        Debug.Log($"MongoDB Master: {message}");
    }

    private async Task<bool> VerifySongExistsInMaster(TracklistEntryDocument song)
    {
        try
        {
            // Check if it's a keypad input (DD-DD format)
            if (song.Title.Contains("-") && song.Title.Length == 5)
            {
                // Parse keypad input
                if (int.TryParse(song.Title.Substring(0, 2), out int albumIndex) &&
                    int.TryParse(song.Title.Substring(3, 2), out int songIndex))
                {
                    // Check if album exists in master's collection
                    if (albumIndex > 0 && albumIndex <= albumManager.albums.Count)
                    {
                        var album = albumManager.albums[albumIndex - 1];
                        // Check if song exists in that album
                        if (songIndex > 0 && songIndex <= album.Songs.Count)
                        {
                            return true; // Song exists in master
                        }
                    }
                }
            }
            else
            {
                // Check if song exists by name in any album
                foreach (var album in albumManager.albums)
                {
                    var foundSong = album.Songs.FirstOrDefault(s => 
                        s.SongName.Equals(song.Title, StringComparison.OrdinalIgnoreCase));
                    if (foundSong != null)
                    {
                        return true; // Song exists in master
                    }
                }
            }
            
            return false; // Song not found in master
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error verifying song existence: {ex.Message}");
            return false;
        }
    }

    private bool IsSongAlreadyInUnityQueue(TracklistEntryDocument song)
    {
        try
        {
            Debug.Log($"[MONGODB_MASTER] Checking Unity queue for duplicate: {song.Title}");
            Debug.Log($"[MONGODB_MASTER] Unity queue has {trackQueueManager.queueList.Count} songs");
            
            // List all songs in Unity queue for debugging
            for (int i = 0; i < trackQueueManager.queueList.Count; i++)
            {
                var queueItem = trackQueueManager.queueList[i];
                Debug.Log($"[MONGODB_MASTER] Unity queue[{i}]: {queueItem.Item1.SongName}");
            }
            
            // Check if song is already in Unity's tracklist
            bool isDuplicate = trackQueueManager.queueList.Any(queueItem => 
                queueItem.Item1.SongName.Equals(song.Title, StringComparison.OrdinalIgnoreCase));
            
            Debug.Log($"[MONGODB_MASTER] Duplicate check result for '{song.Title}': {isDuplicate}");
            return isDuplicate;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MONGODB_MASTER] Error checking Unity queue: {ex.Message}");
            UpdateDebugText($"Error checking Unity queue: {ex.Message}");
            return false;
        }
    }

    private void OnDestroy()
    {
        Debug.Log("[MONGODB_MASTER] OnDestroy - Stopping polling and cleaning up...");
        
        if (pollingCoroutine != null)
        {
            StopCoroutine(pollingCoroutine);
        }
        isConnected = false;
        
        // Clear the tracking queue
        currentQueue.Clear();
        
        Debug.Log("[MONGODB_MASTER] Cleanup completed");
    }
    
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            Debug.Log("[MONGODB_MASTER] Application paused - Stopping polling");
            isConnected = false;
            if (pollingCoroutine != null)
            {
                StopCoroutine(pollingCoroutine);
            }
        }
    }
    
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            Debug.Log("[MONGODB_MASTER] Application lost focus - Stopping polling");
            isConnected = false;
            if (pollingCoroutine != null)
            {
                StopCoroutine(pollingCoroutine);
            }
        }
    }
}
