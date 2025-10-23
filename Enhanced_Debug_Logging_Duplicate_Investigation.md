# Enhanced Debug Logging - Duplicate Investigation

## Problem
Songs are still being added continuously despite duplicate prevention logic. The queue count is growing (5 songs) and `isPlaying: True`, indicating songs are being added faster than they're being played/removed.

## Enhanced Debug Logging Added

### **1. MongoDB Master Controller - Detailed Filtering:**

```csharp
// Debug each song being filtered
foreach (var song in queuedSongs)
{
    bool inCurrentQueue = currentQueue.Any(existing => existing.Id == song.Id);
    bool inUnityQueue = IsSongAlreadyInUnityQueue(song);
    Debug.Log($"[MONGODB_MASTER] Song {song.Title}: InCurrentQueue={inCurrentQueue}, InUnityQueue={inUnityQueue}, WillProcess={!inCurrentQueue && !inUnityQueue}");
}
```

### **2. Enhanced Duplicate Checking:**

```csharp
private bool IsSongAlreadyInUnityQueue(TracklistEntryDocument song)
{
    Debug.Log($"[MONGODB_MASTER] Checking Unity queue for duplicate: {song.Title}");
    Debug.Log($"[MONGODB_MASTER] Unity queue has {trackQueueManager.queueList.Count} songs");
    
    // List all songs in Unity queue for debugging
    for (int i = 0; i < trackQueueManager.queueList.Count; i++)
    {
        var queueItem = trackQueueManager.queueList[i];
        Debug.Log($"[MONGODB_MASTER] Unity queue[{i}]: {queueItem.Item1.SongName}");
    }
    
    bool isDuplicate = trackQueueManager.queueList.Any(queueItem => 
        queueItem.Item1.SongName.Equals(song.Title, StringComparison.OrdinalIgnoreCase));
    
    Debug.Log($"[MONGODB_MASTER] Duplicate check result for '{song.Title}': {isDuplicate}");
    return isDuplicate;
}
```

### **3. TrackQueueManager - Queue Contents Tracking:**

```csharp
// Debug: List all songs in queue after adding
Debug.Log($"[TRACKQUEUE] Current queue contents:");
for (int i = 0; i < queueList.Count; i++)
{
    Debug.Log($"[TRACKQUEUE]   [{i}] {queueList[i].Item1.SongName}");
}
```

### **4. AddSongToUnityQueueFromMongoDB - Before/After Tracking:**

```csharp
Debug.Log($"[TRACKQUEUE] Adding song to Unity queue: {selectedSong.SongName}");
Debug.Log($"[TRACKQUEUE] Queue count before adding: {queueList.Count}");
StartCoroutine(AddSongToQueueByName(selectedSong.SongName, 180, false));
```

## What You'll See Now

### **MongoDB Polling Cycle:**
```
[MONGODB_MASTER] Processing new songs...
[MONGODB_MASTER] Found 3 queued songs in MongoDB
[MONGODB_MASTER] After filtering: 2 new songs to process
[MONGODB_MASTER] Current queue size: 1
[MONGODB_MASTER] Unity queue size: 3
[MONGODB_MASTER] Song 01-01: InCurrentQueue=True, InUnityQueue=True, WillProcess=False
[MONGODB_MASTER] Song 02-01: InCurrentQueue=False, InUnityQueue=False, WillProcess=True
[MONGODB_MASTER] Song 03-01: InCurrentQueue=False, InUnityQueue=False, WillProcess=True
```

### **Duplicate Checking Details:**
```
[MONGODB_MASTER] Checking Unity queue for duplicate: 02-01
[MONGODB_MASTER] Unity queue has 3 songs
[MONGODB_MASTER] Unity queue[0]: Song Name 1
[MONGODB_MASTER] Unity queue[1]: Song Name 2
[MONGODB_MASTER] Unity queue[2]: Song Name 3
[MONGODB_MASTER] Duplicate check result for '02-01': False
```

### **Song Addition Process:**
```
[TRACKQUEUE] Adding song to Unity queue: Song Name
[TRACKQUEUE] Queue count before adding: 3
[TRACKQUEUE] Creating song instance: Song Name
[TRACKQUEUE] Added song to queue list: Song Name (GameObject: Song(Clone))
[TRACKQUEUE] Total songs in queue: 4
[TRACKQUEUE] Current queue contents:
[TRACKQUEUE]   [0] Song Name 1
[TRACKQUEUE]   [1] Song Name 2
[TRACKQUEUE]   [2] Song Name 3
[TRACKQUEUE]   [3] Song Name
```

## What to Look For

### **1. Duplicate Prevention Issues:**
- Are songs being marked as `InUnityQueue=False` when they should be `True`?
- Are the song names matching correctly in the duplicate check?
- Are songs being added to the queue multiple times?

### **2. Queue Growth:**
- Is the queue count growing continuously?
- Are songs being removed as they finish playing?
- Are there multiple instances of the same song?

### **3. MongoDB Polling:**
- How often is the polling happening?
- Are the same songs being processed repeatedly?
- Is the filtering logic working correctly?

## Expected Debug Output

The enhanced logging will show you:
- **Exactly which songs** are being processed
- **Why duplicates are or aren't being detected**
- **The complete queue contents** at each step
- **The filtering logic** for each song
- **The sequence of events** leading to continuous additions

This will help identify whether the issue is:
- **Duplicate detection not working** (songs not being recognized as duplicates)
- **Songs being added multiple times** (same song added repeatedly)
- **Different songs being added** (new songs being added continuously)
- **Queue not being cleared** (songs not being removed after playing)

The enhanced debug logging will reveal exactly what's causing the continuous song additions!
