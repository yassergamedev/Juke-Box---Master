# Enhanced Debug Logging - Queue Tracking

## Problem
The cleanup method was showing "Clearing 0 songs from queue" even though there should have been songs in the queue. This suggests songs were being removed somewhere else before cleanup was called.

## Enhanced Debug Logging Added

### **1. Detailed ClearSongQueue Logging:**
```csharp
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
```

### **2. Song Addition Tracking:**
```csharp
Debug.Log($"[TRACKQUEUE] Added song to queue list: {songName} (GameObject: {songInstance.gameObject.name})");
Debug.Log($"[TRACKQUEUE] Total songs in queue: {queueList.Count}");
```

### **3. Song Removal Tracking:**
```csharp
// When removing due to NULL AudioClip
Debug.Log($"[TRACKQUEUE] Removing song due to NULL AudioClip: {songInstance.SongName}");
Debug.Log($"[TRACKQUEUE] Queue count after removal: {queueList.Count}");

// When song finishes playing
Debug.Log($"[TRACKQUEUE] Song finished playing, removing: {queueList[currentSongIndex].Item1.SongName}");
Debug.Log($"[TRACKQUEUE] Queue count after song finished: {queueList.Count}");
```

### **4. Update Method Queue Monitoring:**
```csharp
private void Update()
{
    // Debug log to track queue count changes
    if (queueList.Count > 0)
    {
        Debug.Log($"[TRACKQUEUE] Update - Queue count: {queueList.Count}, isPlaying: {isPlaying}, isPaused: {isPaused}");
    }
    // ... rest of Update method
}
```

## What You'll See Now

### **When Adding Songs:**
```
[TRACKQUEUE] Creating song instance: Song Name
[TRACKQUEUE] Added song to queue list: Song Name (GameObject: Song(Clone))
[TRACKQUEUE] Total songs in queue: 1
[TRACKQUEUE] Update - Queue count: 1, isPlaying: False, isPaused: False
```

### **When Songs Are Removed:**
```
[TRACKQUEUE] Song finished playing, removing: Song Name
[TRACKQUEUE] Queue count after song finished: 0
[TRACKQUEUE] Update - Queue count: 0, isPlaying: False, isPaused: False
```

### **When Cleanup Is Called:**
```
[TRACKQUEUE] ClearSongQueue called - Current queue count: 0
[TRACKQUEUE] Queue is already empty - nothing to clear
```

## Possible Causes for Empty Queue

### **1. Songs Finished Playing:**
- Songs might have finished playing and been removed before cleanup
- Check for "Song finished playing, removing" logs

### **2. AudioClip Issues:**
- Songs might have been removed due to NULL AudioClip
- Check for "Removing song due to NULL AudioClip" logs

### **3. Normal Playback:**
- Songs might have been played and removed during normal operation
- Check the Update logs to see queue count changes

### **4. Timing Issues:**
- Cleanup might be called after songs were already removed
- Check the sequence of logs to see when songs were removed

## How to Use These Logs

1. **Add a song** and watch the logs to see it being added
2. **Let it play** and watch for removal logs
3. **Stop the game** and see if cleanup finds any songs
4. **Check the sequence** to understand when songs were removed

The enhanced logging will show you exactly when and why songs are being removed from the queue!
