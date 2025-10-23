# MongoDB Integration Setup Instructions

## Overview
This guide will help you implement the MongoDB system in your Juke Box application for persistent data storage and multi-device synchronization.

## Prerequisites
- MongoDB Atlas account (cloud database)
- Unity 2022.3.51f1 or later
- Internet connection for cloud database access

## Setup Steps

### 1. MongoDB Atlas Configuration
1. **Create MongoDB Atlas Account**: Go to https://www.mongodb.com/atlas
2. **Create a Cluster**: Choose the free tier (M0)
3. **Get Connection String**: 
   - Go to "Database Access" → "Connect" → "Connect your application"
   - Copy the connection string
   - Replace `<password>` with your actual password

### 2. Unity Project Setup

#### A. Add MongoDB Components to Scene
1. **MongoDBManager**: 
   - Create empty GameObject named "MongoDBManager"
   - Add `MongoDBManager.cs` script
   - Set connection string in inspector

2. **MongoDBIntegration**:
   - Create empty GameObject named "MongoDBIntegration" 
   - Add `MongoDBIntegration.cs` script

3. **MongoDBUI** (Optional):
   - Create UI Canvas for MongoDB controls
   - Add `MongoDBUI.cs` script
   - Connect UI elements in inspector

#### B. Update Connection String
1. Open `MongoDBManager.cs` in inspector
2. Replace the connection string with your MongoDB Atlas connection string
3. Ensure database name is set to "jukebox"

### 3. Integration with Existing Systems

#### A. AlbumManager Integration
The `MongoDBIntegration` script automatically syncs albums when:
- Application starts (if `autoSyncOnStart` is enabled)
- Albums are added or modified

#### B. TrackQueueManager Integration
The tracklist is automatically synced to MongoDB when:
- Songs are added to queue
- Songs start playing
- Songs finish playing
- Songs are skipped

### 4. Database Collections

The system creates three collections:

#### `albums` Collection
```json
{
  "_id": "ObjectId",
  "title": "Album Name"
}
```

#### `songs` Collection
```json
{
  "_id": "ObjectId", 
  "title": "Song Title",
  "album": "Album Name",
  "familyFriendly": true
}
```

#### `tracklist` Collection
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
  "requestedBy": "User",
  "masterId": "master",
  "slaveId": "slave1"
}
```

### 5. Usage Examples

#### Sync Albums to Database
```csharp
// In your script
MongoDBIntegration mongoDB = FindObjectOfType<MongoDBIntegration>();
await mongoDB.SyncAlbumsToMongoDB();
```

#### Add Song to Tracklist
```csharp
await mongoDB.AddSongToTracklist(songId, title, artist, album, duration, "User");
```

#### Search Songs
```csharp
List<SongDocument> results = await mongoDB.SearchSongs("search query");
```

#### Get Play History
```csharp
List<TracklistEntryDocument> history = await mongoDB.GetPlayHistory();
```

### 6. Security Considerations

#### A. Connection String Security
- **Never commit connection strings to version control**
- Use environment variables or Unity's PlayerPrefs for production
- Consider using MongoDB's IP whitelist feature

#### B. Data Validation
- All user inputs should be validated before database operations
- Implement proper error handling for network failures

### 7. Testing

#### A. Test Database Connection
1. Run the application
2. Check Unity Console for "MongoDB connection initialized successfully"
3. Use MongoDB UI to sync albums

#### B. Test Data Sync
1. Add some albums to your local folder
2. Click "Sync Albums" in MongoDB UI
3. Check MongoDB Atlas dashboard to verify data

### 8. Troubleshooting

#### Common Issues:

**Connection Failed**
- Check internet connection
- Verify connection string is correct
- Ensure MongoDB Atlas cluster is running

**Data Not Syncing**
- Check Unity Console for error messages
- Verify MongoDBIntegration component is attached
- Ensure AlbumManager is properly initialized

**Performance Issues**
- Consider implementing data pagination for large datasets
- Use async/await properly to avoid blocking main thread
- Monitor MongoDB Atlas usage limits

### 9. Advanced Features

#### A. Real-time Updates
Consider implementing MongoDB Change Streams for real-time synchronization between multiple devices.

#### B. Data Backup
Set up regular backups in MongoDB Atlas dashboard.

#### C. Analytics
Use the tracklist data to generate play statistics and user analytics.

## Support

For issues with:
- **MongoDB**: Check MongoDB Atlas documentation
- **Unity Integration**: Review Unity async/await documentation
- **This Implementation**: Check Unity Console for specific error messages
