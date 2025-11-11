import { WebSocketServer } from 'ws';
import { MongoClient, ObjectId } from 'mongodb';
import express from 'express';
import cors from 'cors';
import { createServer } from 'http';

const app = express();
const server = createServer(app);
const wss = new WebSocketServer({ server });
//const mongoUrl = "mongodb+srv://mezragyasser2002:mezrag.yasser123...@8bbjukebox.w1btiwn.mongodb.net/";
const mongoUrl = "mongodb+srv://8bbjukebox:8bbjukebox123...@8bbjukebox.w1btiwn.mongodb.net/?retryWrites=true&w=majority&appName=8bbJukebox";
let clients = new Set();
let db = null;
let changeStream = null;

// Middleware
app.use(cors());
app.use(express.json());

// Connect to MongoDB and start monitoring
async function connectToMongoDB() {
    try {
        const client = new MongoClient(mongoUrl);
        await client.connect();
        console.log('✅ Connected to MongoDB for tracklist monitoring');
        
        db = client.db('jukebox');
        const tracklistCollection = db.collection('tracklist');
        
        // Start change stream monitoring
        console.log('🔄 Starting tracklist change stream monitoring...');
        changeStream = tracklistCollection.watch();
        
        changeStream.on('change', async (change) => {
            console.log('📡 Tracklist change detected:', {
                operationType: change.operationType,
                documentId: change.documentKey?._id,
                timestamp: new Date().toISOString()
            });
            
            let documentData = change.fullDocument;
            
            // For update operations, fetch the full document if not provided
            if (change.operationType === 'update' && !documentData) {
                try {
                    documentData = await tracklistCollection.findOne({ _id: change.documentKey._id });
                    console.log('📄 Fetched full document for update:', {
                        title: documentData?.title,
                        status: documentData?.status,
                        artist: documentData?.artist,
                        album: documentData?.album,
                        existsAtMaster: documentData?.existsAtMaster,
                        length: documentData?.length,
                        priority: documentData?.priority,
                        requestedBy: documentData?.requestedBy,
                        masterId: documentData?.masterId
                    });
                } catch (error) {
                    console.error('❌ Error fetching document:', error);
                    return;
                }
            }
            
            // For delete operations, we can't get the document data since it's already deleted
            if (change.operationType === 'delete') {
                console.log('🗑️ Document deleted:', {
                    documentId: change.documentKey?._id,
                    operationType: 'delete'
                });
            }
            
            // Create broadcast message
            const update = {
                operationType: change.operationType,
                songTitle: documentData?.title || (change.operationType === 'delete' ? 'Deleted Song' : 'Unknown Song'),
                status: documentData?.status || (change.operationType === 'delete' ? 'deleted' : 'unknown'),
                songId: change.documentKey?._id,
                artist: documentData?.artist || (change.operationType === 'delete' ? 'Deleted Artist' : 'Unknown Artist'),
                album: documentData?.album || (change.operationType === 'delete' ? 'Deleted Album' : 'Unknown Album'),
                duration: documentData?.duration || documentData?.length || 0,
                priority: documentData?.priority || 1,
                requestedBy: documentData?.requestedBy || 'system',
                masterId: documentData?.masterId || 'unknown',
                existsAtMaster: documentData?.existsAtMaster || false,
                timestamp: new Date().toISOString()
            };
            
            // Add currentTime for playing songs
            if (documentData?.status === 'playing') {
                update.currentTime = 0;
            }
            
            // Add songIndex for queue position
            if (change.operationType === 'insert' || change.operationType === 'update') {
                update.songIndex = 0;
            }
            
            console.log('📤 Broadcasting tracklist update:', {
                operation: change.operationType,
                song: update.songTitle,
                status: update.status,
                artist: update.artist,
                album: update.album,
                existsAtMaster: update.existsAtMaster
            });
            
            // Broadcast to all connected clients
            broadcastTracklistUpdate(update);
        });
        
        changeStream.on('error', (error) => {
            console.error('❌ Change stream error:', error);
            // Attempt to reconnect after 5 seconds
            setTimeout(() => {
                console.log('🔄 Attempting to reconnect change stream...');
                connectToMongoDB();
            }, 5000);
        });
        
        changeStream.on('close', () => {
            console.log('⚠️ Change stream closed, attempting to reconnect...');
            setTimeout(() => {
                connectToMongoDB();
            }, 5000);
        });
        
    } catch (error) {
        console.error('❌ MongoDB connection error:', error);
        // Retry connection after 10 seconds
        setTimeout(() => {
            console.log('🔄 Retrying MongoDB connection...');
            connectToMongoDB();
        }, 10000);
    }
}

// Broadcast message to all connected clients
function broadcastToClients(message) {
    const messageStr = JSON.stringify(message);
    let sentCount = 0;
    
    clients.forEach(client => {
        if (client.readyState === WebSocket.OPEN) {
            try {
                client.send(messageStr);
                sentCount++;
            } catch (error) {
                console.error('❌ Error sending message to client:', error);
                clients.delete(client);
            }
        } else {
            // Remove disconnected clients
            clients.delete(client);
        }
    });
    
    console.log(`📡 Broadcasted to ${sentCount} clients:`, {
        type: message.type,
        operationType: message.operationType,
        documentId: message.data?.documentId
    });
}

// Broadcast tracklist update using the provided format
function broadcastTracklistUpdate(update) {
    const message = JSON.stringify({
        operationType: update.operationType,
        songTitle: update.songTitle,
        status: update.status,
        currentTime: update.currentTime,
        songIndex: update.songIndex,
        songId: update.songId,
        artist: update.artist,
        album: update.album,
        duration: update.duration,
        priority: update.priority,
        requestedBy: update.requestedBy,
        masterId: update.masterId,
        existsAtMaster: update.existsAtMaster,
        timestamp: new Date().toISOString()
    });
    
    console.log(`[WEBSOCKET_SERVER] Broadcasting tracklist update:`, update);
    
    clients.forEach(client => {
        if (client.readyState === WebSocket.OPEN) {
            client.send(message);
        }
    });
}

// Example usage functions for testing
function simulatePause() {
    broadcastTracklistUpdate({
        operationType: 'pause',
        songTitle: 'Current Song',
        status: 'paused',
        currentTime: 45.5
    });
}

function simulateResume() {
    broadcastTracklistUpdate({
        operationType: 'resume',
        songTitle: 'Current Song',
        status: 'playing',
        currentTime: 45.5
    });
}

function simulateSkip() {
    broadcastTracklistUpdate({
        operationType: 'skip',
        songTitle: 'Next Song',
        status: 'playing',
        songIndex: 1
    });
}

function simulateInsert() {
    broadcastTracklistUpdate({
        operationType: 'insert',
        songTitle: 'New Song Added',
        status: 'queued',
        songId: 'song_' + Date.now(),
        artist: 'Test Artist',
        album: 'Test Album',
        duration: 180, // 3 minutes
        priority: 1,
        requestedBy: 'user',
        masterId: 'master',
        existsAtMaster: true
    });
}

// Handle WebSocket connections
wss.on('connection', (ws, req) => {
    const clientId = Math.random().toString(36).substr(2, 9);
    console.log(`🔌 New monitoring client connected: ${clientId}`);
    
    // Add client to set
    clients.add(ws);
    
    // Send welcome message
    const welcomeMessage = {
        type: 'welcome',
        clientId: clientId,
        timestamp: new Date().toISOString(),
        message: 'Connected to tracklist monitor'
    };
    
    ws.send(JSON.stringify(welcomeMessage));
    
    // Send current tracklist status
    if (db) {
        db.collection('tracklist').find({}).sort({ priority: 1, createdAt: 1 }).toArray()
            .then(tracklist => {
                const statusMessage = {
                    type: 'initial_status',
                    timestamp: new Date().toISOString(),
                    data: {
                        tracklist: tracklist,
                        totalSongs: tracklist.length,
                        queuedSongs: tracklist.filter(s => s.status === 'queued').length,
                        playingSongs: tracklist.filter(s => s.status === 'playing').length,
                        pausedSongs: tracklist.filter(s => s.status === 'paused').length,
                        playedSongs: tracklist.filter(s => s.status === 'played').length,
                        skippedSongs: tracklist.filter(s => s.status === 'skipped').length
                    }
                };
                ws.send(JSON.stringify(statusMessage));
                console.log(`📤 Sent initial status to client ${clientId}: ${tracklist.length} songs`);
            })
            .catch(error => {
                console.error('❌ Error sending initial status:', error);
            });
    }
    
    // Handle client messages
    ws.on('message', (message) => {
        try {
            const data = JSON.parse(message);
            console.log(`📨 Message from client ${clientId}:`, data.type);
            
            switch (data.type) {
                case 'ping':
                    ws.send(JSON.stringify({
                        type: 'pong',
                        timestamp: new Date().toISOString(),
                        clientId: clientId
                    }));
                    break;
                    
                case 'get_status':
                    if (db) {
                        db.collection('tracklist').find({}).sort({ priority: 1, createdAt: 1 }).toArray()
                            .then(tracklist => {
                                const statusMessage = {
                                    type: 'status_update',
                                    timestamp: new Date().toISOString(),
                                    data: {
                                        tracklist: tracklist,
                                        totalSongs: tracklist.length,
                                        queuedSongs: tracklist.filter(s => s.status === 'queued').length,
                                        playingSongs: tracklist.filter(s => s.status === 'playing').length,
                                        pausedSongs: tracklist.filter(s => s.status === 'paused').length,
                                        playedSongs: tracklist.filter(s => s.status === 'played').length,
                                        skippedSongs: tracklist.filter(s => s.status === 'skipped').length
                                    }
                                };
                                ws.send(JSON.stringify(statusMessage));
                            });
                    }
                    break;
                    
                default:
                    console.log(`❓ Unknown message type from client ${clientId}:`, data.type);
            }
        } catch (error) {
            console.error(`❌ Error parsing message from client ${clientId}:`, error);
        }
    });
    
    // Handle client disconnect
    ws.on('close', () => {
        console.log(`🔌 Monitoring client disconnected: ${clientId}`);
        clients.delete(ws);
    });
    
    // Handle client errors
    ws.on('error', (error) => {
        console.error(`❌ WebSocket error for client ${clientId}:`, error);
        clients.delete(ws);
    });
});

// API endpoint to get current status
app.get('/api/status', async (req, res) => {
    try {
        if (!db) {
            return res.status(500).json({ error: 'Database not connected' });
        }

        const tracklistCollection = db.collection('tracklist');
        const tracklist = await tracklistCollection.find({}).sort({ priority: 1, createdAt: 1 }).toArray();
        
        const status = {
            totalSongs: tracklist.length,
            queuedSongs: tracklist.filter(s => s.status === 'queued').length,
            playingSongs: tracklist.filter(s => s.status === 'playing').length,
            pausedSongs: tracklist.filter(s => s.status === 'paused').length,
            playedSongs: tracklist.filter(s => s.status === 'played').length,
            skippedSongs: tracklist.filter(s => s.status === 'skipped').length,
            connectedClients: clients.size,
            tracklist: tracklist
        };
        
        res.json(status);
    } catch (error) {
        console.error('Error getting status:', error);
        res.status(500).json({ error: 'Failed to get status' });
    }
});

// Health check endpoint
app.get('/api/health', (req, res) => {
    res.json({
        status: 'healthy',
        timestamp: new Date().toISOString(),
        connectedClients: clients.size,
        databaseConnected: db !== null,
        changeStreamActive: changeStream !== null
    });
});

// Control endpoints: pause, resume, skip current song
app.post('/api/pause', async (req, res) => {
    try {
        if (!db) return res.status(500).json({ success: false, message: 'Database not connected' });
        const { tracklistId } = req.body || {};
        const col = db.collection('tracklist');

        let query = null;
        if (tracklistId) {
            // Try ObjectId first; if it fails, treat it as string _id
            try {
                query = { _id: new ObjectId(tracklistId) };
            } catch {
                query = { _id: tracklistId };
            }
        }
        // If no id provided or not found, fall back to paused/playing
        let doc = null;
        if (query) {
            doc = await col.findOne(query);
        }
        if (!doc) {
            // Fallback: pause the currently playing song
            doc = await col.findOne({ status: 'playing' });
        }

        if (!doc) return res.status(404).json({ success: false, message: 'No song currently playing to pause' });

        await col.updateOne({ _id: doc._id }, { $set: { status: 'paused' } });
        
        // Broadcast explicit PAUSE command to all WebSocket clients
        const pauseMessage = JSON.stringify({
            operationType: 'pause',
            songId: doc._id,
            songTitle: doc.title || 'Unknown Song',
            status: 'paused',
            timestamp: new Date().toISOString()
        });
        
        clients.forEach(client => {
            if (client.readyState === WebSocket.OPEN) {
                client.send(pauseMessage);
            }
        });
        
        console.log('[API] /api/pause -> Broadcasted PAUSE command to clients');
        return res.json({ success: true, message: 'Song paused', id: String(doc._id) });
    } catch (err) {
        console.error('[API] pause error', err);
        return res.status(500).json({ success: false, message: 'Internal error' });
    }
});

app.post('/api/resume', async (req, res) => {
    try {
        if (!db) return res.status(500).json({ success: false, message: 'Database not connected' });
        const { tracklistId } = req.body || {};
        const col = db.collection('tracklist');

        let query = null;
        if (tracklistId) {
            try {
                query = { _id: new ObjectId(tracklistId) };
            } catch {
                query = { _id: tracklistId };
            }
        }
        let doc = null;
        if (query) {
            doc = await col.findOne(query);
        }
        if (!doc) {
            // Fallback: resume the currently paused song
            doc = await col.findOne({ status: 'paused' });
        }
        if (!doc) return res.status(404).json({ success: false, message: 'No song currently paused to resume' });

        await col.updateOne({ _id: doc._id }, { $set: { status: 'playing' } });
        
        // Broadcast explicit RESUME command to all WebSocket clients
        const resumeMessage = JSON.stringify({
            operationType: 'resume',
            songId: doc._id,
            songTitle: doc.title || 'Unknown Song',
            status: 'playing',
            timestamp: new Date().toISOString()
        });
        
        clients.forEach(client => {
            if (client.readyState === WebSocket.OPEN) {
                client.send(resumeMessage);
            }
        });
        
        console.log('[API] /api/resume -> Broadcasted RESUME command to clients');
        return res.json({ success: true, message: 'Song resumed', id: String(doc._id) });
    } catch (err) {
        console.error('[API] resume error', err);
        return res.status(500).json({ success: false, message: 'Internal error' });
    }
});

app.post('/api/skip', async (req, res) => {
    try {
        if (!db) return res.status(500).json({ success: false, message: 'Database not connected' });
        const { tracklistId } = req.body || {};
        const col = db.collection('tracklist');

        let query = null;
        if (tracklistId) {
            try {
                query = { _id: new ObjectId(tracklistId) };
            } catch {
                query = { _id: tracklistId };
            }
        }
        let doc = null;
        if (query) {
            doc = await col.findOne(query);
        }
        if (!doc) {
            // Skip the currently playing or paused song
            doc = await col.findOne({ status: { $in: ['playing', 'paused'] } }, { sort: { createdAt: 1 } });
        }
        if (!doc) return res.status(404).json({ success: false, message: 'No song currently playing or paused to skip' });

        await col.deleteOne({ _id: doc._id });
        return res.json({ success: true, message: 'Song deleted from tracklist', id: String(doc._id) });
    } catch (err) {
        console.error('[API] skip error', err);
        return res.status(500).json({ success: false, message: 'Internal error' });
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

// Test simulation endpoints
app.post('/api/test/pause', (req, res) => {
    console.log('[API] Test pause called');
    simulatePause();
    res.json({ success: true, message: 'Pause simulation sent' });
});

app.post('/api/test/resume', (req, res) => {
    console.log('[API] Test resume called');
    simulateResume();
    res.json({ success: true, message: 'Resume simulation sent' });
});

app.post('/api/test/skip', (req, res) => {
    console.log('[API] Test skip called');
    simulateSkip();
    res.json({ success: true, message: 'Skip simulation sent' });
});

app.post('/api/test/insert', (req, res) => {
    console.log('[API] Test insert called');
    simulateInsert();
    res.json({ success: true, message: 'Insert simulation sent' });
});

// Start the server
console.log('🚀 Starting Tracklist Monitor Server...');
console.log('  📡 WebSocket: ws://localhost:8082');
console.log('  🌐 HTTP API: http://localhost:8082');

server.listen(8082, () => {
    console.log('✅ Tracklist Monitor Server running on port 8082');
});

// Connect to MongoDB
connectToMongoDB();

// Graceful shutdown
process.on('SIGINT', () => {
    console.log('\n🛑 Shutting down Tracklist Monitor Server...');
    
    if (changeStream) {
        changeStream.close();
    }
    
    wss.close(() => {
        console.log('✅ Tracklist Monitor Server closed');
        process.exit(0);
    });
});

// Keep track of connected clients
setInterval(() => {
    if (clients.size > 0) {
        console.log(`📊 Currently monitoring ${clients.size} clients`);
    }
}, 30000); // Log every 30 seconds
