using System.Collections.Generic;
using UnityEngine;
using TMPro;
using MongoDBModels;
using System.Threading.Tasks;
using System.Linq;

public class MongoDBDataTester : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_Text debugText;
    public TMP_Text albumCountText;
    public TMP_Text songCountText;
    public TMP_Text tracklistCountText;

    private MongoDBManager mongoDBManager;

    private void Start()
    {
        mongoDBManager = MongoDBManager.Instance;
        if (mongoDBManager == null)
        {
            UpdateDebugText("MongoDBManager not found!");
            return;
        }

        _ = TestMongoDBData();
    }

    private async Task TestMongoDBData()
    {
        try
        {
            UpdateDebugText("Testing MongoDB data retrieval...");

            // Test albums
            var albums = await mongoDBManager.GetAllAlbumsAsync();
            UpdateDebugText($"Found {albums.Count} albums in MongoDB");
            if (albumCountText != null)
                albumCountText.text = $"Albums: {albums.Count}";

            // Test songs
            var songs = await mongoDBManager.GetAllSongsAsync();
            UpdateDebugText($"Found {songs.Count} songs in MongoDB");
            if (songCountText != null)
                songCountText.text = $"Songs: {songs.Count}";

            // Test tracklist
            var tracklist = await mongoDBManager.GetQueuedSongsAsync();
            UpdateDebugText($"Found {tracklist.Count} queued songs in tracklist");
            if (tracklistCountText != null)
                tracklistCountText.text = $"Queued: {tracklist.Count}";

            // Show sample data
            if (albums.Count > 0)
            {
                var firstAlbum = albums[0];
                UpdateDebugText($"Sample album: {firstAlbum.Title}");
            }

            if (songs.Count > 0)
            {
                var firstSong = songs[0];
                UpdateDebugText($"Sample song: {firstSong.Title} from {firstSong.Album}");
            }

            // Test songs by album
            if (albums.Count > 0)
            {
                var albumSongs = await mongoDBManager.GetSongsByAlbumAsync(albums[0].Title);
                UpdateDebugText($"Album '{albums[0].Title}' has {albumSongs.Count} songs");
            }

            UpdateDebugText("MongoDB data test completed successfully!");
        }
        catch (System.Exception ex)
        {
            UpdateDebugText($"Error testing MongoDB data: {ex.Message}");
        }
    }

    private void UpdateDebugText(string message)
    {
        if (debugText != null)
        {
            debugText.text = message;
        }
        Debug.Log($"MongoDB Data Tester: {message}");
    }

    // Public method to manually refresh data
    public void RefreshData()
    {
        _ = TestMongoDBData();
    }
}
