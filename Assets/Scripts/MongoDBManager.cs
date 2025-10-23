using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using UnityEngine;
using MongoDBModels;
using MongoDB.Bson;
public class MongoDBManager : MonoBehaviour
{
    [Header("MongoDB Settings")]
    public string connectionString = "mongodb+srv://mezragyasser2002:mezrag.yasser123...@8bbjukebox.w1btiwn.mongodb.net/?retryWrites=true&w=majority";
    public string databaseName = "jukebox";
    
    [Header("Security")]
    [SerializeField] private bool useEnvironmentVariables = true;

    private MongoClient client;
    private IMongoDatabase database;
    private IMongoCollection<AlbumDocument> albumsCollection;
    private IMongoCollection<SongDocument> songsCollection;
    private IMongoCollection<TracklistEntryDocument> tracklistCollection;
    private MonoBehaviour mainThreadDispatcher;

    public static MongoDBManager Instance { get; private set; }
    
    public void SetMainThreadDispatcher(MonoBehaviour dispatcher)
    {
        mainThreadDispatcher = dispatcher;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeMongoDB();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void InitializeMongoDB()
    {
        try
        {
            client = new MongoClient(connectionString);
            database = client.GetDatabase(databaseName);
            
            albumsCollection = database.GetCollection<AlbumDocument>("albums");
            songsCollection = database.GetCollection<SongDocument>("songs");
            tracklistCollection = database.GetCollection<TracklistEntryDocument>("tracklist");

            Debug.Log("MongoDB connection initialized successfully");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to initialize MongoDB: {ex.Message}");
        }
    }

    // AlbumDocument operations
    public async Task<List<AlbumDocument>> GetAllAlbumsAsync()
    {
        try
        {
            return await albumsCollection.Find(_ => true).ToListAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error getting albums: {ex.Message}");
            return new List<AlbumDocument>();
        }
    }

    // SongDocument operations
    public async Task<List<SongDocument>> GetAllSongsAsync()
    {
        try
        {
            return await songsCollection.Find(_ => true).ToListAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error getting songs: {ex.Message}");
            return new List<SongDocument>();
        }
    }

    public async Task<List<SongDocument>> GetSongsByAlbumAsync(string albumTitle)
    {
        try
        {
            return await songsCollection.Find(s => s.Album == albumTitle).ToListAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error getting songs by AlbumDocument: {ex.Message}");
            return new List<SongDocument>();
        }
    }

    public async Task<SongDocument> GetSongByIdAsync(string songId)
    {
        try
        {
            return await songsCollection.Find(s => s.Id == songId).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error getting SongDocument by ID: {ex.Message}");
            return null;
        }
    }

    // Tracklist operations
    public async Task<List<TracklistEntryDocument>> GetQueuedSongsAsync()
    {
        try
        {
            return await tracklistCollection
                .Find(t => t.Status == TracklistStatus.Queued)
                .Sort(Builders<TracklistEntryDocument>.Sort.Ascending(t => t.Priority).Ascending(t => t.CreatedAt))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error getting queued songs: {ex.Message}");
            return new List<TracklistEntryDocument>();
        }
    }

    public async Task<List<TracklistEntryDocument>> GetPlayingSongsAsync()
    {
        try
        {
            return await tracklistCollection.Find(t => t.Status == TracklistStatus.Playing).ToListAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error getting playing songs: {ex.Message}");
            return new List<TracklistEntryDocument>();
        }
    }

    public async Task<List<TracklistEntryDocument>> GetPlayedSongsAsync()
    {
        try
        {
            return await tracklistCollection
                .Find(t => t.Status == TracklistStatus.Played)
                .Sort(Builders<TracklistEntryDocument>.Sort.Descending(t => t.PlayedAt))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error getting played songs: {ex.Message}");
            return new List<TracklistEntryDocument>();
        }
    }

    public async Task<TracklistEntryDocument> AddSongToTracklistAsync(string songId, string title, string artist, string AlbumDocument, int duration, string requestedBy, string masterId, int priority = 1)
    {
        try
        {
            var TracklistEntryDocument = new TracklistEntryDocument
            {
                SongId = songId,
                Title = title,
                Artist = artist,
                Album = AlbumDocument,
                Duration = duration,
                Status = TracklistStatus.Queued,
                Priority = priority,
                CreatedAt = DateTime.UtcNow,
                PlayedAt = null,
                RequestedBy = requestedBy,
                MasterId = masterId,
                SlaveId = null,
                ExistsAtMaster = false // Will be updated by master after validation
            };

            await tracklistCollection.InsertOneAsync(TracklistEntryDocument);
            Debug.Log($"Added song to tracklist: {title} by {artist}");
            return TracklistEntryDocument;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error adding song to tracklist: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> UpdateTracklistStatusAsync(string tracklistId, string status, string slaveId = null)
    {
        try
        {
            var filter = Builders<TracklistEntryDocument>.Filter.Eq(t => t.Id, tracklistId);
            var update = Builders<TracklistEntryDocument>.Update
                .Set(t => t.Status, status)
                .Set(t => t.SlaveId, slaveId);

            if (status == TracklistStatus.Playing || status == TracklistStatus.Played)
            {
                update = update.Set(t => t.PlayedAt, DateTime.UtcNow);
            }

            var result = await tracklistCollection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error updating tracklist status: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SkipCurrentSongAsync()
    {
        try
        {
            var playingSongs = await GetPlayingSongsAsync();
            foreach (var SongDocument in playingSongs)
            {
                await UpdateTracklistStatusAsync(SongDocument.Id, TracklistStatus.Skipped);
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error skipping current SongDocument: {ex.Message}");
            return false;
        }
    }

    public async Task<TracklistEntryDocument> GetNextSongAsync()
    {
        try
        {
            var queuedSongs = await GetQueuedSongsAsync();
            if (queuedSongs.Count > 0)
            {
                var nextSong = queuedSongs[0];
                await UpdateTracklistStatusAsync(nextSong.Id, TracklistStatus.Playing);
                return nextSong;
            }
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error getting next SongDocument: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> MarkSongAsPlayedAsync(string tracklistId)
    {
        try
        {
            return await UpdateTracklistStatusAsync(tracklistId, TracklistStatus.Played);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error marking SongDocument as played: {ex.Message}");
            return false;
        }
    }

    // Search functionality
    public async Task<List<SongDocument>> SearchSongsAsync(string query)
    {
        try
        {
            var filter = Builders<SongDocument>.Filter.Regex(s => s.Title, new MongoDB.Bson.BsonRegularExpression(query, "i"));
            return await songsCollection.Find(filter).ToListAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error searching songs: {ex.Message}");
            return new List<SongDocument>();
        }
    }

    // Additional utility methods
    public async Task<bool> ClearTracklistAsync()
    {
        try
        {
            var result = await tracklistCollection.DeleteManyAsync(_ => true);
            Debug.Log($"Cleared {result.DeletedCount} tracklist entries");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error clearing tracklist: {ex.Message}");
            return false;
        }
    }

    public async Task<List<TracklistEntryDocument>> GetTracklistByStatusAsync(string status)
    {
        try
        {
            return await tracklistCollection
                .Find(t => t.Status == status)
                .Sort(Builders<TracklistEntryDocument>.Sort.Ascending(t => t.Priority).Ascending(t => t.CreatedAt))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error getting tracklist by status: {ex.Message}");
            return new List<TracklistEntryDocument>();
        }
    }

    public async Task<bool> UpdateSongFamilyFriendlyAsync(string songId, bool familyFriendly)
    {
        try
        {
            var filter = Builders<SongDocument>.Filter.Eq(s => s.Id, songId);
            var update = Builders<SongDocument>.Update.Set(s => s.FamilyFriendly, familyFriendly);
            var result = await songsCollection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error updating song family friendly status: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UpdateExistsAtMasterAsync(string tracklistId, bool existsAtMaster, int length = 0)
    {
        try
        {
            // Validate ObjectId format
            if (!ObjectId.TryParse(tracklistId, out ObjectId objectId))
            {
                Debug.LogError($"[MONGODB] Invalid ObjectId format: {tracklistId}");
                return false;
            }
            
            // Use string ID directly since it's represented as ObjectId in MongoDB
            var filter = Builders<TracklistEntryDocument>.Filter.Eq(t => t.Id, tracklistId);
            var update = Builders<TracklistEntryDocument>.Update
                .Set(t => t.ExistsAtMaster, existsAtMaster);
            
            // Only set length if existsAtMaster is true and length is provided
            if (existsAtMaster && length > 0)
            {
                update = update.Set(t => t.Length, length);
            }
            
            var result = await tracklistCollection.UpdateOneAsync(filter, update);
            Debug.Log($"[MONGODB] Update result: ModifiedCount={result.ModifiedCount}, MatchedCount={result.MatchedCount}");
            return result.ModifiedCount > 0;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MONGODB] Error updating existsAtMaster status: {ex.Message}");
            return false;
        }
    }
    

    // Expose collections for direct access if needed
    public IMongoCollection<AlbumDocument> AlbumsCollection => albumsCollection;
    public IMongoCollection<SongDocument> SongsCollection => songsCollection;
    public IMongoCollection<TracklistEntryDocument> TracklistCollection => tracklistCollection;

    // Real-time Change Streams

    public async Task StartTracklistChangeStream()
    {
        try
        {
            Debug.Log("[MONGODB] Starting real-time tracklist change stream...");
            
            var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<TracklistEntryDocument>>()
                .Match(change => change.OperationType == ChangeStreamOperationType.Insert ||
                                change.OperationType == ChangeStreamOperationType.Update ||
                                change.OperationType == ChangeStreamOperationType.Replace);

            using var cursor = await tracklistCollection.WatchAsync(pipeline);
            
            Debug.Log("[MONGODB] Change stream started successfully");
            
            await cursor.ForEachAsync(change =>
            {
                // Schedule the change processing on the main thread using stored dispatcher
                if (mainThreadDispatcher != null)
                {
                    mainThreadDispatcher.StartCoroutine(ProcessChangeOnMainThread(change));
                }
                else
                {
                    Debug.LogWarning("[MONGODB] Main thread dispatcher not set, cannot process change stream events");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MONGODB] Error in change stream: {ex.Message}");
        }
    }
    
    private System.Collections.IEnumerator ProcessChangeOnMainThread(ChangeStreamDocument<TracklistEntryDocument> change)
    {
        // Wait one frame to ensure we're on the main thread
        yield return null;
        
        Debug.Log($"[MONGODB] Real-time change detected: {change.OperationType} on document {change.DocumentKey}");
        
        // Notify TrackQueueManager of the change
        if (OnTracklistChanged != null)
        {
            OnTracklistChanged.Invoke(change);
        }
    }

    // Event for real-time notifications
    public static event System.Action<ChangeStreamDocument<TracklistEntryDocument>> OnTracklistChanged;

    public async Task<List<TracklistEntryDocument>> GetAllTracklistEntriesAsync()
    {
        try
        {
            return await tracklistCollection.Find(_ => true).ToListAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error getting all tracklist entries: {ex.Message}");
            return new List<TracklistEntryDocument>();
        }
    }

    public async Task<bool> DeleteTracklistEntryAsync(string tracklistId)
    {
        try
        {
            var result = await tracklistCollection.DeleteOneAsync(t => t.Id == tracklistId);
            return result.DeletedCount > 0;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error deleting tracklist entry {tracklistId}: {ex.Message}");
            return false;
        }
    }
}
