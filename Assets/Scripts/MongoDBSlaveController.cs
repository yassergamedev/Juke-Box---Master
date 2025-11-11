using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Threading.Tasks;
using MongoDBModels;
using System;
using System.Linq;

public class MongoDBSlaveController : MonoBehaviour
{
    [Header("MongoDB Settings")]
    public float pollInterval = 2f; // Poll MongoDB every 2 seconds
    
    [Header("UI Elements")]
    public Text debugText;
    public TMP_InputField songInputField;
    public Button addSongButton;
    public Button pauseResumeButton;
    public Button nextSongButton;
    public Button previousSongButton;
    public Text statusText;
    public Text currentSongText;

    private MongoDBManager mongoDBManager;
    private AlbumManager albumManager;
    private TrackQueueManager trackQueueManager;
    private Coroutine pollingCoroutine;
    private string slaveId;
    private bool isConnected = false;

    private void Start()
    {
        // Generate unique slave ID
        slaveId = $"slave_{System.Guid.NewGuid().ToString("N")[..8]}";
        Debug.Log($"[MONGODB_SLAVE_{slaveId}] Starting MongoDB Slave Controller...");
        
        mongoDBManager = MongoDBManager.Instance;
        albumManager = FindObjectOfType<AlbumManager>();
        trackQueueManager = FindObjectOfType<TrackQueueManager>();

        Debug.Log($"[MONGODB_SLAVE_{slaveId}] MongoDBManager found: {mongoDBManager != null}");
        Debug.Log($"[MONGODB_SLAVE_{slaveId}] AlbumManager found: {albumManager != null}");
        Debug.Log($"[MONGODB_SLAVE_{slaveId}] TrackQueueManager found: {trackQueueManager != null}");

        if (mongoDBManager == null)
        {
            Debug.LogError($"[MONGODB_SLAVE_{slaveId}] MongoDBManager not found! Make sure it's in the scene.");
            UpdateDebugText("MongoDBManager not found! Make sure it's in the scene.");
            return;
        }

        if (albumManager == null)
        {
            Debug.LogError($"[MONGODB_SLAVE_{slaveId}] AlbumManager not found!");
            UpdateDebugText("AlbumManager not found!");
            return;
        }

        if (trackQueueManager == null)
        {
            Debug.LogError($"[MONGODB_SLAVE_{slaveId}] TrackQueueManager not found!");
            UpdateDebugText("TrackQueueManager not found!");
            return;
        }

        // Setup UI
        Debug.Log($"[MONGODB_SLAVE_{slaveId}] Setting up UI...");
        SetupUI();
        
        // Start polling for commands
        Debug.Log($"[MONGODB_SLAVE_{slaveId}] Starting polling...");
        StartPolling();
        
        UpdateDebugText($"Slave {slaveId} initialized. Connected to MongoDB.");
        isConnected = true;
        Debug.Log($"[MONGODB_SLAVE_{slaveId}] Slave initialized successfully and connected to MongoDB");
    }

    private void SetupUI()
    {
        if (addSongButton != null)
            addSongButton.onClick.AddListener(() => _ = AddSongToQueue());
        
        if (pauseResumeButton != null)
            pauseResumeButton.onClick.AddListener(() => _ = PauseResumeSong());
        
        if (nextSongButton != null)
            nextSongButton.onClick.AddListener(() => _ = PlayNextSong());
        
        if (previousSongButton != null)
            previousSongButton.onClick.AddListener(() => _ = PlayPreviousSong());
    }

    private void StartPolling()
    {
        Debug.Log($"[MONGODB_SLAVE_{slaveId}] Starting polling coroutine...");
        if (pollingCoroutine != null)
        {
            Debug.Log($"[MONGODB_SLAVE_{slaveId}] Stopping existing polling coroutine...");
            StopCoroutine(pollingCoroutine);
        }
        pollingCoroutine = StartCoroutine(PollForCommands());
        Debug.Log($"[MONGODB_SLAVE_{slaveId}] Polling coroutine started successfully");
    }

    private IEnumerator PollForCommands()
    {
        Debug.Log($"[MONGODB_SLAVE_{slaveId}] PollForCommands coroutine started");
        while (isConnected)
        {
            Debug.Log($"[MONGODB_SLAVE_{slaveId}] Waiting {pollInterval} seconds before next poll...");
            yield return new WaitForSeconds(pollInterval);
            Debug.Log($"[MONGODB_SLAVE_{slaveId}] Polling interval reached, checking for commands...");
            _ = CheckForCommands();
        }
        Debug.Log($"[MONGODB_SLAVE_{slaveId}] PollForCommands coroutine ended (isConnected = false)");
    }

    private async Task CheckForCommands()
    {
        try
        {
            Debug.Log($"[MONGODB_SLAVE_{slaveId}] Checking for commands...");
            
            // Check for songs assigned to this slave
            var assignedSongs = await mongoDBManager.GetTracklistByStatusAsync(TracklistStatus.Queued);
            Debug.Log($"[MONGODB_SLAVE_{slaveId}] Found {assignedSongs.Count} queued songs in MongoDB");
            
            // Filter out songs that are already in Unity's queue and only process validated songs
            var newSongs = assignedSongs.Where(song => 
                (string.IsNullOrEmpty(song.SlaveId) || song.SlaveId == slaveId) &&
                song.ExistsAtMaster && // Only process songs that exist at master
                !IsSongAlreadyInUnityQueue(song)).ToList();
            
            Debug.Log($"[MONGODB_SLAVE_{slaveId}] After filtering: {newSongs.Count} new songs to process");
            Debug.Log($"[MONGODB_SLAVE_{slaveId}] Unity queue size: {trackQueueManager.queueList.Count}");
            
            foreach (var song in newSongs)
            {
                Debug.Log($"[MONGODB_SLAVE_{slaveId}] Processing assigned song: {song.Title} (ID: {song.Id})");
                // This song is for us, add it to our local queue
                await ProcessAssignedSong(song);
            }

            // Check for control commands (pause, next, previous)
            await CheckControlCommands();
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error polling MongoDB: {ex.Message}");
        }
    }

    private async Task ProcessAssignedSong(TracklistEntryDocument song)
    {
        try
        {
            Debug.Log($"[MONGODB_SLAVE_{slaveId}] Processing assigned song: {song.Title} by {song.Artist}");
            
            // Mark song as assigned to this slave
            await mongoDBManager.UpdateTracklistStatusAsync(song.Id, TracklistStatus.Queued, slaveId);
            Debug.Log($"[MONGODB_SLAVE_{slaveId}] Updated song status in MongoDB: {song.Title}");
            
            // Add to local queue
            if (song.Title.Contains("-"))
            {
                // It's a keypad input (DD-DD format)
                Debug.Log($"[MONGODB_SLAVE_{slaveId}] Adding keypad input to Unity queue: {song.Title}");
                _ = trackQueueManager.AddSongToUnityQueueFromMongoDB(song.Title, "slave");
            }
            else
            {
                // It's a song name
                Debug.Log($"[MONGODB_SLAVE_{slaveId}] Adding song name to Unity queue: {song.Title}");
                StartCoroutine(trackQueueManager.AddSongToQueueByName(song.Title, song.Duration, true));
            }

            Debug.Log($"[MONGODB_SLAVE_{slaveId}] Successfully added song to queue: {song.Title}");
            UpdateDebugText($"Added song to queue: {song.Title}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MONGODB_SLAVE_{slaveId}] Error processing assigned song {song.Title}: {ex.Message}");
            UpdateDebugText($"Error processing assigned song: {ex.Message}");
        }
    }

    private async Task CheckControlCommands()
    {
        // This could be expanded to check for specific control commands
        // For now, we'll rely on the local UI controls
    }

    public async Task AddSongToQueue()
    {
        if (string.IsNullOrEmpty(songInputField.text))
        {
            UpdateDebugText("Please enter a song or keypad input (DD-DD)");
            return;
        }

        try
        {
            string input = songInputField.text.Trim();
            string songId = System.Guid.NewGuid().ToString();
            string title = input;
            string artist = "Unknown";
            string album = "Unknown";
            int duration = 180; // Default duration

            // Try to get song info if it's a keypad input
            if (input.Length == 5 && input[2] == '-' && 
                int.TryParse(input.Substring(0, 2), out int albumIndex) &&
                int.TryParse(input.Substring(3, 2), out int songIndex))
            {
                // It's a keypad input, try to get real song info
                if (albumManager.albums.Count > albumIndex - 1)
                {
                    var albumObj = albumManager.albums[albumIndex - 1];
                    if (albumObj.Songs.Count > songIndex - 1)
                    {
                        var song = albumObj.Songs[songIndex - 1];
                        title = song.SongName;
                        artist = song.Artist;
                        album = albumObj.albumName;
                        duration = (int)song.SongLength;
                    }
                }
            }

            // Add to MongoDB tracklist
            var tracklistEntry = await mongoDBManager.AddSongToTracklistAsync(
                songId, title, artist, album, duration, slaveId, "master", 1);

            if (tracklistEntry != null)
            {
                UpdateDebugText($"Song added to MongoDB tracklist: {title}");
                songInputField.text = ""; // Clear input
            }
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error adding song to queue: {ex.Message}");
        }
    }

    public async Task PauseResumeSong()
    {
        try
        {
            // Get current playing songs for this slave
            var playingSongs = await mongoDBManager.GetPlayingSongsAsync();
            
            // Check if we have a playing song for this slave
            var currentSong = playingSongs.FirstOrDefault(s => s.SlaveId == slaveId);
            
            if (currentSong != null)
            {
                // If we have a playing song, pause it
                await mongoDBManager.UpdateTracklistStatusAsync(currentSong.Id, "paused", slaveId);
                Debug.Log($"[MONGODB_SLAVE] Paused song: {currentSong.Title}");
            }
            else
            {
                // If no playing song, check for paused songs and resume the first one
                var allSongs = await mongoDBManager.GetAllTracklistEntriesAsync();
                var pausedSong = allSongs.FirstOrDefault(s => s.SlaveId == slaveId && s.Status == "paused");
                
                if (pausedSong != null)
                {
                    await mongoDBManager.UpdateTracklistStatusAsync(pausedSong.Id, "playing", slaveId);
                    Debug.Log($"[MONGODB_SLAVE] Resumed song: {pausedSong.Title}");
                }
            }

            // Also call local pause/resume
            trackQueueManager.PauseResumeSong();
            UpdateDebugText("Pause/Resume command sent");
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error with pause/resume: {ex.Message}");
        }
    }

    public async Task PlayNextSong()
    {
        try
        {
            // Remove current song from MongoDB tracklist entirely instead of marking as skipped
            var playingSongs = await mongoDBManager.GetPlayingSongsAsync();
            foreach (var song in playingSongs)
            {
                if (song.SlaveId == slaveId)
                {
                    bool deleted = await mongoDBManager.DeleteTracklistEntryAsync(song.Id);
                    if (deleted)
                    {
                        Debug.Log($"Successfully removed skipped song from tracklist: {song.Title}");
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to remove skipped song from tracklist: {song.Title}");
                    }
                }
            }

            // Call local next song
            trackQueueManager.SkipToNextSong();
            UpdateDebugText("Next song command sent");
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error with next song: {ex.Message}");
        }
    }

    public async Task PlayPreviousSong()
    {
        try
        {
            // Call local previous song
            trackQueueManager.PlayPreviousSong();
            UpdateDebugText("Previous song command sent");
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error with previous song: {ex.Message}");
        }
    }

    public async Task MarkSongAsPlaying(string tracklistId)
    {
        try
        {
            await mongoDBManager.UpdateTracklistStatusAsync(tracklistId, TracklistStatus.Playing, slaveId);
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
            // Delete the finished song from tracklist instead of marking as played
            bool deleted = await mongoDBManager.DeleteTracklistEntryAsync(tracklistId);
            if (deleted)
            {
                UpdateDebugText("Finished song removed from tracklist");
            }
            else
            {
                UpdateDebugText("Failed to remove finished song from tracklist");
            }
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error removing finished song: {ex.Message}");
        }
    }

    private void UpdateDebugText(string message)
    {
        if (debugText != null)
        {
            debugText.text = message;
        }
        Debug.Log($"MongoDB Slave {slaveId}: {message}");
    }

    private bool IsSongAlreadyInUnityQueue(TracklistEntryDocument song)
    {
        try
        {
            // Check if song is already in Unity's tracklist
            bool isDuplicate = trackQueueManager.queueList.Any(queueItem => 
                queueItem.Item1.SongName.Equals(song.Title, StringComparison.OrdinalIgnoreCase));
            
            Debug.Log($"[MONGODB_SLAVE_{slaveId}] Checking Unity queue for duplicate: {song.Title} - Found: {isDuplicate}");
            return isDuplicate;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MONGODB_SLAVE_{slaveId}] Error checking Unity queue: {ex.Message}");
            UpdateDebugText($"Error checking Unity queue: {ex.Message}");
            return false;
        }
    }

    private void OnDestroy()
    {
        Debug.Log($"[MONGODB_SLAVE_{slaveId}] OnDestroy - Stopping polling and cleaning up...");
        
        if (pollingCoroutine != null)
        {
            StopCoroutine(pollingCoroutine);
        }
        isConnected = false;
        
        Debug.Log($"[MONGODB_SLAVE_{slaveId}] Cleanup completed");
    }
    
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            Debug.Log($"[MONGODB_SLAVE_{slaveId}] Application paused - Stopping polling");
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
            Debug.Log($"[MONGODB_SLAVE_{slaveId}] Application lost focus - Stopping polling");
            isConnected = false;
            if (pollingCoroutine != null)
            {
                StopCoroutine(pollingCoroutine);
            }
        }
    }
}
