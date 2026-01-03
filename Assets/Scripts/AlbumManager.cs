using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SFB;
using System.Collections;
using UnityEngine.UIElements;
using System;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDBModels;

[Serializable]
public class AlbumData
{
    public string AlbumName;
    public string ArtistName;
    public string CoverPath;
    public string AlbumPath;
    public List<SongData> Songs = new List<SongData>();
}

[Serializable]
public class SongData
{
    public string SongName;
    public string AudioPath;
}
[System.Serializable]
public class AlbumDataListWrapper
{
    public List<AlbumData> albums;

    public AlbumDataListWrapper(List<AlbumData> albumDataList)
    {
        this.albums = albumDataList;
    }
}

public class AlbumManager : MonoBehaviour
{
    public static AlbumManager Instance { get; private set; }

    public Transform AlbumContainer;
    public Transform UnseenAlbums;
    public Album AlbumPrefab;
    public Song SongPrefab;

    public UnityEngine.UI.Button NextButton;
    public UnityEngine.UI.Button PreviousButton;

    public TMP_InputField SearchInput;
    public Transform SearchResultContainer;
    public Song SearchResultPrefab;

    public List<Album> albums = new List<Album>();
    private List<Album> activeAlbums = new List<Album>();
    private int currentAlbumIndex = 0;
    private MasterNetworkHandler master;
   
    public bool isSlave;
    public Text debugText;

    private MongoDBManager mongoDBManager;
    private List<MongoDBModels.AlbumDocument> mongoAlbums = new List<MongoDBModels.AlbumDocument>();
    private List<MongoDBModels.SongDocument> mongoSongs = new List<MongoDBModels.SongDocument>();
    private List<AlbumData> albumDataList = new List<AlbumData>();
    [Header("Where to scan for albums")]
    public string AlbumBasePath;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    async void Start()
    {
        // Load AlbumBasePath from PlayerPrefs
        AlbumBasePath = PlayerPrefs.GetString("AlbumBasePath", "");
        if (string.IsNullOrEmpty(AlbumBasePath))
        {
            UpdateDebugText("Please select an albums folder first using the 'Select Albums Folder' button.");
            Debug.LogWarning("AlbumBasePath not set. Please select an albums folder first.");
        }
        else
        {
            Debug.Log($"Loaded AlbumBasePath from PlayerPrefs: {AlbumBasePath}");
        }

        mongoDBManager = MongoDBManager.Instance;
        if (mongoDBManager == null)
        {
            Debug.LogError("MongoDBManager not found!");
            return;
        }

        await LoadAlbumsFromMongoDB();
        UpdateButtonStates();
        master = FindAnyObjectByType<MasterNetworkHandler>();
    }
    public void UpdateDebugText(string message)
    {
        if (debugText != null)
        {
            debugText.text = message;
        }
        Debug.Log(message);
    }

    public async Task LoadAlbumsFromMongoDB()
    {
        try
        {
            UpdateDebugText("Loading albums from MongoDB...");
            
            // Load albums and songs from MongoDB
            mongoAlbums = await mongoDBManager.GetAllAlbumsAsync();
            mongoSongs = await mongoDBManager.GetAllSongsAsync();
            
            // Clear existing UI albums
            ClearAlbums();
            albums.Clear();
            activeAlbums.Clear();

            // Create UI albums from MongoDB data
            int albumNumber = 1;
            foreach (var mongoAlbum in mongoAlbums)
            {
                var albumSongs = mongoSongs.FindAll(s => s.Album == mongoAlbum.Title);
                
                if (albumSongs.Count > 0)
                {
                    // Create album UI object
                    Album albumInstance = Instantiate(AlbumPrefab, UnseenAlbums);
                    albumInstance.Initialize(mongoAlbum.Title, mongoAlbum.Artist ?? "Various Artists", null, "", albumNumber);
                    albums.Add(albumInstance);

                    // Add songs to the album
                    int trackNumber = 1;
                    foreach (var mongoSong in albumSongs)
                    {
                        // Extract artist from song title if possible (assuming format: "Artist - Song Title")
                        string artist = "Unknown Artist";
                        string songTitle = mongoSong.Title;
                        
                        if (mongoSong.Title.Contains(" - "))
                        {
                            var parts = mongoSong.Title.Split(new string[] { " - " }, 2, StringSplitOptions.None);
                            if (parts.Length == 2)
                            {
                                songTitle = parts[0];
                                artist = parts[1];
                            }
                        }

                        // Don't search for file paths here - wait until song is actually added to queue
                        // Just pass empty audio path for now
                        albumInstance.AddSong(SongPrefab, songTitle, artist, "", trackNumber);
                        trackNumber++;
                    }
                    
                    albumNumber++;
                }
            }

            InitializeAlbums();
            UpdateDebugText($"Loaded {albums.Count} albums from MongoDB");
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error loading albums from MongoDB: {ex.Message}");
            Debug.LogError($"Error loading albums from MongoDB: {ex.Message}");
        }
    }
    public async void SelectSongsFolder()
    {
        var paths = StandaloneFileBrowser.OpenFolderPanel("Select Songs Folder", "", false);

        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            string songsFolderPath = paths[0];
            string[] supportedExtensions = { ".mp3", ".wav", ".ogg" };

            var allFiles = Directory.GetFiles(songsFolderPath);
            var nonSongFiles = allFiles.Where(file => !supportedExtensions.Contains(Path.GetExtension(file).ToLower())).ToList();

            if (nonSongFiles.Count > 0)
            {
                UpdateDebugText("The selected folder contains non-song files. Please select a folder with only .mp3, .wav, or .ogg files.");
                return;
            }

            PlayerPrefs.SetString("FriendlyAlbumsPath", songsFolderPath);
            Debug.Log($"Selected songs folder: {songsFolderPath}");
            UpdateDebugText("Songs folder successfully selected. Scanning friendly songs...");
            
            // Scan and add friendly songs to MongoDB
            await ScanFriendlySongs(songsFolderPath);
        }
        else
        {
            UpdateDebugText("No songs folder selected.");
        }
    }
    
    /// <summary>
    /// Scans the friendly songs folder and adds songs to MongoDB with FamilyFriendly=true
    /// </summary>
    private async Task ScanFriendlySongs(string friendlyFolderPath)
    {
        try
        {
            UpdateDebugText("Scanning friendly songs folder...");
            HubLogger.Log("Scanning friendly songs folder", LogCategory.Files);
            
            string[] supportedExtensions = { ".mp3", ".wav", ".ogg" };
            var audioFiles = Directory.GetFiles(friendlyFolderPath)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();
            
            if (audioFiles.Count == 0)
            {
                UpdateDebugText("No audio files found in friendly songs folder.");
                HubLogger.LogWarning("No audio files found in friendly songs folder", LogCategory.Files);
                return;
            }
            
            HubLogger.Log($"Found {audioFiles.Count} audio files in friendly folder", LogCategory.Files);
            
            // Get existing songs from MongoDB
            var existingSongs = await mongoDBManager.GetAllSongsAsync();
            int addedCount = 0;
            int updatedCount = 0;
            
            // Create a virtual "Friendly Songs" album for organization
            string friendlyAlbumName = "Friendly Songs";
            string friendlyArtist = "Various Artists";
            
            // Check if album exists, create if not
            var existingAlbums = await mongoDBManager.GetAllAlbumsAsync();
            var friendlyAlbum = existingAlbums.FirstOrDefault(a => a.Title == friendlyAlbumName);
            if (friendlyAlbum == null)
            {
                await mongoDBManager.AddAlbumAsync(friendlyAlbumName, friendlyArtist);
                HubLogger.LogSuccess($"Created friendly album: {friendlyAlbumName}", LogCategory.Albums);
            }
            
            // Process each audio file
            foreach (var audioFile in audioFiles)
            {
                string songTitle = GetSongTitle(audioFile);
                if (string.IsNullOrEmpty(songTitle))
                {
                    songTitle = Path.GetFileNameWithoutExtension(audioFile);
                }
                
                // Check if song already exists
                var existingSong = existingSongs.FirstOrDefault(s => 
                    s.Title == songTitle && s.Album == friendlyAlbumName);
                
                if (existingSong == null)
                {
                    // New song - add with FamilyFriendly=true
                    bool added = await mongoDBManager.AddSongAsync(
                        songTitle, 
                        friendlyArtist, 
                        friendlyAlbumName, 
                        familyFriendly: true
                    );
                    if (added)
                    {
                        addedCount++;
                        HubLogger.LogSuccess($"Added friendly song: {songTitle}", LogCategory.Files);
                    }
                }
                else if (!existingSong.FamilyFriendly)
                {
                    // Song exists but not marked as friendly - update it
                    bool updated = await mongoDBManager.UpdateSongFamilyFriendlyAsync(existingSong.Id, true);
                    if (updated)
                    {
                        updatedCount++;
                        HubLogger.LogSuccess($"Updated song to friendly: {songTitle}", LogCategory.Files);
                    }
                }
            }
            
            UpdateDebugText($"Friendly songs scan complete. Added: {addedCount}, Updated: {updatedCount}");
            HubLogger.LogSuccess($"Friendly songs scan complete - Added: {addedCount}, Updated: {updatedCount}", LogCategory.Files);
        }
        catch (Exception ex)
        {
            string errorMsg = $"Error scanning friendly songs: {ex.Message}";
            UpdateDebugText(errorMsg);
            HubLogger.LogFailure(errorMsg, LogCategory.Files);
            Debug.LogError($"[ALBUM_MANAGER] {errorMsg}");
        }
    }

    public void SelectAlbumsFolder()
    {
        var paths = StandaloneFileBrowser.OpenFolderPanel("Select Albums Folder", "", false);

        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            string albumsFolderPath = paths[0];
            
            // Validate that the folder contains album subfolders
            var subfolders = Directory.GetDirectories(albumsFolderPath);
            if (subfolders.Length == 0)
            {
                UpdateDebugText("The selected folder contains no subfolders. Please select a folder containing album subfolders.");
                return;
            }

            // Check if subfolders contain audio files
            bool hasAudioFiles = false;
            string[] supportedExtensions = { ".mp3", ".wav", ".ogg" };
            
            foreach (var subfolder in subfolders)
            {
                var audioFiles = Directory.GetFiles(subfolder, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()));
                
                if (audioFiles.Any())
                {
                    hasAudioFiles = true;
                    break;
                }
            }

            if (!hasAudioFiles)
            {
                UpdateDebugText("No audio files found in the album subfolders. Please select a folder containing album subfolders with audio files.");
                return;
            }

            // Set the album base path
            AlbumBasePath = albumsFolderPath;
            PlayerPrefs.SetString("AlbumBasePath", albumsFolderPath);
            
            Debug.Log($"Selected albums folder: {albumsFolderPath}");
            UpdateDebugText($"Albums folder successfully selected: {albumsFolderPath}");
            UpdateDebugText($"Found {subfolders.Length} album subfolders");
            
            // Reload albums from MongoDB with the new path
            StartCoroutine(ReloadAlbumsAfterPathChange());
        }
        else
        {
            UpdateDebugText("No albums folder selected.");
        }
    }

    private IEnumerator ReloadAlbumsAfterPathChange()
    {
        UpdateDebugText("Reloading albums with new folder path...");
        
        // Clear existing albums
        ClearAlbums();
        albums.Clear();
        activeAlbums.Clear();
        
        // Wait a frame to ensure UI is cleared
        yield return null;
        
        // Reload from MongoDB
        yield return StartCoroutine(LoadAlbumsFromMongoDBCoroutine());
        
        UpdateDebugText($"Reloaded {albums.Count} albums with new folder path");
    }

    private IEnumerator LoadAlbumsFromMongoDBCoroutine()
    {
        // Load albums and songs from MongoDB using coroutine-compatible approach
        var albumsTask = mongoDBManager.GetAllAlbumsAsync();
        var songsTask = mongoDBManager.GetAllSongsAsync();
        
        // Wait for both tasks to complete
        yield return new WaitUntil(() => albumsTask.IsCompleted && songsTask.IsCompleted);
        
        try
        {
            mongoAlbums = albumsTask.Result;
            mongoSongs = songsTask.Result;

            Debug.Log($"[ALBUM_MANAGER] Loaded {mongoAlbums.Count} albums and {mongoSongs.Count} songs from MongoDB");

            // Clear existing UI
            ClearAlbums();
            albums.Clear();
            activeAlbums.Clear();

            // Create UI albums from MongoDB data
            int albumNumber = 1;
            foreach (var mongoAlbum in mongoAlbums)
            {
                var albumSongs = mongoSongs.FindAll(s => s.Album == mongoAlbum.Title);
                
                if (albumSongs.Count > 0)
                {
                    // Create album UI object
                    Album albumInstance = Instantiate(AlbumPrefab, UnseenAlbums);
                    albumInstance.Initialize(mongoAlbum.Title, mongoAlbum.Artist ?? "Various Artists", null, "", albumNumber);
                    albums.Add(albumInstance);

                    // Add songs to the album
                    int trackNumber = 1;
                    foreach (var mongoSong in albumSongs)
                    {
                        // Extract artist from song title if possible (assuming format: "Artist - Song Title")
                        string artist = "Unknown Artist";
                        string songTitle = mongoSong.Title;
                        
                        if (mongoSong.Title.Contains(" - "))
                        {
                            var parts = mongoSong.Title.Split(new string[] { " - " }, 2, StringSplitOptions.None);
                            if (parts.Length == 2)
                            {
                                songTitle = parts[0];
                                artist = parts[1];
                            }
                        }

                        // Don't search for file paths here - wait until song is actually added to queue
                        // Just pass empty audio path for now
                        albumInstance.AddSong(SongPrefab, songTitle, artist, "", trackNumber);
                        trackNumber++;
                    }
                    
                    albumNumber++;
                }
            }

            InitializeAlbums();
            UpdateDebugText($"Loaded {albums.Count} albums from MongoDB");
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error loading albums from MongoDB: {ex.Message}");
            Debug.LogError($"Error loading albums from MongoDB: {ex.Message}");
        }
        
        yield return null; // Ensure coroutine completes
    }

    public void ActivateNextFourAlbums()
    {
        StartCoroutine(SwitchAlbumsCoroutine(true));
    }

    public void ActivatePreviousFourAlbums()
    {
        StartCoroutine(SwitchAlbumsCoroutine(false));
    }
    private IEnumerator SwitchAlbumsCoroutine(bool isNext)
    {
        foreach (var album in activeAlbums)
        {
            album.transform.SetParent(UnseenAlbums);
            album.gameObject.SetActive(false);
        }
        activeAlbums.Clear();

        if (isNext)
        {
            int remainingAlbums = albums.Count - (currentAlbumIndex + activeAlbums.Count);
            int step = Mathf.Min(remainingAlbums, 4);
            currentAlbumIndex += step;
        }
        else
        {
            currentAlbumIndex = Mathf.Max(currentAlbumIndex - 4, 0);
        }

        int startIndex = currentAlbumIndex;
        int endIndex = Mathf.Min(startIndex + 4, albums.Count);

        for (int i = startIndex; i < endIndex; i++)
        {
            Album album = albums[i];
            album.transform.SetParent(AlbumContainer);
            album.gameObject.SetActive(true);
            activeAlbums.Add(album);
        }

        yield return null;

        UpdateButtonStates(); // Update button states after switching albums
    }
    private string GetSongTitle(string filePath)
    {
        try
        {
            using (var file = TagLib.File.Create(filePath))
            {
                return file.Tag.Title; // Fetch the song title from metadata
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error reading metadata from {filePath}: {ex.Message}");
            return null;
        }
    }

    public string FindAlbumFolder(string albumTitle)
    {
        try
        {
            if (string.IsNullOrEmpty(AlbumBasePath) || !Directory.Exists(AlbumBasePath))
            {
                Debug.LogWarning($"[ALBUM_MANAGER] AlbumBasePath is invalid: {AlbumBasePath}");
                return "";
            }

            // Search for folders that contain the album title
            var albumFolders = Directory.GetDirectories(AlbumBasePath);
            
            Debug.Log($"[ALBUM_MANAGER] Searching for album '{albumTitle}' in {albumFolders.Length} folders");

            // First try exact match
            foreach (var folder in albumFolders)
            {
                string folderName = Path.GetFileName(folder);
                if (folderName.Equals(albumTitle, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[ALBUM_MANAGER] Found exact album folder match: {folder}");
                    return folder;
                }
            }

            // Then try partial match (in case folder has "Artist - Album" format)
            foreach (var folder in albumFolders)
            {
                string folderName = Path.GetFileName(folder);
                if (folderName.Contains(albumTitle, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[ALBUM_MANAGER] Found partial album folder match: {folder} (contains: {albumTitle})");
                    return folder;
                }
            }

            // Finally try reverse match (album title contains folder name)
            foreach (var folder in albumFolders)
            {
                string folderName = Path.GetFileName(folder);
                if (albumTitle.Contains(folderName, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[ALBUM_MANAGER] Found reverse album folder match: {folder} (album contains: {folderName})");
                    return folder;
                }
            }

            Debug.LogWarning($"[ALBUM_MANAGER] No album folder found for: {albumTitle}");
            return "";
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ALBUM_MANAGER] Error finding album folder for '{albumTitle}': {ex.Message}");
            return "";
        }
    }

    public string FindSongFilePath(string albumPath, string songTitle)
    {
        try
        {
            if (string.IsNullOrEmpty(albumPath) || !Directory.Exists(albumPath))
            {
                Debug.LogWarning($"[ALBUM_MANAGER] Album path is invalid: {albumPath}");
                return "";
            }

            string[] supportedExtensions = { ".mp3", ".wav", ".ogg" };
            
            // Search for files in the album folder
            var audioFiles = Directory.GetFiles(albumPath, "*", SearchOption.TopDirectoryOnly)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToArray();

            Debug.Log($"[ALBUM_MANAGER] Searching for '{songTitle}' in {audioFiles.Length} files in {albumPath}");

            // First try exact filename match
            foreach (var file in audioFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.Equals(songTitle, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[ALBUM_MANAGER] Found exact filename match: {file}");
                    return file;
                }
            }

            // Then try ID3 tag title match
            foreach (var file in audioFiles)
            {
                string id3Title = GetSongTitle(file);
                if (!string.IsNullOrEmpty(id3Title) && id3Title.Equals(songTitle, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[ALBUM_MANAGER] Found ID3 title match: {file} (title: {id3Title})");
                    return file;
                }
            }

            // Finally try partial match (in case of slight differences)
            foreach (var file in audioFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string id3Title = GetSongTitle(file);
                
                if ((!string.IsNullOrEmpty(id3Title) && id3Title.Contains(songTitle, StringComparison.OrdinalIgnoreCase)) ||
                    fileName.Contains(songTitle, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[ALBUM_MANAGER] Found partial match: {file} (filename: {fileName}, id3: {id3Title})");
                    return file;
                }
            }

            Debug.LogWarning($"[ALBUM_MANAGER] No file found for song title: {songTitle} in {albumPath}");
            return "";
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ALBUM_MANAGER] Error finding file for song '{songTitle}': {ex.Message}");
            return "";
        }
    }
    public async void ScanForAlbums()
    {
        if (!Directory.Exists(AlbumBasePath))
        {
            UpdateDebugText($"Album folder path not found: {AlbumBasePath}");
            return;
        }
        
        UpdateDebugText("Scanning for new, deleted albums and songs...");
        
        try
        {
            // Get current albums and songs from MongoDB
            var existingAlbums = await mongoDBManager.GetAllAlbumsAsync();
            var existingSongs = await mongoDBManager.GetAllSongsAsync();
            
            // Track albums and songs that exist in the file system
            var fileSystemAlbums = new HashSet<(string Album, string Artist)>();
            var fileSystemSongs = new HashSet<(string Title, string Album)>();
            
            // Scan the folder for new content and track what exists
            var newAlbums = new List<MongoDBModels.AlbumDocument>();
            var newSongs = new List<MongoDBModels.SongDocument>();
            
            int albNum = 0;
            foreach (var directory in Directory.GetDirectories(AlbumBasePath))
            {
                string folderName = Path.GetFileName(directory);
                string[] nameParts = folderName.Split('-');
                if (nameParts.Length < 2)
                {
                    UpdateDebugText($"Skipping invalid album folder: {folderName}. Expected format: 'Artist Name - Album Name'");
                    continue;
                }

                string artistName = nameParts[0].Trim();
                string albumName = nameParts[1].Trim();
                
                // Track this album as existing in file system
                fileSystemAlbums.Add((albumName, artistName));
                
                // Check if album already exists in MongoDB
                var existingAlbum = existingAlbums.FirstOrDefault(a => a.Title == albumName && a.Artist == artistName);
                if (existingAlbum == null)
                {
                    // New album found - add to MongoDB
                    var newAlbum = new MongoDBModels.AlbumDocument
                    {
                        Title = albumName,
                        Artist = artistName
                    };
                    newAlbums.Add(newAlbum);
                    Debug.Log($"[ALBUM_MANAGER] New album found: {albumName} by {artistName}");
                }

                // Check for album cover
                string[] coverExtensions = { ".png", ".jpg" };
                string coverPath = null;
                foreach (var ext in coverExtensions)
                {
                    string potentialPath = Path.Combine(directory, $"cover{ext}");
                    if (System.IO.File.Exists(potentialPath))
                    {
                        coverPath = potentialPath;
                        break;
                    }
                }

                if (coverPath == null)
                {
                    UpdateDebugText($"No cover image found for album: {folderName}");
                    continue;
                }

                // Scan songs in this album
                string[] audioFiles = Directory.GetFiles(directory, "*.mp3");
                foreach (var audioFile in audioFiles)
                {
                    string songTitle = GetSongTitle(audioFile);
                    if (string.IsNullOrEmpty(songTitle))
                    {
                        songTitle = Path.GetFileNameWithoutExtension(audioFile); // Fallback to filename
                    }
                    
                    // Track this song as existing in file system
                    fileSystemSongs.Add((songTitle, albumName));
                    
                    // Check if song already exists in MongoDB
                    var existingSong = existingSongs.FirstOrDefault(s => s.Title == songTitle && s.Album == albumName);
                    if (existingSong == null)
                    {
                        // New song found - add to MongoDB
                        var newSong = new MongoDBModels.SongDocument
                        {
                            Title = songTitle,
                            Artist = artistName,
                            Album = albumName,
                            FamilyFriendly = false // Default to false, can be changed later
                        };
                        newSongs.Add(newSong);
                        Debug.Log($"[ALBUM_MANAGER] New song found: {songTitle} in album {albumName}");
                    }
                }
            }
            
            // Add new albums to MongoDB
            foreach (var album in newAlbums)
            {
                await mongoDBManager.AddAlbumAsync(album.Title, album.Artist);
                Debug.Log($"[ALBUM_MANAGER] Added new album to MongoDB: {album.Title}");
            }
            
            // Add new songs to MongoDB
            foreach (var song in newSongs)
            {
                await mongoDBManager.AddSongAsync(song.Title, song.Artist, song.Album, song.FamilyFriendly);
                Debug.Log($"[ALBUM_MANAGER] Added new song to MongoDB: {song.Title}");
            }
            
            // Detect and delete albums that no longer exist in file system
            int deletedAlbumsCount = 0;
            foreach (var album in existingAlbums)
            {
                if (!fileSystemAlbums.Contains((album.Title, album.Artist)))
                {
                    Debug.Log($"[ALBUM_MANAGER] Album deleted from file system: {album.Title} by {album.Artist}");
                    // Delete all songs from this album first
                    await mongoDBManager.DeleteSongsByAlbumAsync(album.Title);
                    // Then delete the album
                    await mongoDBManager.DeleteAlbumAsync(album.Title, album.Artist);
                    deletedAlbumsCount++;
                }
            }
            
            // Detect and delete songs that no longer exist in file system
            int deletedSongsCount = 0;
            foreach (var song in existingSongs)
            {
                if (!fileSystemSongs.Contains((song.Title, song.Album)))
                {
                    Debug.Log($"[ALBUM_MANAGER] Song deleted from file system: {song.Title} in album {song.Album}");
                    await mongoDBManager.DeleteSongAsync(song.Title, song.Album);
                    deletedSongsCount++;
                }
            }
            
            // Reload UI with updated data if any changes were made
            bool hasChanges = newAlbums.Count > 0 || newSongs.Count > 0 || deletedAlbumsCount > 0 || deletedSongsCount > 0;
            if (hasChanges)
            {
                string changesSummary = $"Added: {newAlbums.Count} albums, {newSongs.Count} songs. Deleted: {deletedAlbumsCount} albums, {deletedSongsCount} songs.";
                UpdateDebugText(changesSummary + " Reloading UI...");
                Debug.Log($"[ALBUM_MANAGER] {changesSummary}");
                await LoadAlbumsFromMongoDB();
            }
            else
            {
                UpdateDebugText("No changes detected. Albums and songs are up to date.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ALBUM_MANAGER] Error scanning for albums: {ex.Message}");
            UpdateDebugText($"Error scanning albums: {ex.Message}");
        }
    }
    public void UpdateAlbumData(string albumJson)
    {
        if (string.IsNullOrEmpty(albumJson))
        {
            UpdateDebugText("UpdateAlbumData received an empty JSON string.");
            return;
        }
        try
        {
            AlbumDataListWrapper myAlbum = JsonUtility.FromJson<AlbumDataListWrapper>(albumJson);

            if (albumDataList == null || albumDataList.Count == 0)
            {
                UpdateDebugText("No albums found in the JSON data.");
                return;
            }
            albumDataList = myAlbum.albums;
            UpdateDebugText(albumDataList.ToString());
            ClearAlbums();
            albums.Clear();
            activeAlbums.Clear();

            int albNum = 0;
            foreach (var albumData in albumDataList)
            {
               // Sprite coverSprite = LoadSpriteFromPath(albumData.CoverPath);
                Album albumInstance = Instantiate(AlbumPrefab, UnseenAlbums);
                albumInstance.Initialize(albumData.AlbumName, albumData.ArtistName, null, albumData.AlbumPath, ++albNum);
                albums.Add(albumInstance);

             
            }

            InitializeAlbums();
            UpdateDebugText("Album data updated successfully.");
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error updating album data: {ex.Message}");
        }
    }

    private void ClearAlbums()
    {
        // Remove all existing albums from UI
        foreach (Transform child in AlbumContainer)
        {
            Destroy(child.gameObject);
        }

        foreach (Transform child in UnseenAlbums)
        {
            Destroy(child.gameObject);
        }
    }


    private void InitializeAlbums()
    {
        for (int i = 0; i < albums.Count; i++)
        {
            if (i < 4)
            {
                albums[i].transform.SetParent(AlbumContainer);
                albums[i].gameObject.SetActive(true);
                activeAlbums.Add(albums[i]);
            }
            else
            {
                albums[i].transform.SetParent(UnseenAlbums);
                albums[i].gameObject.SetActive(false);
            }

            // Songs are already loaded from MongoDB in LoadAlbumsFromMongoDB()
            // No need to load from file system here
        }

        UpdateButtonStates();
    }

    private Sprite LoadSpriteFromPath(string path)
    {
        byte[] imageData = System.IO.File.ReadAllBytes(path);
        Texture2D texture = new Texture2D(2, 2);
        if (texture.LoadImage(imageData))
        {
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }
        return null;
    }

    private void UpdateButtonStates()
    {
        if (PreviousButton != null)
            PreviousButton.interactable = currentAlbumIndex > 0;

        if (NextButton != null)
            NextButton.interactable = currentAlbumIndex + 4 < albums.Count;
    }

    public void SearchSongs()
    {
        string query = SearchInput.text.ToLower();
        foreach (Transform child in SearchResultContainer)
        {
            Destroy(child.gameObject);
        }

        foreach (var album in albums)
        {
            foreach (var song in album.GetSongs())
            {
                if (song.SongName.ToLower().Contains(query))
                {
                    Song result = Instantiate(SearchResultPrefab, SearchResultContainer);
                    result.Initialize(song.SongName, album.artistName, song.AudioClipPath, $"{album.albumNumber:00}-{song.Number:00}");
                }
            }
        }
    }

    public void AddLetter(string letter)
    {
        if (SearchInput != null)
        {
            SearchInput.text += letter;
        }
    }

    public void DeleteLetter()
    {
        if (SearchInput != null && SearchInput.text.Length > 0)
        {
            SearchInput.text = SearchInput.text.Substring(0, SearchInput.text.Length - 1);
        }
    }

    public int GetAudioFileLength(string audioPath)
    {
        try
        {
            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
            {
                Debug.LogWarning($"[ALBUM_MANAGER] Audio file not found: {audioPath}");
                return 0;
            }

            // Use Unity's WWW to get audio file length
            // Note: This is a synchronous method that loads the audio file
            // In a production environment, you might want to use a more efficient method
            using (var www = new WWW("file://" + audioPath))
            {
                while (!www.isDone)
                {
                    // Wait for the file to load
                }

                if (www.error != null)
                {
                    Debug.LogError($"[ALBUM_MANAGER] Error loading audio file {audioPath}: {www.error}");
                    return 0;
                }

                AudioClip clip = www.GetAudioClip();
                if (clip != null)
                {
                    int length = Mathf.RoundToInt(clip.length);
                    Debug.Log($"[ALBUM_MANAGER] Audio file length: {length} seconds for {audioPath}");
                    return length;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ALBUM_MANAGER] Exception getting audio file length for {audioPath}: {ex.Message}");
        }

        return 0;
    }
}
