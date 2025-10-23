using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using MongoDBModels;
using System.Linq;

public class MongoDBIntegration : MonoBehaviour
{
    private MongoDBManager mongoDBManager;
    private AlbumManager albumManager;
    private TrackQueueManager trackQueueManager;

    [Header("Sync Settings")]
    public bool autoSyncOnStart = true;
    public bool syncOnAlbumChange = true;
    public bool syncOnSongPlay = true;

    private void Start()
    {
        mongoDBManager = MongoDBManager.Instance;
        albumManager = AlbumManager.Instance;
        trackQueueManager = FindObjectOfType<TrackQueueManager>();

        if (autoSyncOnStart)
        {
            _ = SyncAlbumsToMongoDB();
        }
    }

    /// <summary>
    /// Syncs all albums from local file system to MongoDB
    /// </summary>
    public async Task SyncAlbumsToMongoDB()
    {
        if (mongoDBManager == null || albumManager == null)
        {
            Debug.LogError("MongoDBManager or AlbumManager not found!");
            return;
        }

        try
        {
            Debug.Log("Starting album sync to MongoDB...");
            
            // Get all albums from AlbumManager
            var albums = albumManager.albums;
            
            foreach (var album in albums)
            {
                // Create or update album document
                await SyncAlbumToMongoDB(album);
                
                // Sync all songs in the album
                await SyncAlbumSongsToMongoDB(album);
            }
            
            Debug.Log("Album sync completed successfully!");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error syncing albums to MongoDB: {ex.Message}");
        }
    }

    /// <summary>
    /// Syncs a single album to MongoDB
    /// </summary>
    private async Task SyncAlbumToMongoDB(Album album)
    {
        try
        {
            var albumDoc = new AlbumDocument
            {
                Title = album.albumName
            };

            // Check if album already exists
            var existingAlbums = await mongoDBManager.GetAllAlbumsAsync();
            var existingAlbum = existingAlbums.FirstOrDefault(a => a.Title == album.albumName);

            if (existingAlbum == null)
            {
                // Insert new album
                await mongoDBManager.AlbumsCollection.InsertOneAsync(albumDoc);
                Debug.Log($"Added new album to MongoDB: {album.albumName}");
            }
            else
            {
                Debug.Log($"Album already exists in MongoDB: {album.albumName}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error syncing album {album.albumName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Syncs all songs from an album to MongoDB
    /// </summary>
    private async Task SyncAlbumSongsToMongoDB(Album album)
    {
        try
        {
            foreach (var song in album.GetSongs())
            {
                var songDoc = new SongDocument
                {
                    Title = song.SongName,
                    Album = album.albumName,
                    FamilyFriendly = true // You can implement logic to determine this
                };

                // Check if song already exists
                var existingSongs = await mongoDBManager.GetAllSongsAsync();
                var existingSong = existingSongs.FirstOrDefault(s => s.Title == song.SongName && s.Album == album.albumName);

                if (existingSong == null)
                {
                    // Insert new song
                    await mongoDBManager.SongsCollection.InsertOneAsync(songDoc);
                    Debug.Log($"Added new song to MongoDB: {song.SongName}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error syncing songs for album {album.albumName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds a song to the MongoDB tracklist when it's queued
    /// </summary>
    public async Task AddSongToTracklist(string songId, string title, string artist, string album, int duration, string requestedBy = "User")
    {
        if (mongoDBManager == null) return;

        try
        {
            var tracklistEntry = await mongoDBManager.AddSongToTracklistAsync(
                songId, title, artist, album, duration, requestedBy, "master", 1);
            
            if (tracklistEntry != null)
            {
                Debug.Log($"Added song to MongoDB tracklist: {title}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error adding song to tracklist: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates tracklist status when song starts playing
    /// </summary>
    public async Task MarkSongAsPlaying(string tracklistId, string slaveId = null)
    {
        if (mongoDBManager == null) return;

        try
        {
            await mongoDBManager.UpdateTracklistStatusAsync(tracklistId, TracklistStatus.Playing, slaveId);
            Debug.Log($"Marked song as playing in MongoDB: {tracklistId}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error marking song as playing: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates tracklist status when song finishes
    /// </summary>
    public async Task MarkSongAsPlayed(string tracklistId)
    {
        if (mongoDBManager == null) return;

        try
        {
            await mongoDBManager.MarkSongAsPlayedAsync(tracklistId);
            Debug.Log($"Marked song as played in MongoDB: {tracklistId}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error marking song as played: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the current tracklist from MongoDB
    /// </summary>
    public async Task<List<TracklistEntryDocument>> GetCurrentTracklist()
    {
        if (mongoDBManager == null) return new List<TracklistEntryDocument>();

        try
        {
            return await mongoDBManager.GetQueuedSongsAsync();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error getting tracklist from MongoDB: {ex.Message}");
            return new List<TracklistEntryDocument>();
        }
    }

    /// <summary>
    /// Searches songs in MongoDB
    /// </summary>
    public async Task<List<SongDocument>> SearchSongs(string query)
    {
        if (mongoDBManager == null) return new List<SongDocument>();

        try
        {
            return await mongoDBManager.SearchSongsAsync(query);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error searching songs in MongoDB: {ex.Message}");
            return new List<SongDocument>();
        }
    }

    /// <summary>
    /// Gets play history from MongoDB
    /// </summary>
    public async Task<List<TracklistEntryDocument>> GetPlayHistory()
    {
        if (mongoDBManager == null) return new List<TracklistEntryDocument>();

        try
        {
            return await mongoDBManager.GetPlayedSongsAsync();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error getting play history from MongoDB: {ex.Message}");
            return new List<TracklistEntryDocument>();
        }
    }
}
