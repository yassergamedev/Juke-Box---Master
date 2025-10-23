using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SlaveController : MonoBehaviour
{
    [Header("Connection Settings")]
    public string masterIpAddress = "192.168.100.4";
    public int masterPort = 8888;
    /*    [Header("FTP Settings")]
        public string ftpServerAddress = "ftp://192.168.100.4"; 
        public string ftpUsername = "ftpuser"; 
        public string ftpPassword = "ftppassword"; */
    [Header("UI Elements")]
    public Text debugText;
    public TMP_InputField ipInputField;
    public TMP_InputField portInputField;
    public TMP_InputField username;
    public TMP_InputField password;
    public Button connectButton;
    public Text requestStatusText;
    public Text consoleText;
    private TcpClient client;
    private NetworkStream stream;
    private CancellationTokenSource receiveTokenSource;
    private AlbumManager albumManager;
    private TrackQueueManager trackQueueManager;
    public int totalAttempts = 5;
    private int currentAttemptCount;
    private void Start()
    {
        if (PlayerPrefs.GetString("IP") != null)
        {
            masterIpAddress = PlayerPrefs.GetString("IP");
        }
        if (ipInputField != null)
        {
            ipInputField.text = masterIpAddress;
        }

        if (portInputField != null)
        {
            portInputField.text = masterPort.ToString();
        }

        if (connectButton != null)
        {
            connectButton.onClick.AddListener(async () => await ConnectToMaster());
        }

        albumManager = FindAnyObjectByType<AlbumManager>();
        if (albumManager == null)
        {
            Debug.LogError("AlbumManager component is missing!");
        }
        trackQueueManager = FindAnyObjectByType<TrackQueueManager>();

        StartCoroutine(PeriodicConnection());
    }
    private IEnumerator PeriodicConnection()
    {
        while (currentAttemptCount < totalAttempts && (client == null || !client.Connected))
        {
            yield return new WaitForSeconds(5f);

            if (client == null || !client.Connected)
            {
                _ = ConnectToMaster();
                currentAttemptCount++;
            }
        }
    }


    private async Task ConnectToMaster()
    {
        if (ipInputField != null)
        {
            masterIpAddress = ipInputField.text;
            PlayerPrefs.SetString("IP", masterIpAddress);
        }

        if (portInputField != null && int.TryParse(portInputField.text, out int port))
        {
            masterPort = port;
        }

        try
        {
            client = new TcpClient();
            UpdateDebugText($"Connecting to Master Application at {masterIpAddress}:{masterPort}...");

            await client.ConnectAsync(masterIpAddress, masterPort);
            stream = client.GetStream();

            UpdateDebugText($"Connected to Master Application at {masterIpAddress}:{masterPort}.");

            // Start receiving data continuously
            await ReceiveDataAsync();
        }
        catch (SocketException ex)
        {
            UpdateDebugText($"Failed to connect to Master Application: {ex.Message}");
        }
    }


    private async Task SendRequest(string request)
    {
        if (client == null || !client.Connected)
        {
            UpdateDebugText("Not connected to the Master Application. Attempting to reconnect...");
            await ConnectToMaster();
            return;
        }

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(request);
            await stream.WriteAsync(data, 0, data.Length);
            UpdateRequestStatusText($"Request sent: {request}", true);
        }
        catch (Exception ex)
        {
            UpdateRequestStatusText($"Failed to send request: {ex.Message}", false);
        }
    }
    private async Task ReceiveDataAsync()
    {
        byte[] buffer = new byte[8192];
        try
        {
            while (client.Connected)  // Keep reading while the client is connected
            {
                if (stream.DataAvailable)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        UpdateDebugText("Master closed the connection.");
                        break;
                    }

                    string receivedChunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    UpdateConsoleText($"Received Chunk: {receivedChunk}");
                    ProcessReceivedData(receivedChunk);
                }
                else
                {
                    await Task.Delay(50); // Prevent tight looping and give the thread some time
                }
            }
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error receiving data: {ex.Message}");
        }
        finally
        {
            Disconnect();
        }
    }


    private void Disconnect()
    {
        try
        {
            receiveTokenSource?.Cancel(); // Cancel the receive loop
            receiveTokenSource?.Dispose();
            receiveTokenSource = null;

            stream?.Close();
            stream?.Dispose();
            stream = null;

            client?.Close();
            client?.Dispose();
            client = null;

            UpdateDebugText("Disconnected from Master.");
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error during disconnection: {ex.Message}");
        }
    }



    private void ProcessReceivedData(string data)
    {
        UpdateDebugText($"Raw received data: {data}");
        try
        {
            HandleRequest(data); 
        }
        catch (Exception ex)
        {
            UpdateDebugText($"Error processing request: {ex.Message}");
        }
    }


    private void OnApplicationQuit()
    {
        receiveTokenSource?.Cancel();

        if (client != null)
        {
            client.Close();
        }
    }

    public async void AddSongToQueue(string keypadInput)
    {
        await SendRequest($"ADD_SONG|{keypadInput}");
       // trackQueueManager.AddSongToQueue(keypadInput);
    }

    public async void PauseResumeSong()
    {
        await SendRequest("PAUSE_RESUME");
    }

    public async void PlayNextSong()
    {
        await SendRequest("NEXT_SONG");
    }

    public async void PlayPreviousSong()
    {
        await SendRequest("PREVIOUS_SONG");
    }

    private void UpdateDebugText(string message)
    {
        if (debugText != null)
        {
            debugText.text = message;
        }
        Debug.Log(message);
    }

    private void UpdateConsoleText(string message)
    {
        if (consoleText != null)
        {
            consoleText.text = message;
        }
        Debug.Log(message);
    }

    private void UpdateRequestStatusText(string message, bool success)
    {
        if (requestStatusText != null)
        {
            requestStatusText.color = success ? Color.green : Color.red;
            requestStatusText.text = message;
        }
        Debug.Log(message);
    }
    private void HandleRequest(string request)
    {
        string[] parts = request.Split('|');
        string command = parts[0];

        switch (command)
        {
            case "ADD_SONG":
                if (parts.Length > 2) // Ensure there are enough parts
                {
                    string keypadInput = parts[1];

                    // Try parsing the song length from parts[2] as a float
                    if (float.TryParse(parts[2], out float songLength))
                    {
                        // Pass the song length along with other parameters to AddSongToQueue
                        //StartCoroutine(trackQueueManager?.AddSongToQueue(keypadInput, songLength, true));

                        // Log the song addition to a text file
                        LogSongToFile( keypadInput, songLength);

                        // Optional: Uncomment to reset progress or any other variables
                        /* trackQueueManager.progressSlider.value = 0;
                        trackQueueManager.slaveCurrentTime = 0; */
                        if (trackQueueManager.isFirstSong)
                        {
                            trackQueueManager.isSlavePlaying = true;
                            trackQueueManager.isFirstSong = false;
                        }

                        UpdateDebugText($"Added song: {keypadInput} with length: {songLength} seconds.");
                    }
                    else
                    {
                        UpdateDebugText($"Failed to parse song length: {parts[2]}");
                    }
                }
                break;


            case "SONG_LENGTH":
                if (parts.Length > 1) 
                {
                    if (int.TryParse(parts[1], out int songLengthInt))
                    {
                        trackQueueManager?.SetSongLength(songLengthInt);
                        trackQueueManager.isSlavePlaying = true;
                        UpdateDebugText($"Received song length: {songLengthInt}.");
                    }
                    else
                    {
                        UpdateDebugText($"Failed to parse song length: {parts[1]}");
                    }
                }
                break;

            case "PAUSE_RESUME":
                trackQueueManager.isSlavePlaying = !trackQueueManager.isSlavePlaying;
                break;

            case "NEXT_SONG":
                trackQueueManager?.SkipSongSlave();
                break;

            case "PREVIOUS_SONG":
                trackQueueManager?.PlayPreviousSong();
                break;

            default:
                Debug.LogWarning($"Unknown command: {command}");
                break;
        }
    }
    private void LogSongToFile(string keypadInput, float songLength)
    {
        try
        {
            string todayDate = DateTime.Now.ToString("yyyy-MM-dd"); // Format: YYYY-MM-DD

            // Ensure directory exists
            if (!Directory.Exists(albumManager.AlbumBasePath))
            {
                Directory.CreateDirectory(albumManager.AlbumBasePath);
            }

            // Define the path to the log file (log.txt in the album base directory)
            string logFilePath = Path.Combine(albumManager.AlbumBasePath,todayDate);

            // Append song info to the log file
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"{DateTime.Now:HH:mm:ss} - {keypadInput} (Length: {songLength} sec)");
            }

            albumManager.UpdateDebugText($"Logged song to {logFilePath}");
        }
        catch (Exception ex)
        {
            albumManager.UpdateDebugText($"Error writing to log file: {ex.Message}");
        }
    }






}
