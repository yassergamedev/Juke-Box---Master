using UnityEngine;

public class ScreenSwitcher : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject hubObject;        // The Hub UI container
    public GameObject jukeboxObject;    // The Jukebox UI container (always stays active)
    public RectTransform queueContainer; // The queue container that changes size/position

    [Header("Queue Transform Settings")]
    public Vector2 hubModePosition;      // Position of queue in Hub Mode
    public Vector2 hubModeSize;          // Size of queue in Hub Mode
    public Vector2 jukeboxModePosition;  // Position of queue in Jukebox Mode
    public Vector2 jukeboxModeSize;      // Size of queue in Jukebox Mode

    // Call this function when Hub Button is clicked
    public void ActivateHubMode()
    {
        hubObject.SetActive(true); // Activate the Hub

    }

    // Call this function when Jukebox Button is clicked
    public void ActivateJukeboxMode()
    {
        hubObject.SetActive(false); // Deactivate only the Hub

    }

    // Helper function to update the queue container's size and position
    private void UpdateQueueContainer(Vector2 newPosition, Vector2 newSize)
    {
        queueContainer.anchoredPosition = newPosition; // Set position
        queueContainer.sizeDelta = newSize;           // Set size
    }
}
