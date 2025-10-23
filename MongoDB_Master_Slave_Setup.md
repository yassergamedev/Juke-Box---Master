# MongoDB Master-Slave Communication Setup

## Overview
This replaces the TCP-based master-slave communication with MongoDB-based communication where both master and slave applications communicate through a shared MongoDB database.

## Architecture Change

### **Before (TCP-based):**
```
Slave → TCP Connection → Master
- Slave sends: ADD_SONG|DD-DD
- Master responds: SONG_LENGTH|180
- Master sends: PAUSE_RESUME, NEXT_SONG
```

### **After (MongoDB-based):**
```
Slave → MongoDB Tracklist Collection → Master
- Slave adds song to MongoDB tracklist
- Master polls MongoDB for new songs
- Both update song status in MongoDB
```

## Setup Instructions

### **1. MongoDB Atlas Setup**
1. Create MongoDB Atlas account at https://www.mongodb.com/atlas
2. Create a free cluster (M0)
3. Get connection string
4. Update connection string in `MongoDBManager.cs`

### **2. Master Application Setup**

#### A. Add Components to Scene
1. **MongoDBManager** (already exists)
   - Set connection string
   - Set database name to "jukebox"

2. **MongoDBMasterController** (new)
   - Add to empty GameObject
   - Connect UI elements in inspector

3. **Disable TCP Components**
   - Disable or remove `MasterNetworkHandler` component
   - Remove TCP server code

#### B. Update TrackQueueManager
- The `TrackQueueManager` now includes MongoDB integration
- Songs are automatically synced to MongoDB when played

### **3. Slave Application Setup**

#### A. Add Components to Scene
1. **MongoDBManager** (same connection as master)
2. **MongoDBSlaveController** (new)
   - Add to empty GameObject
   - Connect UI elements in inspector

3. **Disable TCP Components**
   - Disable or remove `SlaveController` component
   - Remove TCP client code

### **4. Database Collections**

#### `tracklist` Collection Structure
```json
{
  "_id": "ObjectId",
  "songId": "ObjectId",
  "title": "Song Title",
  "artist": "Artist Name", 
  "album": "Album Name",
  "duration": 180,
  "status": "queued|playing|played|skipped",
  "priority": 1,
  "createdAt": "2024-01-01T00:00:00Z",
  "playedAt": "2024-01-01T00:03:00Z",
  "requestedBy": "slave_abc123",
  "masterId": "master",
  "slaveId": "slave_abc123"
}
```

### **5. Communication Flow**

#### **Adding Songs (Slave → Master)**
1. User enters song/keypad input in slave
2. `MongoDBSlaveController.AddSongToQueue()` called
3. Song added to MongoDB `tracklist` collection with status "queued"
4. Master polls MongoDB and finds new song
5. Master adds song to local queue and plays it

#### **Status Updates (Master → Slave)**
1. Master marks song as "playing" in MongoDB
2. Slave polls MongoDB and sees status change
3. Slave updates local UI accordingly

#### **Control Commands (Slave → Master)**
1. User clicks pause/next/previous in slave
2. Slave updates song status in MongoDB
3. Master polls MongoDB and sees status change
4. Master executes corresponding action

### **6. Key Features**

#### **MongoDBMasterController**
- Polls MongoDB every 1 second for new songs
- Processes queued songs and adds to local queue
- Updates song status when playing/finished
- Provides queue management UI

#### **MongoDBSlaveController**
- Polls MongoDB every 2 seconds for assigned songs
- Adds songs to MongoDB tracklist
- Handles control commands (pause, next, previous)
- Provides song input UI

### **7. Configuration**

#### **Polling Intervals**
- Master: 1 second (faster for responsiveness)
- Slave: 2 seconds (slower to reduce load)

#### **Slave ID Generation**
- Each slave gets unique ID: `slave_abc12345`
- Used to identify which songs belong to which slave

### **8. Migration Steps**

#### **Step 1: Backup Current System**
- Keep TCP components but disable them
- Test MongoDB connection first

#### **Step 2: Add MongoDB Components**
- Add `MongoDBMasterController` to master
- Add `MongoDBSlaveController` to slave
- Configure MongoDB connection

#### **Step 3: Test Communication**
- Add song from slave
- Verify it appears in master queue
- Test play/pause/next functionality

#### **Step 4: Remove TCP Components**
- Once MongoDB system works, remove TCP code
- Clean up unused networking scripts

### **9. Troubleshooting**

#### **Common Issues:**
- **Songs not appearing**: Check MongoDB connection and polling intervals
- **Status not updating**: Verify both apps are updating the same MongoDB collection
- **Performance issues**: Adjust polling intervals or implement MongoDB Change Streams

#### **Debug Information:**
- Check Unity Console for MongoDB operation logs
- Monitor MongoDB Atlas dashboard for collection updates
- Use MongoDB Compass to inspect data directly

### **10. Advantages of MongoDB Approach**

1. **Scalability**: Multiple slaves can connect to same master
2. **Reliability**: No direct network connection required
3. **Persistence**: Queue survives application restarts
4. **Analytics**: All song requests stored in database
5. **Flexibility**: Easy to add new features like user management

## Next Steps

1. Set up MongoDB Atlas account
2. Update connection strings
3. Add new components to both applications
4. Test the communication flow
5. Remove old TCP networking code
