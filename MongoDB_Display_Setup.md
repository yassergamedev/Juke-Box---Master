# MongoDB Data Display Setup Guide

## Overview
This guide shows how to display albums and songs from your MongoDB database using the existing Unity prefabs.

## Current Database Structure
Based on your MongoDB collections:

### Albums Collection (32 documents)
- `_id`: ObjectId
- `title`: Album name

### Songs Collection (460 documents)  
- `_id`: ObjectId
- `title`: Song title
- `album`: Album name (links to albums collection)
- `familyFriendly`: Boolean

### Tracklist Collection (10 documents)
- `_id`: ObjectId
- `songId`: ObjectId (references songs)
- `title`: Song title
- `artist`: Artist name
- `album`: Album name
- `duration`: Song length in seconds
- `status`: "queued|playing|played|skipped"
- `priority`: Priority number
- `createdAt`: Timestamp
- `playedAt`: Timestamp (when played)
- `requestedBy`: Who requested the song
- `masterId`: Master identifier
- `slaveId`: Slave identifier

## Setup Instructions

### 1. AlbumManager Already Configured
The `AlbumManager.cs` has been updated to load from MongoDB:
- ✅ Fetches albums from MongoDB
- ✅ Fetches songs from MongoDB  
- ✅ Groups songs by album
- ✅ Creates UI using existing prefabs
- ✅ Handles null album covers

### 2. Test Scripts Created

#### A. MongoDBDataTester.cs
- Tests MongoDB connection
- Shows collection counts
- Displays sample data
- Helps verify data loading

### 3. Scene Setup

#### For Master Application:
1. **Add MongoDBManager** to scene
   - Set connection string
   - Set database name to "jukebox"

2. **Add MongoDBMasterController** to scene
   - Connect UI elements
   - Set poll interval (default: 1 second)

3. **Add MongoDBDataTester** (optional, for testing)
   - Connect UI text elements
   - Use to verify data loading

#### For Slave Application:
1. **Add MongoDBManager** to scene (same connection as master)
2. **Add MongoDBSlaveController** to scene
   - Connect UI elements
   - Set poll interval (default: 2 seconds)

### 4. UI Configuration

#### AlbumManager UI Elements:
- `AlbumContainer`: Where albums are displayed
- `UnseenAlbums`: Hidden container for inactive albums
- `AlbumPrefab`: Prefab for album display
- `SongPrefab`: Prefab for song display
- `NextButton`/`PreviousButton`: Navigation buttons
- `SearchInput`: Search functionality

#### MongoDBDataTester UI Elements:
- `debugText`: Status messages
- `albumCountText`: Album count display
- `songCountText`: Song count display
- `tracklistCountText`: Tracklist count display

#### MongoDBAlbumDisplay UI Elements:
- `albumContainer`: Container for album display
- `albumPrefab`: Album prefab
- `songPrefab`: Song prefab
- `statusText`: Status messages
- `refreshButton`: Refresh button

### 5. Data Flow

#### Loading Process:
1. **Start()** → `LoadAlbumsFromMongoDB()`
2. **Fetch Albums** → `GetAllAlbumsAsync()`
3. **Fetch Songs** → `GetAllSongsAsync()`
4. **Group Songs** → Group by album title
5. **Create UI** → Instantiate prefabs
6. **Display** → Show in UI containers

#### Song Display:
- **Album Title**: From `albums.title`
- **Song Title**: From `songs.title`
- **Artist**: Extracted from song title (format: "Artist - Song Title")
- **Cover**: Set to null (as requested)

### 6. Testing

#### Test Data Loading:
1. Run the application
2. Check Unity Console for MongoDB connection messages
3. Verify albums and songs appear in UI
4. Use MongoDBDataTester to verify counts

#### Test Song Interaction:
1. Click on songs in album tracklists
2. Verify songs can be added to tracklist
3. Check MongoDB tracklist collection for new entries

### 7. Customization

#### Album Cover Handling:
Currently set to null. To add covers later:
1. Add cover image field to MongoDB
2. Update `AlbumDocument` model
3. Load and display cover images

#### Song Artist Extraction:
Currently extracts from song title format "Artist - Song Title".
To change this:
1. Modify the artist extraction logic in `LoadAlbumsFromMongoDB()`
2. Or add separate artist field to songs collection

#### Display Layout:
- Uses existing prefabs
- Maintains current UI structure
- Supports pagination (4 albums at a time)
- Includes search functionality

### 8. Troubleshooting

#### Common Issues:
- **No albums showing**: Check MongoDB connection and data
- **Songs not grouped**: Verify album titles match between collections
- **UI not updating**: Check prefab assignments and container references

#### Debug Steps:
1. Use MongoDBDataTester to verify data loading
2. Check Unity Console for error messages
3. Verify MongoDB connection string
4. Check collection names and field names

### 9. Next Steps

1. **Test the setup** with your existing data
2. **Customize the display** as needed
3. **Add album covers** when ready
4. **Test master-slave communication** with real data
5. **Optimize performance** if needed

## Summary

Your MongoDB data is now ready to be displayed using the existing Unity prefabs. The system will:
- Load 32 albums from MongoDB
- Display 460 songs grouped by album
- Use existing UI components and prefabs
- Support all current functionality (search, navigation, etc.)
- Handle null album covers gracefully

The master and slave applications can now work with real data from your MongoDB database!
