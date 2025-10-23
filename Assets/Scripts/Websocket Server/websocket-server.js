import { WebSocketServer } from 'ws';
import { MongoClient } from 'mongodb';
import express from 'express';
import cors from 'cors';
import { createServer } from 'http';

const app = express();
const server = createServer(app);
const wss = new WebSocketServer({ server });
const mongoUrl1 = "mongodb+srv://mezragyasser2002:mezrag.yasser123...@8bbjukebox.w1btiwn.mongodb.net/";
const mongoUrl = "mongodb+srv://dbuser:dbuser@cluster0.qlpwlae.mongodb.net/?retryWrites=true&w=majority&appName=Cluster0";

let clients = new Set();
let db = null;

// Middleware
app.use(cors());
app.use(express.json());

// Connect to MongoDB
async function connectToMongoDB() {
    try {
        const client = new MongoClient(mongoUrl);
        await client.connect();
        console.log('✅ Connected to MongoDB');
        
        db = client.db('jukebox');
        const tracklist = db.collection('tracklist');
        
        // Watch for changes
        const changeStream = tracklist.watch();
        changeStream.on('change', async (change) => {
            console.log('🔄 MongoDB change detected:', change.operationType);
            
            // Check if this is a new song that needs validation
            if (change.operationType === 'insert' && change.fullDocument?.existsAtMaster === false) {
                console.log('🔍 New song needs validation:', {
                    title: change.fullDocument.title,
                    artist: change.fullDocument.artist,
                    album: change.fullDocument.album,
                    tracklistId: change.fullDocument._id
                });
                
                // Send validation request to master
                const validationMessage = JSON.stringify({
                    type: 'validation_request',
                    tracklistId: change.fullDocument._id,
                    title: change.fullDocument.title || 'Unknown Song',
                    artist: change.fullDocument.artist || 'Unknown Artist',
                    album: change.fullDocument.album || 'Unknown Album',
                    timestamp: new Date().toISOString()
                });
                
                console.log('📤 Sending validation request:', validationMessage);
                
                // Send to master clients only (you can add client type identification later)
                clients.forEach(client => {
                    if (client.readyState === WebSocket.OPEN) {
                        client.send(validationMessage);
                    }
                });
                
                // Don't broadcast the insert yet - wait for validation
                return;
            }
            
            // Broadcast to all WebSocket clients (for validated songs or other operations)
            const message = JSON.stringify({
                operationType: change.operationType,
                songId: change.fullDocument?._id,
                songTitle: change.fullDocument?.title,
                status: change.fullDocument?.status,
                album: change.fullDocument?.album,
                artist: change.fullDocument?.artist,
                existsAtMaster: change.fullDocument?.existsAtMaster,
                length: change.fullDocument?.length,
                priority: change.fullDocument?.priority,
                requestedBy: change.fullDocument?.requestedBy,
                masterId: change.fullDocument?.masterId,
                slaveId: change.fullDocument?.slaveId,
                createdAt: change.fullDocument?.createdAt,
                playedAt: change.fullDocument?.playedAt,
                timestamp: new Date().toISOString()
            });
            
            console.log(`📡 Broadcasting to ${clients.size} clients:`, {
                operationType: change.operationType,
                songTitle: change.fullDocument?.title,
                status: change.fullDocument?.status
            });
            
            clients.forEach(client => {
                if (client.readyState === WebSocket.OPEN) {
                    client.send(message);
                }
            });
        });
        
        changeStream.on('error', (error) => {
            console.error('❌ Change stream error:', error);
        });
        
    } catch (error) {
        console.error('❌ MongoDB connection error:', error);
    }
}

// Handle WebSocket connections
wss.on('connection', (ws) => {
    console.log('🔌 New WebSocket client connected');
    clients.add(ws);
    
    // Send current tracklist to new client
    if (db) {
        db.collection('tracklist').find({}).sort({ priority: 1, createdAt: 1 }).toArray()
            .then(tracklist => {
                const initialData = JSON.stringify({
                    operationType: 'initial',
                    tracklist: tracklist,
                    timestamp: new Date().toISOString()
                });
                ws.send(initialData);
                console.log(`📤 Sent initial tracklist (${tracklist.length} songs) to new client`);
            })
            .catch(error => {
                console.error('❌ Error sending initial data:', error);
            });
    }
    
    ws.on('message', (message) => {
        try {
            const data = JSON.parse(message);
            console.log('📨 Received message from client:', data);
            
            // Handle different message types
            switch (data.type) {
                case 'ping':
                    ws.send(JSON.stringify({ type: 'pong', timestamp: new Date().toISOString() }));
                    break;
                case 'getTracklist':
                    if (db) {
                        db.collection('tracklist').find({}).sort({ priority: 1, createdAt: 1 }).toArray()
                            .then(tracklist => {
                                ws.send(JSON.stringify({
                                    type: 'tracklist',
                                    tracklist: tracklist,
                                    timestamp: new Date().toISOString()
                                }));
                            });
                    }
                    break;
                default:
                    console.log('❓ Unknown message type:', data.type);
            }
        } catch (error) {
            console.error('❌ Error parsing client message:', error);
        }
    });
    
    ws.on('close', () => {
        console.log('🔌 WebSocket client disconnected');
        clients.delete(ws);
    });
    
    ws.on('error', (error) => {
        console.error('❌ WebSocket error:', error);
        clients.delete(ws);
    });
});

// Handle server errors
wss.on('error', (error) => {
    console.error('❌ WebSocket server error:', error);
});

// API Routes

// Add song to tracklist
app.post('/api/tracklist', async (req, res) => {
    try {
        if (!db) {
            return res.status(500).json({ error: 'Database not connected' });
        }

        const tracklistCollection = db.collection('tracklist');
        const newEntry = {
            ...req.body,
            _id: new Date().getTime().toString(),
            createdAt: new Date(),
            playedAt: null
        };

        const result = await tracklistCollection.insertOne(newEntry);
        res.json({ success: true, id: result.insertedId });
    } catch (error) {
        console.error('Error adding to tracklist:', error);
        res.status(500).json({ error: 'Failed to add to tracklist' });
    }
});

// Pause current song
app.post('/api/pause', async (req, res) => {
    console.log('[API] /api/pause called');
    try {
        if (!db) {
            console.log('[API] /api/pause -> DB not connected');
            return res.status(500).json({ error: 'Database not connected' });
        }

        const tracklistCollection = db.collection('tracklist');
        
        // Find currently playing song
        const currentSong = await tracklistCollection.findOne({ status: 'playing' });
        if (!currentSong) {
            console.log('[API] /api/pause -> No song currently playing');
            return res.status(404).json({ error: 'No song currently playing' });
        }

        // Update status to paused
        await tracklistCollection.updateOne(
            { _id: currentSong._id },
            { $set: { status: 'paused', pausedAt: new Date() } }
        );
        console.log('[API] /api/pause -> Paused', { _id: currentSong._id, title: currentSong.title });
        res.json({ success: true, message: 'Song paused' });
    } catch (error) {
        console.error('Error pausing song:', error);
        res.status(500).json({ error: 'Failed to pause song' });
    }
});

// Resume paused song
app.post('/api/resume', async (req, res) => {
    console.log('[API] /api/resume called');
    try {
        if (!db) {
            console.log('[API] /api/resume -> DB not connected');
            return res.status(500).json({ error: 'Database not connected' });
        }

        const tracklistCollection = db.collection('tracklist');
        
        // Find paused song
        const pausedSong = await tracklistCollection.findOne({ status: 'paused' });
        if (!pausedSong) {
            console.log('[API] /api/resume -> No song currently paused');
            return res.status(404).json({ error: 'No song currently paused' });
        }

        // Update status to playing
        await tracklistCollection.updateOne(
            { _id: pausedSong._id },
            { $set: { status: 'playing', resumedAt: new Date() } }
        );
        console.log('[API] /api/resume -> Resumed', { _id: pausedSong._id, title: pausedSong.title });
        res.json({ success: true, message: 'Song resumed' });
    } catch (error) {
        console.error('Error resuming song:', error);
        res.status(500).json({ error: 'Failed to resume song' });
    }
});

// Skip current song
app.post('/api/skip', async (req, res) => {
    console.log('[API] /api/skip called');
    try {
        if (!db) {
            console.log('[API] /api/skip -> DB not connected');
            return res.status(500).json({ error: 'Database not connected' });
        }

        const tracklistCollection = db.collection('tracklist');
        
        // Find currently playing song
        const currentSong = await tracklistCollection.findOne({ status: 'playing' });
        if (!currentSong) {
            console.log('[API] /api/skip -> No song currently playing');
            return res.status(404).json({ error: 'No song currently playing' });
        }

        // Mark current song as skipped
        await tracklistCollection.updateOne(
            { _id: currentSong._id },
            { $set: { status: 'skipped', skippedAt: new Date() } }
        );
        console.log('[API] /api/skip -> Skipped', { _id: currentSong._id, title: currentSong.title });

        // Find next queued song
        const nextSong = await tracklistCollection.findOne(
            { status: 'queued' },
            { sort: { priority: 1, createdAt: 1 } }
        );

        if (nextSong) {
            // Start next song
            await tracklistCollection.updateOne(
                { _id: nextSong._id },
                { $set: { status: 'playing', playedAt: new Date() } }
            );
            console.log('[API] /api/skip -> Started next song', { _id: nextSong._id, title: nextSong.title });
        }

        res.json({ success: true, message: 'Song skipped', nextSong: nextSong?.title || null });
    } catch (error) {
        console.error('Error skipping song:', error);
        res.status(500).json({ error: 'Failed to skip song' });
    }
});

// Update song validation status
app.post('/api/validate', async (req, res) => {
    console.log('[API] /api/validate called');
    try {
        if (!db) {
            console.log('[API] /api/validate -> DB not connected');
            return res.status(500).json({ error: 'Database not connected' });
        }

        const { tracklistId, existsAtMaster, length } = req.body;
        
        if (!tracklistId) {
            return res.status(400).json({ error: 'tracklistId is required' });
        }

        const tracklistCollection = db.collection('tracklist');
        
        // Update the song's validation status
        const updateData = { existsAtMaster: existsAtMaster };
        if (length !== undefined) {
            updateData.length = length;
        }
        
        const result = await tracklistCollection.updateOne(
            { _id: tracklistId },
            { $set: updateData }
        );
        
        if (result.matchedCount === 0) {
            console.log('[API] /api/validate -> Song not found:', tracklistId);
            return res.status(404).json({ error: 'Song not found' });
        }
        
        console.log('[API] /api/validate -> Updated validation status:', {
            tracklistId,
            existsAtMaster,
            length
        });
        
        res.json({ success: true, message: 'Validation status updated' });
    } catch (error) {
        console.error('Error updating validation status:', error);
        res.status(500).json({ error: 'Failed to update validation status' });
    }
});

// Get current status
app.get('/api/status', async (req, res) => {
    try {
        if (!db) {
            return res.status(500).json({ error: 'Database not connected' });
        }

        const tracklistCollection = db.collection('tracklist');
        
        const [currentSong, queuedSongs, playedSongs] = await Promise.all([
            tracklistCollection.findOne({ status: 'playing' }),
            tracklistCollection.find({ status: 'queued' }).sort({ priority: 1, createdAt: 1 }).toArray(),
            tracklistCollection.find({ status: 'played' }).sort({ playedAt: -1 }).limit(5).toArray()
        ]);

        res.json({
            currentSong,
            queuedSongs,
            playedSongs,
            totalQueued: queuedSongs.length,
            totalPlayed: playedSongs.length
        });
    } catch (error) {
        console.error('Error getting status:', error);
        res.status(500).json({ error: 'Failed to get status' });
    }
});

// Start the server
console.log('🚀 Starting combined WebSocket and API server on port 8081...');

// Start server
server.listen(8081, () => {
    console.log('✅ Server running on port 8081');
    console.log('  📡 WebSocket: ws://localhost:8081');
    console.log('  🌐 HTTP API: http://localhost:8081');
});

// Connect to MongoDB
connectToMongoDB();

// Graceful shutdown
process.on('SIGINT', () => {
    console.log('\n🛑 Shutting down WebSocket server...');
    wss.close(() => {
        console.log('✅ WebSocket server closed');
        process.exit(0);
    });
});

console.log('✅ WebSocket server running on port 8080');
