using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Album : MonoBehaviour
{
    public TMP_Text AlbumTitleText; // Assign in Unity Editor
    public TMP_Text ArtistNameText; // Assign in Unity Editor
    public Image AlbumCoverImage;   // Assign in Unity Editor for the UI image
    public TMP_Text AlbumNumberText; // Assign in Unity Editor for the album number
    public Transform Tracklist;     // Assign in Unity Editor (parent for song GameObjects)
    public GridLayoutGroup TracklistLayout;  // Assign in Unity Editor for GridLayoutGroup
    public float defaultCellWidth;  // Default cell width
    public float defaultCellHeight;  // Default cell height
    public float minCellSize = 50f;        // Minimum allowed cell size for 
    public string albumName;
    public string artistName;
    public Sprite albumCover;
    public int albumNumber;
    public string Path;
    public List<Song> Songs { get; private set; }

    // New variable for controlling animation speed
    public float animationSpeed = 1f;  // Default speed of 1 (normal speed)

    private Animator albumAnimator; // Reference to the Animator component

    public void Initialize(string albumName, string artistName, Sprite albumCover, string Path, int albumNumber)
    {
        this.albumName = albumName;
        this.artistName = artistName;
        this.albumCover = albumCover;
        this.albumNumber = albumNumber;
        this.Path = Path;
        UpdateUI();

        Songs = new List<Song>();
        defaultCellWidth = TracklistLayout.cellSize.x;
        defaultCellHeight = TracklistLayout.cellSize.y;
        UpdateTracklistSize();  // Initial size adjustment based on empty tracklist

        // Get the Animator component (if attached)
        albumAnimator = GetComponent<Animator>();
        if (albumAnimator != null)
        {
            SetAnimationSpeed(animationSpeed);  // Set the initial speed
        }
    }

    public List<Song> GetSongs()
    {
        return Songs;
    }

    private void UpdateUI()
    {
        if (AlbumTitleText != null) AlbumTitleText.text = albumName;
        if (ArtistNameText != null) ArtistNameText.text = artistName;
        if (AlbumCoverImage != null) AlbumCoverImage.sprite = albumCover;
        if (AlbumNumberText != null) AlbumNumberText.text = $"#{albumNumber}";
    }

    public void AddSong(Song songPrefab, string songName, string artist, string audioPath, int number)
    {
        Song songInstance = Instantiate(songPrefab, Tracklist);
        songInstance.Initialize(songName, artist, audioPath, number);

        Songs.Add(songInstance);

        UpdateTracklistSize();  // Adjust cell size after adding a song
    }

    public void RemoveSong(Song song)
    {
        Songs.Remove(song);

        if (song != null)
        {
            Destroy(song.gameObject);
        }

        UpdateTracklistSize();  // Adjust cell size after removing a song
    }

    public Song GetSongByName(string name)
    {
        return Songs.Find(song => song.SongName == name);
    }

    private void UpdateTracklistSize()
    {
        if (TracklistLayout != null)
        {
            RectTransform tracklistRect = Tracklist.GetComponent<RectTransform>();
            float availableHeight = tracklistRect.rect.height;
            float availableWidth = tracklistRect.rect.width;

            int numSongs = Songs.Count;

            float tracklistSurfaceArea = availableHeight * availableWidth;

            float cellSurfaceArea = TracklistLayout.cellSize.x * TracklistLayout.cellSize.y;

            int maxCells = Mathf.FloorToInt(tracklistSurfaceArea / cellSurfaceArea);

            if (numSongs <= maxCells)
            {
                // Do nothing if songs fit within the available cells
            }
            else
            {
                // Keep the current cell width constant
                float newCellHeight = tracklistSurfaceArea / (numSongs * TracklistLayout.cellSize.x);  // Only adjust height based on available area and song count

                // Set the new cell height but keep the width constant
                TracklistLayout.cellSize = new Vector2(TracklistLayout.cellSize.x, newCellHeight);
                // Adjust song scale
                foreach (Transform song in Tracklist)
                {
                    if (song != null)
                    {
                        song.localScale = new Vector3(song.localScale.x - 0.1f, song.localScale.y - 0.1f, 1);  // Only scale on the y-axis
                    }
                }
            }
        }
    }

    // Method to adjust the Animator speed
    public void SetAnimationSpeed(float speed)
    {
        if (albumAnimator != null)
        {
            albumAnimator.speed = speed;
        }
    }
}
