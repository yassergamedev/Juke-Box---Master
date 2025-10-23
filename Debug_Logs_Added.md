# Debug Logs Added - Complete Song Addition Tracking

## Overview
Added comprehensive debug logging throughout the MongoDB-based song addition system to track exactly what's happening and help identify duplicate issues.

## Debug Log Categories

### **1. TrackQueueManager Debug Logs**

#### **AddSongToQueue Method:**
- `[TRACKQUEUE] AddSongToQueue called - Input: {keypadInput}, RequestedBy: {requestedBy}`
- `[TRACKQUEUE] Invalid input format: {keypadInput}. Expected: DD-DD`
- `[TRACKQUEUE] Parsed - Album: {albumIndex}, Song: {songIndex}`
- `[TRACKQUEUE] Selected album: {album.albumName}`
- `[TRACKQUEUE] Selected song: {selectedSong.SongName}`

#### **AddSongToUnityQueueFromMongoDB Method:**
- `[TRACKQUEUE] AddSongToUnityQueueFromMongoDB called - Input: {keypadInput}, RequestedBy: {requestedBy}`
- `[TRACKQUEUE] Adding song to Unity queue: {selectedSong.SongName}`
- `[TRACKQUEUE] Successfully added {selectedSong.SongName} to Unity queue from MongoDB`

#### **AddSongToQueueByName Method:**
- `[TRACKQUEUE] AddSongToQueueByName called - Song: {songFileName}, Length: {length}, FromSlave: {isFromSlave}`
- `[TRACKQUEUE] Found song file: {fullPath}`
- `[TRACKQUEUE] Creating song instance: {songName}`
- `[TRACKQUEUE] Added song to queue list. Total songs in queue: {queueList.Count}`

### **2. MongoDBMasterController Debug Logs**

#### **ProcessNewSongs Method:**
- `[MONGODB_MASTER] Processing new songs...`
- `[MONGODB_MASTER] Found {count} queued songs in MongoDB`
- `[MONGODB_MASTER] After filtering: {count} new songs to process`
- `[MONGODB_MASTER] Current queue size: {currentQueue.Count}`
- `[MONGODB_MASTER] Unity queue size: {trackQueueManager.queueList.Count}`

#### **ProcessNewSong Method:**
- `[MONGODB_MASTER] Processing song: {song.Title} by {song.Artist}`
- `[MONGODB_MASTER] Song verified in master albums: {song.Title}`
- `[MONGODB_MASTER] Adding keypad input to Unity queue: {song.Title}`
- `[MONGODB_MASTER] Added to tracking queue. Total tracked: {currentQueue.Count}`

#### **Duplicate Checking:**
- `[MONGODB_MASTER] Checking Unity queue for duplicate: {song.Title} - Found: {isDuplicate}`

### **3. MongoDBSlaveController Debug Logs**

#### **CheckForCommands Method:**
- `[MONGODB_SLAVE_{slaveId}] Checking for commands...`
- `[MONGODB_SLAVE_{slaveId}] Found {count} queued songs in MongoDB`
- `[MONGODB_SLAVE_{slaveId}] After filtering: {count} new songs to process`
- `[MONGODB_SLAVE_{slaveId}] Unity queue size: {trackQueueManager.queueList.Count}`

#### **ProcessAssignedSong Method:**
- `[MONGODB_SLAVE_{slaveId}] Processing assigned song: {song.Title} by {song.Artist}`
- `[MONGODB_SLAVE_{slaveId}] Updated song status in MongoDB: {song.Title}`
- `[MONGODB_SLAVE_{slaveId}] Adding keypad input to Unity queue: {song.Title}`
- `[MONGODB_SLAVE_{slaveId}] Successfully added song to queue: {song.Title}`

#### **Duplicate Checking:**
- `[MONGODB_SLAVE_{slaveId}] Checking Unity queue for duplicate: {song.Title} - Found: {isDuplicate}`

## What You'll See in Unity Console

### **Normal Flow (No Duplicates):**
```
[MONGODB_MASTER] Processing new songs...
[MONGODB_MASTER] Found 1 queued songs in MongoDB
[MONGODB_MASTER] After filtering: 1 new songs to process
[MONGODB_MASTER] Current queue size: 0
[MONGODB_MASTER] Unity queue size: 0
[MONGODB_MASTER] Processing song: 01-01 (ID: 507f1f77bcf86cd799439011)
[MONGODB_MASTER] Processing song: 01-01 by Unknown Artist
[MONGODB_MASTER] Song verified in master albums: 01-01
[MONGODB_MASTER] Adding keypad input to Unity queue: 01-01
[TRACKQUEUE] AddSongToUnityQueueFromMongoDB called - Input: 01-01, RequestedBy: master
[TRACKQUEUE] Parsed - Album: 1, Song: 1
[TRACKQUEUE] Selected album: Album Name
[TRACKQUEUE] Selected song: Song Name
[TRACKQUEUE] Adding song to Unity queue: Song Name
[TRACKQUEUE] AddSongToQueueByName called - Song: Song Name, Length: 180, FromSlave: False
[TRACKQUEUE] Found song file: C:\Path\To\Song.mp3
[TRACKQUEUE] Creating song instance: Song Name
[TRACKQUEUE] Added song to queue list. Total songs in queue: 1
[MONGODB_MASTER] Added to tracking queue. Total tracked: 1
```

### **Duplicate Prevention:**
```
[MONGODB_MASTER] Processing new songs...
[MONGODB_MASTER] Found 1 queued songs in MongoDB
[MONGODB_MASTER] After filtering: 0 new songs to process
[MONGODB_MASTER] Current queue size: 1
[MONGODB_MASTER] Unity queue size: 1
[MONGODB_MASTER] Checking Unity queue for duplicate: 01-01 - Found: True
```

## How to Use These Logs

### **1. Check for Duplicates:**
Look for `Checking Unity queue for duplicate` logs - if you see `Found: True`, the duplicate prevention is working.

### **2. Track Song Flow:**
Follow the logs from `Processing new songs` through `Added song to queue list` to see the complete flow.

### **3. Identify Issues:**
- **No songs processed**: Check if songs are being found in MongoDB
- **Songs not added to Unity**: Check if song verification is failing
- **Duplicates still occurring**: Check if duplicate checking is working properly

### **4. Monitor Queue Sizes:**
Watch the `Current queue size` and `Unity queue size` to ensure they're in sync.

## Benefits

- **Complete visibility** into the song addition process
- **Easy identification** of where duplicates might be occurring
- **Clear tracking** of queue states and filtering
- **Detailed error messages** for troubleshooting
- **Performance monitoring** with queue size tracking

The debug logs will help you see exactly what's happening and identify any remaining duplicate issues!
