using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static DeviceRegistryManager;
using UnityEngine.Tilemaps;

[System.Serializable]
public class TimerData
{
    public string deviceId;
    public string groupName; // Optional
    public bool isOnTimer;
    public string time; // Format: "HH:mm"
    public List<string> days;
    public string startDate; // Can be empty
}

[System.Serializable]
public class TimerListWrapper
{
    public List<TimerData> timers = new();
}


public class TimerPanelManager : MonoBehaviour
{
    [Header("Timer Type")]
    public Toggle onToggle;
    public Toggle offToggle;

    [Header("Time and Date")]
    public TMP_InputField timeField;
    public TMP_InputField startDateField;

    [Header("Result Text")]
    public TMP_Text Result_Text;

    [Header("Day Toggles")]
    public Toggle[] dayToggles; 

    [Header("Device List")]
    public Transform deviceContainer;
    public GameObject deviceItemPrefab;

    [Header("Save Button")]
    public Button saveButton;

    private List<DeviceData> currentDevices = new();

    private DeviceData currentSingleDevice = null;
    private GroupData currentGroup = null;
    private void Start()
    {
        onToggle.onValueChanged.AddListener((isOn) =>
        {
            if (isOn && offToggle.isOn)
                offToggle.isOn = false;
        });

        offToggle.onValueChanged.AddListener((isOn) =>
        {
            if (isOn && onToggle.isOn)
                onToggle.isOn = false;
        });

        if (!onToggle.isOn && !offToggle.isOn)
            onToggle.isOn = true;
    }

    public void LoadTimerPanel(DeviceData device = null, GroupData group = null)
    {
        if (device == null && group == null)
        {
            Debug.LogWarning("TimerPanel loaded with no device or group!");
            return;
        }
        // Clear previous
        currentSingleDevice = null;
        currentGroup = null;
        currentDevices.Clear();
        foreach (Transform child in deviceContainer)
            Destroy(child.gameObject);


        currentSingleDevice = device;
        currentGroup = group;
      



        if (device != null)
        {
            currentDevices.Add(device);
            CreateDeviceItem(device);
        }
        else if (group != null)
        {
            foreach (string id in group.deviceIds)
            {
                var d = LoadDeviceById(id);
                if (d != null)
                {
                    currentDevices.Add(d);
                    CreateDeviceItem(d);
                }
            }
        }
    }

    private void CreateDeviceItem(DeviceData data)
    {
        GameObject item = Instantiate(deviceItemPrefab, deviceContainer);
        DeviceItemUI ui = item.GetComponent<DeviceItemUI>();
        ui.Setup(data.device_name, data.deviceId, null); 
    }

    public void OnSaveButtonClicked()
    {
        // 1. Validate time first
        string time = timeField.text;
        if (string.IsNullOrEmpty(time) || !IsValidTime(time))
        {
            Result_Text.text = "Invalid time format. Use HH:mm";
            return;
        }

        // 2. Get selected days with null checks
        List<string> selectedDays = new List<string>();
        foreach (var toggle in dayToggles)
        {
            if (toggle != null && toggle.isOn)
            {
                var text = toggle.GetComponentInChildren<TextMeshProUGUI>();
                if (text != null && !string.IsNullOrEmpty(text.text))
                {
                    selectedDays.Add(text.text);
                }
            }
        }

        if (selectedDays.Count == 0)
        {
            Result_Text.text = "Select at least one day";
            return;
        }

        // 3. Validate devices
        if (currentDevices == null || currentDevices.Count == 0)
        {
            Result_Text.text = "No devices selected";
            return;
        }

        // 4. Save timers with detailed error handling
        int successCount = 0;
        foreach (var device in currentDevices)
        {
            try
            {
                if (device == null)
                {
                    Debug.LogWarning("Skipping null device");
                    continue;
                }

                if (string.IsNullOrEmpty(device.deviceId))
                {
                    Debug.LogWarning($"Device has null/empty ID: {device.device_name}");
                    continue;
                }

                var timer = new TimerData()
                {
                    deviceId = device.deviceId,
                    groupName = currentGroup != null ? currentGroup.groupName : null,
                    isOnTimer = onToggle != null && onToggle.isOn,
                    time = time,
                    days = new List<string>(selectedDays), // Defensive copy
                    startDate = startDateField != null ? startDateField.text : ""
                };

                SaveTimerToFile(timer);
                successCount++;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save for {device?.deviceId}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // 5. User feedback
        if (successCount > 0)
        {
            Result_Text.text = $"Saved {successCount} timer(s)";
        }
        else
        {
            Result_Text.text = "Failed to save any timers";
        }
    }

    private bool IsValidTime(string t)
    {
        if (TimeSpan.TryParse(t, out var parsed))
        {
            return parsed.Hours < 24 && parsed.Minutes < 60;
        }
        Debug.LogWarning("[TimerPanel] Invalid time format.");
        return false;
    }

    private const string TIMER_SAVE_FILE = "timer_list.json";

    private void SaveTimerToFile(TimerData timer)
    {
        string fullPath = Path.Combine(Application.persistentDataPath, TIMER_SAVE_FILE);
        TimerListWrapper wrapper;

        if (File.Exists(fullPath))
        {
            string json = File.ReadAllText(fullPath);
            wrapper = JsonUtility.FromJson<TimerListWrapper>(json);
            if (wrapper == null) wrapper = new TimerListWrapper();
        }
        else
        {
            wrapper = new TimerListWrapper();
        }

        wrapper.timers.Add(timer);

        string newJson = JsonUtility.ToJson(wrapper, true);
        File.WriteAllText(fullPath, newJson);

        Debug.Log($"[TimerPanel] Saved timer to {fullPath}. Total timers: {wrapper.timers.Count}");
    }


    private DeviceData LoadDeviceById(string id)
    {
        string path = Path.Combine(Application.persistentDataPath, "device_list.json");
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        var list = JsonUtility.FromJson<DeviceRegistryManager.DeviceListWrapper>(json);
        return list.devices.FirstOrDefault(d => d.deviceId == id);
    }

    public void LoadDevicePanel(DeviceData device)
    {
        ClearPanel();
        currentDevices.Add(device);
        CreateDeviceItem(device);
    }

    public void LoadGroupPanel(GroupData group, List<DeviceData> devices)
    {
        ClearPanel();
        currentGroup = group;
        currentDevices.AddRange(devices);

        foreach (var device in devices)
        {
            CreateDeviceItem(device);
        }
    }

    private void ClearPanel()
    {
        currentDevices.Clear();
        currentGroup = null;

        foreach (Transform child in deviceContainer)
            Destroy(child.gameObject);
    }
}

