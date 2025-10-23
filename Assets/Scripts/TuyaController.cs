using TMPro;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;
using UnityEngine.UI;

[System.Serializable]
public class TuyaCommand
{
    public string code;
    public string value;
}

[System.Serializable]
public class TuyaCommandData
{
    public string code;
    public string value;
}

[System.Serializable]
public class TuyaCommandRequest
{
    public List<TuyaCommand> commands = new List<TuyaCommand>();
}

public class TuyaController : MonoBehaviour
{
    [Header("Tuya Device Settings")]
    public string device_name;
    public string deviceId;
    [Header("Device Type")]
    public DeviceType deviceType = DeviceType.Unknown;

    [Header("Server Settings")]
    public string serverUrl = "http://localhost:3000/control";

    [Header("Available Commands")]
    public TuyaCommandData[] availableCommands;

  

    [Header("Popup Open Button")]
    public Button openPopupButton; // Assign this in the Inspector

    [Header("Popup UI")]
    public PopupTuyaCommandsUI popupScript;
    private void Start()
    {
        openPopupButton = GetComponent<Button>();

        if (openPopupButton != null)
        {
            openPopupButton.onClick.RemoveAllListeners();
            openPopupButton.onClick.AddListener(OpenPopup);
        }
        else
        {
            Debug.LogWarning("Open popup button is not assigned.");
        }
    }

    // Method to show and assign popup
    public void OpenPopup()
    {
        if (popupScript != null)
        {
            popupScript.gameObject.SetActive(true);
            popupScript.Assign(availableCommands, this);
        }
        else
        {
            Debug.LogWarning("Popup script not assigned.");
        }
    }

    public void SendCommand(string code, string value, System.Action<string> callback = null)
    {
        TuyaCommandRequest commandRequest = new TuyaCommandRequest();
        commandRequest.commands.Add(new TuyaCommand
        {
            code = code,
            value = value
        });

        StartCoroutine(SendTuyaRequest(commandRequest, callback));
    }

    private IEnumerator SendTuyaRequest(TuyaCommandRequest commandRequest, System.Action<string> callback = null)
    {
        string jsonData = JsonUtility.ToJson(commandRequest);
        string fullUrl = $"{serverUrl}?deviceId={deviceId}";

        using (UnityWebRequest request = new UnityWebRequest(fullUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            string result;
            if (request.result == UnityWebRequest.Result.Success)
            {
                result = " Success: " + request.downloadHandler.text;
                Debug.Log(result);
            }
            else
            {
                result = " Error: " + request.error;
                Debug.LogError(result);
            }

            callback?.Invoke(result);
        }
    }
}
