using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

public class MasterNetworkHandler : MonoBehaviour
{
    [Header("Network Settings")]
    public int port = 8888;

    [Header("UI Elements")]
    public TMP_InputField portInputField;
    public Button setPortButton;
    public Text statusText;
    public Text receivedRequestText;

    private TcpListener server;
    private bool isRunning = false;
    private List<TcpClient> connectedClients = new List<TcpClient>(); 

    private TrackQueueManager trackQueueManager;
    private AlbumManager albumManager;
    private string lastConnectedClientIP;
    private string receivedUsername;
    private string receivedPassword;

    private void Start()
    {
        trackQueueManager = FindObjectOfType<TrackQueueManager>();
        albumManager = FindObjectOfType<AlbumManager>();

        if (trackQueueManager == null || albumManager == null)
        {
            Debug.LogError("Missing TrackQueueManager or AlbumManager in the scene!");
            UpdateStatusText("Missing required components!");
            return;
        }

        if (portInputField != null)
        {
            portInputField.text = port.ToString();
        }

        if (setPortButton != null)
        {
            setPortButton.onClick.AddListener(SetPortAndStartServer);
        }
    }

    private void SetPortAndStartServer()
    {
        if (int.TryParse(portInputField.text, out int newPort))
        {
            port = newPort;
            UpdateStatusText($"Port set to {port}. Starting server...");
            StartServer();
        }
        else
        {
            UpdateStatusText("Invalid port number. Please enter a valid integer.");
        }
    }

    private void StartServer()
    {
        if (isRunning)
        {
            UpdateStatusText("Server is already running. Restart the application to change the port.");
            return;
        }

        try
        {
            server = new TcpListener(IPAddress.Any, port);
            server.Start();
            isRunning = true;
            UpdateStatusText($"Server listening on port {port}...");
            AcceptClientsAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start server: {ex.Message}");
            UpdateStatusText($"Failed to start server: {ex.Message}");
        }
    }

    private async void AcceptClientsAsync()
    {
        while (isRunning)
        {
            TcpClient client = await server.AcceptTcpClientAsync();

            lock (connectedClients)
            {
                connectedClients.Add(client); // Store the connected client
            }

            string clientIP = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            Debug.Log($"Slave Application connected from {clientIP}.");
            UpdateStatusText($"Slave Application connected from {clientIP}.");

            lastConnectedClientIP = clientIP;

            HandleClientAsync(client);
        }
    }

    private async void HandleClientAsync(TcpClient client)
    {
        using (NetworkStream stream = client.GetStream())
        {
            byte[] buffer = new byte[1024];
            int bytesRead;

            try
            {
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    string request = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    Debug.Log($"Received request: {request}");
                    UpdateReceivedRequestText(request);
                    HandleRequest(request);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Client disconnected with error: {ex.Message}");
            }
        }

        lock (connectedClients)
        {
            connectedClients.Remove(client); // Remove disconnected clienta
        }

        Debug.Log("Slave Application disconnected.");
        UpdateStatusText("Slave Application disconnected.");
    }
    private void HandleRequest(string request)
    {
        string[] parts = request.Split('|');
        string command = parts[0];

        switch (command)
        {
            case "AUTH":
                receivedUsername = parts[1];
                receivedPassword = parts[2];
                break;

            case "ADD_SONG":
                if (parts.Length > 1)
                {
                    string input = parts[1];

                    // Check if input looks like DD-DD format
                    if (input.Length == 5 && input[2] == '-' &&
                        int.TryParse(input.Substring(0, 2), out _) &&
                        int.TryParse(input.Substring(3, 2), out _))
                    {
                        // Treat as keypad input
                        //StartCoroutine(trackQueueManager.AddSongToQueue(input, 0f, false));
                    }
                    else
                    {
                        // Treat as song name
                        StartCoroutine(trackQueueManager.AddSongToQueueByName(input, 0f, false));
                    }
                }
                else
                {
                    Debug.LogError("ADD_SONG request missing song identifier.");
                }
                break;

            case "PAUSE_RESUME":
                trackQueueManager.PauseResumeSong();
                break;

            case "NEXT_SONG":
                trackQueueManager.SkipToNextSong();
                break;

            case "PREVIOUS_SONG":
                trackQueueManager.PlayPreviousSong();
                break;

            default:
                Debug.LogWarning($"Unknown command: {command}");
                break;
        }
    }
    private void UpdateStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log(message);
    }

    private void UpdateReceivedRequestText(string request)
    {
        if (receivedRequestText != null)
        {
            receivedRequestText.text = $"Received: {request}";
        }
        Debug.Log($"Received request: {request}");
    }

    private void OnApplicationQuit()
    {
        isRunning = false;
        server?.Stop();

        lock (connectedClients)
        {
            foreach (var client in connectedClients)
            {
                client.Close();
            }
            connectedClients.Clear();
        }
    }

    // ---------------------------------------
    // **New Feature: Master Sends Requests**
    // ---------------------------------------

    public async Task SendRequestToSlave(string request)
    {
        if (connectedClients[0] == null || !connectedClients[0].Connected)
        {
            Debug.LogWarning("Not connected to the slave. Attempting to reconnect...");
            return;
        }

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(request);
            await connectedClients[0].GetStream().WriteAsync(data, 0, data.Length);
            Debug.Log($"Request sent to slave: {request}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to send request to slave: {ex.Message}");
        }
    }
    public async Task SendSongLengthToSlave(float songLength)
    {
        // Convert float to string
        string songLengthStr = songLength.ToString();

        // Test the conversion process with various conditions
        if (songLengthStr.Contains("."))
        {
            Debug.Log("Decimal detected in song length");
        }
        else
        {
            Debug.Log("No decimal in song length");
        }

        // Further string manipulations for testing
        if (songLengthStr.Length > 5)
        {
            songLengthStr = songLengthStr.Substring(0, 5); // Limit string to first 5 characters
            Debug.Log($"Trimmed to 5 chars: {songLengthStr}");
        }

        if (float.TryParse(songLengthStr, out float parsedSongLength))
        {
            Debug.Log($"Parsed song length: {parsedSongLength}");
        }
        else
        {
            Debug.LogError("Failed to parse song length.");
        }

        int songLengthInt = (int)parsedSongLength;
        string request = $"SONG_LENGTH|{songLengthInt}";

        await SendRequestToSlave(request);
    }
    public async Task Pause_Resume()
    {
        await SendRequestToSlave("PAUSE_RESUME");
    }
    public async Task PlayNextSong()
    {
        await SendRequestToSlave("NEXT_SONG");
    }
    public async void AddSongToQueue(string keypadInput)
    {
        await SendRequestToSlave($"ADD_SONG|{keypadInput}");
    }
    public async Task SendSongWithLengthToSlave(string keypadInput, float songLength)
    {
        string songLengthStr = songLength.ToString();

        if (songLengthStr.Length > 5)
        {
            songLengthStr = songLengthStr.Substring(0, 5);
        }

        if (!float.TryParse(songLengthStr, out float parsedSongLength))
        {
            Debug.LogError("Failed to parse song length.");
            return;
        }

        int songLengthInt = (int)parsedSongLength;

        string request = $"ADD_SONG|{keypadInput}|{songLengthInt}";

        await SendRequestToSlave(request);
    }


}
