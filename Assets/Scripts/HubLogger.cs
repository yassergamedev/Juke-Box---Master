using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// Centralized hub logging system for success/failure messages across all systems
/// Thread-safe, non-blocking, writes to both UI and file
/// </summary>
public class HubLogger : MonoBehaviour
{
    [Header("Logging Settings")]
    public TMP_Text logTextUI; // Assign the TMP GameObject for hub status logging
    public int maxLogLines = 50; // Maximum lines to display in UI (prevents freezing)
    public int maxLogLinesInMemory = 1000; // Maximum lines to keep in memory (prevents memory leak)
    public string logFileName = "HubStatusLog"; // Log file name (without extension, timestamp will be added)
    public bool enableFileLogging = true; // Toggle file logging on/off
    
    [Header("Log Categories")]
    public bool logTCP = true;
    public bool logMongoDB = true;
    public bool logWebSocket = true;
    public bool logQueueEnabled = true;
    public bool logAlbums = true;
    public bool logFiles = true;
    
    // Singleton instance
    public static HubLogger Instance { get; private set; }
    
    // Logging system
    private Queue<string> logQueue = new Queue<string>();
    private List<string> logLines = new List<string>();
    private object logLock = new object();
    private string logFilePath;
    private Coroutine logProcessingCoroutine;
    
    private void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeLogger();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void InitializeLogger()
    {
        // Create new log file with timestamp on each app start
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string logFileNameWithTimestamp = $"{logFileName}_{timestamp}.txt";
        logFilePath = Path.Combine(Application.persistentDataPath, logFileNameWithTimestamp);
        
        // Clear old log lines to prevent memory accumulation
        lock (logLock)
        {
            logLines.Clear();
            logQueue.Clear();
        }
        
        // Start log processing coroutine
        if (logProcessingCoroutine == null)
        {
            logProcessingCoroutine = StartCoroutine(ProcessLogQueue());
        }
        
        Log("Hub Logger initialized", LogCategory.System);
        if (enableFileLogging)
        {
            Log($"Log file: {logFilePath}", LogCategory.System);
        }
    }
    
    /// <summary>
    /// Logs a message (thread-safe, non-blocking)
    /// </summary>
    public static void Log(string message, LogCategory category = LogCategory.General)
    {
        if (Instance == null)
        {
            Debug.Log($"[HUB_LOG] {message}");
            return;
        }
        
        Instance.LogInternal(message, category);
    }
    
    /// <summary>
    /// Logs a success message
    /// </summary>
    public static void LogSuccess(string message, LogCategory category = LogCategory.General)
    {
        Log($"✓ SUCCESS: {message}", category);
    }
    
    /// <summary>
    /// Logs a failure/error message
    /// </summary>
    public static void LogFailure(string message, LogCategory category = LogCategory.General)
    {
        Log($"✗ FAILURE: {message}", category);
    }
    
    /// <summary>
    /// Logs a warning message
    /// </summary>
    public static void LogWarning(string message, LogCategory category = LogCategory.General)
    {
        Log($"⚠ WARNING: {message}", category);
    }
    
    private void LogInternal(string message, LogCategory category)
    {
        if (string.IsNullOrEmpty(message)) return;
        
        // Filter out TCP connection logs (too verbose)
        if (category == LogCategory.TCP && 
            (message.Contains("connected") || message.Contains("disconnected") || 
             message.Contains("Heartbeat") || message.Contains("heartbeat")))
        {
            // Only log TCP errors and important events, not every connection
            if (!message.Contains("ERROR") && !message.Contains("FAILURE") && 
                !message.Contains("started") && !message.Contains("stopped"))
            {
                return; // Skip verbose TCP connection logs
            }
        }
        
        // Check if this category is enabled
        if (!IsCategoryEnabled(category)) return;
        
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string categoryPrefix = GetCategoryPrefix(category);
        string logEntry = $"[{timestamp}] [{categoryPrefix}] {message}";
        
        // Add to queue (thread-safe)
        lock (logLock)
        {
            logQueue.Enqueue(logEntry);
            
            // Prevent queue from growing too large (prevent memory leak)
            if (logQueue.Count > 1000)
            {
                // Remove oldest entries if queue gets too large
                while (logQueue.Count > 500)
                {
                    logQueue.Dequeue();
                }
            }
        }
        
        // Also log to Unity console
        Debug.Log($"[HUB_LOG] [{categoryPrefix}] {message}");
    }
    
    private bool IsCategoryEnabled(LogCategory category)
    {
        switch (category)
        {
            case LogCategory.TCP: return logTCP;
            case LogCategory.MongoDB: return logMongoDB;
            case LogCategory.WebSocket: return logWebSocket;
            case LogCategory.Queue: return logQueueEnabled;
            case LogCategory.Albums: return logAlbums;
            case LogCategory.Files: return logFiles;
            default: return true;
        }
    }
    
    private string GetCategoryPrefix(LogCategory category)
    {
        switch (category)
        {
            case LogCategory.TCP: return "TCP";
            case LogCategory.MongoDB: return "MongoDB";
            case LogCategory.WebSocket: return "WebSocket";
            case LogCategory.Queue: return "Queue";
            case LogCategory.Albums: return "Albums";
            case LogCategory.Files: return "Files";
            case LogCategory.System: return "System";
            default: return "General";
        }
    }
    
    /// <summary>
    /// Processes log queue on main thread to prevent freezing
    /// </summary>
    private IEnumerator ProcessLogQueue()
    {
        while (true)
        {
            // Process logs in batches to avoid blocking
            int processed = 0;
            const int maxPerFrame = 5; // Process max 5 logs per frame
            
            lock (logLock)
            {
                while (logQueue.Count > 0 && processed < maxPerFrame)
                {
                    string logEntry = logQueue.Dequeue();
                    logLines.Add(logEntry);
                    processed++;
                    
                    // Prevent memory leak - limit logLines size
                    if (logLines.Count > maxLogLinesInMemory)
                    {
                        // Remove oldest entries, keep only the most recent
                        int removeCount = logLines.Count - maxLogLinesInMemory;
                        logLines.RemoveRange(0, removeCount);
                    }
                    
                    // Write to file asynchronously (non-blocking)
                    if (enableFileLogging)
                    {
                        _ = WriteLogToFileAsync(logEntry);
                    }
                }
            }
            
            // Update UI text (limit to maxLogLines to prevent freezing)
            if (logTextUI != null && logLines.Count > 0)
            {
                UpdateLogUI();
            }
            
            yield return null; // Wait one frame before processing more
        }
    }
    
    /// <summary>
    /// Updates the log UI text component
    /// </summary>
    private void UpdateLogUI()
    {
        if (logTextUI == null) return;
        
        try
        {
            // Only show the most recent lines to prevent UI freezing
            int startIndex = Mathf.Max(0, logLines.Count - maxLogLines);
            int count = logLines.Count - startIndex;
            
            var recentLogs = logLines.GetRange(startIndex, count);
            string displayText = string.Join("\n", recentLogs);
            
            // Update UI on main thread (this is already in a coroutine, so we're safe)
            logTextUI.text = displayText;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HUB_LOG] Error updating UI: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Writes log entry to file asynchronously (non-blocking)
    /// </summary>
    private async Task WriteLogToFileAsync(string logEntry)
    {
        try
        {
            // Use async file writing to prevent blocking
            using (StreamWriter writer = new StreamWriter(logFilePath, true, Encoding.UTF8))
            {
                await writer.WriteLineAsync(logEntry);
                await writer.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            // Don't throw - just log to console to avoid breaking the app
            Debug.LogError($"[HUB_LOG] Failed to write to log file: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Clears the log file (useful for maintenance)
    /// </summary>
    public static void ClearLogFile()
    {
        if (Instance == null) return;
        
        try
        {
            if (File.Exists(Instance.logFilePath))
            {
                File.Delete(Instance.logFilePath);
                Log("Log file cleared", LogCategory.System);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HUB_LOG] Failed to clear log file: {ex.Message}");
        }
    }
    
    private void OnDestroy()
    {
        // Stop log processing coroutine
        if (logProcessingCoroutine != null)
        {
            StopCoroutine(logProcessingCoroutine);
            logProcessingCoroutine = null;
        }
    }
}

/// <summary>
/// Categories for logging to enable/disable specific log types
/// </summary>
public enum LogCategory
{
    General,
    TCP,
    MongoDB,
    WebSocket,
    Queue,
    Albums,
    Files,
    System
}

