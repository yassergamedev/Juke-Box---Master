using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Text;
using UnityEngine.Networking;
using System;

[System.Serializable]
public class LocalTuyaRequest
{
    public string device_id;
    public string device_key;
    public string device_ip;
    public string version;
    public string command;
    public string dps;
    public string value;
}
public enum DeviceType
{
    Fan,RGB,Switch,Light,FanLight,Unknown
}
public class LocalTuyaController : MonoBehaviour
{
    [Header("Device Details")]
    public string device_name;
    public string deviceId;
    public string deviceKey;
    public string deviceIp;
    public string version = "3.3";
    [Header("Device Type")]
    public DeviceType deviceType = DeviceType.Unknown;

    [Header("Python Server Settings")]
    public string serverUrl = "http://localhost:5000/control";

    [Header("UI")]
    public GameObject offButton;

    private void Start()
    {
        LoadIp();
    }

    public void TurnOn(Action<string> callback = null)
    {
        SendLocalCommand("turn_on", "20", "true", callback);
    }

    public void TurnOff(Action<string> callback = null)
    {
        SendLocalCommand("turn_off", "20", "false", callback);
    }

    public void SetColorRed(Action<string> callback = null)
    {
        SendLocalCommand("set_color", "", "", callback);
    }

    // Updated to accept a callback
    public void SendLocalCommand(string command, string dps, string value, Action<string> callback = null)
    {
        Debug.Log("Sending local command: " + command);
        LocalTuyaRequest requestPayload = new LocalTuyaRequest
        {
            device_id = deviceId,
            device_key = deviceKey,
            device_ip = deviceIp,
            version = version,
            command = command,
            dps = dps,
            value = value
        };

        StartCoroutine(SendTuyaRequest(JsonUtility.ToJson(requestPayload), callback));
    }

    // Modified coroutine to send result to callback
    private IEnumerator SendTuyaRequest(string jsonData, Action<string> callback)
    {
        using (UnityWebRequest request = new UnityWebRequest(serverUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            string result;

            if (request.result == UnityWebRequest.Result.Success)
            {
                result = "Success: " + request.downloadHandler.text;
                Debug.Log(result);
                if (offButton != null) offButton.SetActive(true);
                gameObject.SetActive(false);
            }
            else
            {
                result = " Error: " + request.error;
                Debug.LogError(result);
            }

            callback?.Invoke(result);
        }
    }

    public void SaveIp()
    {
        PlayerPrefs.SetString("DeviceIp_" + deviceId, deviceIp);
        PlayerPrefs.Save();
    }

    private void LoadIp()
    {
        string savedIp = PlayerPrefs.GetString("DeviceIp_" + deviceId, "");
        if (!string.IsNullOrEmpty(savedIp))
        {
            deviceIp = savedIp;
            Debug.Log($"Loaded saved IP for {deviceId}: {deviceIp}");
        }
    }
}
