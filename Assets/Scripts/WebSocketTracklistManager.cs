using System;
using System.Collections;
using UnityEngine;
using WebSocketSharp;

/// <summary>
/// Alternative real-time approach using WebSockets
/// This can be used instead of MongoDB Change Streams for custom real-time updates
/// </summary>
public class WebSocketTracklistManager : MonoBehaviour
{
    [Header("WebSocket Settings")]
    public string webSocketUrl = "ws://localhost:8080";
    public float reconnectInterval = 5f;
    
    private WebSocket webSocket;
    private bool isConnected = false;
    private Coroutine reconnectCoroutine;
    
    public static event Action<TracklistUpdate> OnTracklistUpdate;
    
    private void Start()
    {
        ConnectToWebSocket();
    }
    
    private void ConnectToWebSocket()
    {
        try
        {
            webSocket = new WebSocket(webSocketUrl);
            
            webSocket.OnOpen += OnWebSocketOpen;
            webSocket.OnMessage += OnWebSocketMessage;
            webSocket.OnError += OnWebSocketError;
            webSocket.OnClose += OnWebSocketClose;
            
            webSocket.Connect();
            
            Debug.Log("[WEBSOCKET] Attempting to connect to WebSocket server...");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WEBSOCKET] Error connecting to WebSocket: {ex.Message}");
            StartReconnectTimer();
        }
    }
    
    private void OnWebSocketOpen(object sender, EventArgs e)
    {
        isConnected = true;
        Debug.Log("[WEBSOCKET] Connected to WebSocket server");
        
        if (reconnectCoroutine != null)
        {
            StopCoroutine(reconnectCoroutine);
            reconnectCoroutine = null;
        }
    }
    
    private void OnWebSocketMessage(object sender, MessageEventArgs e)
    {
        try
        {
            var tracklistUpdate = JsonUtility.FromJson<TracklistUpdate>(e.Data);
            
            Debug.Log($"[WEBSOCKET] Received tracklist update: {tracklistUpdate.operationType} - {tracklistUpdate.songTitle}");
            
            // Notify subscribers
            OnTracklistUpdate?.Invoke(tracklistUpdate);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WEBSOCKET] Error parsing WebSocket message: {ex.Message}");
        }
    }
    
    private void OnWebSocketError(object sender, ErrorEventArgs e)
    {
        Debug.LogError($"[WEBSOCKET] WebSocket error: {e.Message}");
        isConnected = false;
        StartReconnectTimer();
    }
    
    private void OnWebSocketClose(object sender, CloseEventArgs e)
    {
        Debug.Log($"[WEBSOCKET] WebSocket closed: {e.Reason}");
        isConnected = false;
        StartReconnectTimer();
    }
    
    private void StartReconnectTimer()
    {
        if (reconnectCoroutine == null)
        {
            reconnectCoroutine = StartCoroutine(ReconnectTimer());
        }
    }
    
    private IEnumerator ReconnectTimer()
    {
        yield return new WaitForSeconds(reconnectInterval);
        
        Debug.Log("[WEBSOCKET] Attempting to reconnect...");
        ConnectToWebSocket();
        
        reconnectCoroutine = null;
    }
    
    private void OnDestroy()
    {
        if (webSocket != null)
        {
            webSocket.Close();
        }
        
        if (reconnectCoroutine != null)
        {
            StopCoroutine(reconnectCoroutine);
        }
    }
}

// TracklistUpdate class moved to WebSocketTracklistAPI.cs to avoid duplication
