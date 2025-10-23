# Debug Logs Added - Startup and Initialization Tracking

## Problem Identified
The debug logs weren't showing because we needed to check if the MongoDB controllers are even initializing and running. I've added comprehensive startup debug logs to track the entire initialization process.

## Debug Logs Added

### **1. TrackQueueManager Startup:**
```
[TRACKQUEUE] Starting TrackQueueManager...
[TRACKQUEUE] TrackQueueManager initialized successfully
```

### **2. MongoDBMasterController Startup:**
```
[MONGODB_MASTER] Starting MongoDB Master Controller...
[MONGODB_MASTER] MongoDBManager found: True/False
[MONGODB_MASTER] AlbumManager found: True/False
[MONGODB_MASTER] TrackQueueManager found: True/False
[MONGODB_MASTER] Setting up UI...
[MONGODB_MASTER] Starting polling...
[MONGODB_MASTER] Polling coroutine started successfully
[MONGODB_MASTER] Master initialized successfully and connected to MongoDB
```

### **3. MongoDBSlaveController Startup:**
```
[MONGODB_SLAVE_{slaveId}] Starting MongoDB Slave Controller...
[MONGODB_SLAVE_{slaveId}] MongoDBManager found: True/False
[MONGODB_SLAVE_{slaveId}] AlbumManager found: True/False
[MONGODB_SLAVE_{slaveId}] TrackQueueManager found: True/False
[MONGODB_SLAVE_{slaveId}] Setting up UI...
[MONGODB_SLAVE_{slaveId}] Starting polling...
[MONGODB_SLAVE_{slaveId}] Polling coroutine started successfully
[MONGODB_SLAVE_{slaveId}] Slave initialized successfully and connected to MongoDB
```

### **4. Polling Debug Logs:**
```
[MONGODB_MASTER] PollForNewSongs coroutine started
[MONGODB_MASTER] Waiting 1 seconds before next poll...
[MONGODB_MASTER] Polling interval reached, processing new songs...

[MONGODB_SLAVE_{slaveId}] PollForCommands coroutine started
[MONGODB_SLAVE_{slaveId}] Waiting 2 seconds before next poll...
[MONGODB_SLAVE_{slaveId}] Polling interval reached, checking for commands...
```

### **5. KeypadScript Debug Logs:**
```
[KEYPAD] Valid input: 01-01
[KEYPAD] Adding song to MongoDB tracklist: 01-01
[KEYPAD] Song added to MongoDB tracklist: 01-01
```

## What to Look For

### **1. Controller Initialization:**
- Check if you see the startup logs for all controllers
- Look for any "not found" errors that would prevent initialization

### **2. Polling Activity:**
- Master should show polling every 1 second
- Slave should show polling every 2 seconds
- If you don't see these, the controllers aren't running

### **3. Song Addition Flow:**
- When you add a song via keypad, you should see:
  - `[KEYPAD]` logs showing the input
  - `[TRACKQUEUE]` logs showing the MongoDB addition
  - `[MONGODB_MASTER]` logs showing the polling and processing

## Expected Debug Output

### **On Scene Start:**
```
[TRACKQUEUE] Starting TrackQueueManager...
[TRACKQUEUE] TrackQueueManager initialized successfully
[MONGODB_MASTER] Starting MongoDB Master Controller...
[MONGODB_MASTER] MongoDBManager found: True
[MONGODB_MASTER] AlbumManager found: True
[MONGODB_MASTER] TrackQueueManager found: True
[MONGODB_MASTER] Setting up UI...
[MONGODB_MASTER] Starting polling...
[MONGODB_MASTER] Polling coroutine started successfully
[MONGODB_MASTER] Master initialized successfully and connected to MongoDB
[MONGODB_MASTER] PollForNewSongs coroutine started
[MONGODB_MASTER] Waiting 1 seconds before next poll...
```

### **When Adding a Song:**
```
[KEYPAD] Valid input: 01-01
[KEYPAD] Adding song to MongoDB tracklist: 01-01
[TRACKQUEUE] AddSongToQueue called - Input: 01-01, RequestedBy: user
[KEYPAD] Song added to MongoDB tracklist: 01-01
[MONGODB_MASTER] Polling interval reached, processing new songs...
[MONGODB_MASTER] Processing new songs...
[MONGODB_MASTER] Found 1 queued songs in MongoDB
[MONGODB_MASTER] After filtering: 1 new songs to process
[MONGODB_MASTER] Processing song: 01-01 (ID: 507f1f77bcf86cd799439011)
[TRACKQUEUE] AddSongToUnityQueueFromMongoDB called - Input: 01-01, RequestedBy: master
[TRACKQUEUE] Added song to queue list. Total songs in queue: 1
```

## Troubleshooting

### **If No Logs Appear:**
1. **Check if controllers are in the scene** - Look for the startup logs
2. **Check for missing dependencies** - Look for "not found" errors
3. **Check if MongoDB is connected** - Look for MongoDB connection errors

### **If Songs Keep Adding:**
1. **Check the polling logs** - Are they running every 1-2 seconds?
2. **Check the filtering logs** - Are duplicates being filtered out?
3. **Check the Unity queue size** - Is it growing continuously?

The debug logs will now show you exactly what's happening at every step!
