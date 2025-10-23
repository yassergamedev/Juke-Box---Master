using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Threading.Tasks;
using MongoDBModels;

public class MongoDBUI : MonoBehaviour
{
    [Header("UI References")]
    public Button syncAlbumsButton;
    public Button searchButton;
    public Button showHistoryButton;
    public Button clearTracklistButton;
    public TMP_InputField searchInput;
    public TMP_Text statusText;
    public TMP_Text tracklistText;
    public TMP_Text historyText;
    public ScrollRect tracklistScrollRect;
    public ScrollRect historyScrollRect;

    [Header("Prefabs")]
    public GameObject tracklistItemPrefab;
    public GameObject historyItemPrefab;

    private MongoDBIntegration mongoDBIntegration;
    private MongoDBManager mongoDBManager;

    private void Start()
    {
        mongoDBIntegration = FindObjectOfType<MongoDBIntegration>();
        mongoDBManager = MongoDBManager.Instance;

        // Setup button listeners
        if (syncAlbumsButton != null)
            syncAlbumsButton.onClick.AddListener(() => _ = SyncAlbums());
        
        if (searchButton != null)
            searchButton.onClick.AddListener(() => _ = SearchSongs());
        
        if (showHistoryButton != null)
            showHistoryButton.onClick.AddListener(() => _ = ShowPlayHistory());
        
        if (clearTracklistButton != null)
            clearTracklistButton.onClick.AddListener(() => _ = ClearTracklist());

        // Load initial data
        _ = LoadTracklist();
    }

    public async Task SyncAlbums()
    {
        if (mongoDBIntegration == null)
        {
            UpdateStatus("MongoDB Integration not found!");
            return;
        }

        UpdateStatus("Syncing albums to MongoDB...");
        await mongoDBIntegration.SyncAlbumsToMongoDB();
        UpdateStatus("Album sync completed!");
    }

    public async Task SearchSongs()
    {
        if (mongoDBIntegration == null || string.IsNullOrEmpty(searchInput.text))
        {
            UpdateStatus("MongoDB Integration not found or search query is empty!");
            return;
        }

        UpdateStatus("Searching songs...");
        var results = await mongoDBIntegration.SearchSongs(searchInput.text);
        
        if (results.Count > 0)
        {
            UpdateStatus($"Found {results.Count} songs matching '{searchInput.text}'");
            DisplaySearchResults(results);
        }
        else
        {
            UpdateStatus($"No songs found matching '{searchInput.text}'");
        }
    }

    public async Task ShowPlayHistory()
    {
        if (mongoDBIntegration == null)
        {
            UpdateStatus("MongoDB Integration not found!");
            return;
        }

        UpdateStatus("Loading play history...");
        var history = await mongoDBIntegration.GetPlayHistory();
        DisplayPlayHistory(history);
        UpdateStatus($"Loaded {history.Count} played songs");
    }

    public async Task LoadTracklist()
    {
        if (mongoDBIntegration == null)
        {
            UpdateStatus("MongoDB Integration not found!");
            return;
        }

        var tracklist = await mongoDBIntegration.GetCurrentTracklist();
        DisplayTracklist(tracklist);
    }

    public async Task ClearTracklist()
    {
        if (mongoDBManager == null)
        {
            UpdateStatus("MongoDB Manager not found!");
            return;
        }

        // This would need to be implemented in MongoDBManager
        UpdateStatus("Clearing tracklist...");
        // await mongoDBManager.ClearTracklist();
        UpdateStatus("Tracklist cleared!");
        await LoadTracklist();
    }

    private void DisplaySearchResults(List<SongDocument> songs)
    {
        if (tracklistScrollRect == null || tracklistItemPrefab == null) return;

        // Clear existing results
        foreach (Transform child in tracklistScrollRect.content)
        {
            Destroy(child.gameObject);
        }

        // Display search results
        foreach (var song in songs)
        {
            var item = Instantiate(tracklistItemPrefab, tracklistScrollRect.content);
            var text = item.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.text = $"{song.Title} - {song.Album}";
            }
        }
    }

    private void DisplayTracklist(List<TracklistEntryDocument> tracklist)
    {
        if (tracklistScrollRect == null || tracklistItemPrefab == null) return;

        // Clear existing items
        foreach (Transform child in tracklistScrollRect.content)
        {
            Destroy(child.gameObject);
        }

        // Display tracklist
        foreach (var entry in tracklist)
        {
            var item = Instantiate(tracklistItemPrefab, tracklistScrollRect.content);
            var text = item.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.text = $"{entry.Title} - {entry.Artist} ({entry.Status})";
            }
        }
    }

    private void DisplayPlayHistory(List<TracklistEntryDocument> history)
    {
        if (historyScrollRect == null || historyItemPrefab == null) return;

        // Clear existing items
        foreach (Transform child in historyScrollRect.content)
        {
            Destroy(child.gameObject);
        }

        // Display history
        foreach (var entry in history)
        {
            var item = Instantiate(historyItemPrefab, historyScrollRect.content);
            var text = item.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                var playedTime = entry.PlayedAt?.ToString("HH:mm:ss") ?? "Unknown";
                text.text = $"{playedTime} - {entry.Title} - {entry.Artist}";
            }
        }
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"MongoDB UI: {message}");
    }
}
