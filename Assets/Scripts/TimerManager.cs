using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using static DeviceRegistryManager;

public class TimerManager : MonoBehaviour
{
    public GroupCreator groupManager;
    public DeviceRegistryManager deviceManager;
    public TimerPanelManager timerPanelManager;
    public TMP_Dropdown selectionDropdown;
    public TMP_Text selectedText;

    private List<string> allOptions = new();
    private List<string> groupNames = new();
    private List<string> deviceIds = new();
    private Dictionary<string, string> deviceNameMap = new(); // deviceId → name

    private void Start()
    {
        PopulateDropdown();
    }
    private void PopulateDropdown()
    {
        allOptions.Clear();
        selectionDropdown.ClearOptions();

        groupNames = groupManager.GetGroups().Select(g => g.groupName).ToList();
        var devices = deviceManager.GetDevices(); // You’ll need to expose this

        foreach (var d in devices)
            deviceNameMap[d.deviceId] = d.device_name;

        deviceIds = devices.Select(d => d.deviceId).ToList();

        // Add groups first
        allOptions.AddRange(groupNames.Select(g => $"[Group] {g}"));

        // Then devices
        allOptions.AddRange(devices.Select(d => $"[Device] {d.device_name}"));

        selectionDropdown.AddOptions(allOptions);

        selectionDropdown.onValueChanged.RemoveAllListeners();
        selectionDropdown.onValueChanged.AddListener(OnDropdownChanged);
    }
    private void OnDropdownChanged(int index)
    {
        string selected = allOptions[index];

        if (selected.StartsWith("[Group] "))
        {
            string groupName = selected.Replace("[Group] ", "");
            selectedText.text = $"{groupName} (Group)";
            DisplayTimersForGroup(groupName);

            var group = groupManager.GetGroups().FirstOrDefault(g => g.groupName == groupName);
            if (group != null)
                timerPanelManager.LoadTimerPanel(null, group);
        }
        else if (selected.StartsWith("[Device] "))
        {
            string name = selected.Replace("[Device] ", "");
            string id = deviceNameMap.FirstOrDefault(pair => pair.Value == name).Key;
            selectedText.text = $"{name} (Device)";
            DisplayTimersForDevice(id);

            var device = deviceManager.GetDevices().FirstOrDefault(d => d.deviceId == id);
            if (device != null)
                timerPanelManager.LoadTimerPanel(device, null);
        }
    }

    public GameObject timerPrefab;
    public Transform timerContainer;

    private TimerListWrapper LoadAllTimers()
    {
        string fullPath = Path.Combine(Application.persistentDataPath, "timer_list.json");
        if (!File.Exists(fullPath)) return new TimerListWrapper();
        string json = File.ReadAllText(fullPath);
        return JsonUtility.FromJson<TimerListWrapper>(json);
    }

    private void DisplayTimersForGroup(string groupName)
    {
        ClearTimers();
        var wrapper = LoadAllTimers();
        var groupTimers = wrapper.timers.Where(t => t.groupName == groupName).ToList();

        foreach (var timer in groupTimers)
        {
            GameObject item = Instantiate(timerPrefab, timerContainer);
            TimerUI ui = item.GetComponent<TimerUI>();
            ui.Setup(timer, () =>
            {
                wrapper.timers.Remove(timer);
                SaveTimers(wrapper);
                Destroy(item);
            });
        }
    }

    private void DisplayTimersForDevice(string deviceId)
    {
        ClearTimers();
        var wrapper = LoadAllTimers();
        var deviceTimers = wrapper.timers.Where(t => t.deviceId == deviceId).ToList();

        foreach (var timer in deviceTimers)
        {
            GameObject item = Instantiate(timerPrefab, timerContainer);
            TimerUI ui = item.GetComponent<TimerUI>();
            ui.Setup(timer, () =>
            {
                wrapper.timers.Remove(timer);
                SaveTimers(wrapper);
                Destroy(item);
            });
        }
    }
    public void SetCurrentTarget(string targetIdOrName, bool isGroup)
    {
        if (isGroup)
        {
            string fullName = $"[Group] {targetIdOrName}";
            int index = allOptions.IndexOf(fullName);
            if (index != -1)
            {
                selectionDropdown.value = index;
                selectionDropdown.RefreshShownValue();
                selectedText.text = $"{targetIdOrName} (Group)";
                DisplayTimersForGroup(targetIdOrName);
            }
            else
            {
                Debug.LogWarning($"[TimerManager] Group '{targetIdOrName}' not found in dropdown.");
            }
        }
        else
        {
            string fullName = $"[Device] {deviceNameMap.GetValueOrDefault(targetIdOrName)}";
            int index = allOptions.IndexOf(fullName);
            if (index != -1)
            {
                selectionDropdown.value = index;
                selectionDropdown.RefreshShownValue();
                selectedText.text = $"{deviceNameMap[targetIdOrName]} (Device)";
                DisplayTimersForDevice(targetIdOrName);
            }
            else
            {
                Debug.LogWarning($"[TimerManager] Device ID '{targetIdOrName}' not found in dropdown.");
            }
        }
    }
    private void ClearTimers()
    {
        foreach (Transform child in timerContainer)
            Destroy(child.gameObject);
    }

    private void SaveTimers(TimerListWrapper wrapper)
    {
        string json = JsonUtility.ToJson(wrapper, true);
        File.WriteAllText(Path.Combine(Application.persistentDataPath, "timer_list.json"), json);
    }
    private GroupData GetGroupByName(string groupName)
    {
        string groupPath = Path.Combine(Application.persistentDataPath, "group_list.json");
        if (!File.Exists(groupPath)) return null;

        string json = File.ReadAllText(groupPath);
        var wrapper = JsonUtility.FromJson<GroupListWrapper>(json);
        return wrapper.groups.FirstOrDefault(g => g.groupName == groupName);
    }

    private List<DeviceData> GetDevicesByIds(List<string> deviceIds)
    {
        string devicePath = Path.Combine(Application.persistentDataPath, "device_list.json");
        if (!File.Exists(devicePath)) return new List<DeviceData>();

        string json = File.ReadAllText(devicePath);
        var wrapper = JsonUtility.FromJson<DeviceListWrapper>(json);

        if (wrapper.devices == null) return new List<DeviceData>();

        return wrapper.devices.Where(d => deviceIds.Contains(d.deviceId)).ToList();
    }
}
