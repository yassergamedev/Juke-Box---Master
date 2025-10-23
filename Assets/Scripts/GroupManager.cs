using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class GroupManager : MonoBehaviour
{
    [Header("Group Display")]
    public Transform groupListContainer;
    public GameObject groupItemPrefab;

    private const string GROUP_FILE = "group_list.json";
    private List<GroupData> loadedGroups;

    private void Start()
    {
        LoadAndDisplayGroups();
    }
    public void LoadAndDisplayGroups()
    {
        foreach (Transform child in groupListContainer)
        {
            Destroy(child.gameObject);
        }

        string path = Path.Combine(Application.persistentDataPath, GROUP_FILE);

        if (!File.Exists(path))
        {
            return;
        }

        string json = File.ReadAllText(path);

        GroupListWrapper wrapper;
        try
        {
            wrapper = JsonUtility.FromJson<GroupListWrapper>(json);
        }
        catch (System.Exception ex)
        {
            Debug.Log(ex);
            return;
        }

        if (wrapper == null || wrapper.groups == null)
        {
            return;
        }

        loadedGroups = wrapper.groups;

        foreach (var group in loadedGroups)
        {
            GameObject gItem = Instantiate(groupItemPrefab, groupListContainer);
            GroupItemUI ui = gItem.GetComponent<GroupItemUI>();

            if (ui == null)
            {
                continue;
            }

            ui.Setup(group.groupName, TurnGroupOn, TurnGroupOff);
        }
    }


    private void TurnGroupOn(string groupName)
    {
        Debug.Log($"[GroupManager] Turning ON group: {groupName}");
        TriggerGroup(groupName, true);
    }

    private void TurnGroupOff(string groupName)
    {
        Debug.Log($"[GroupManager] Turning OFF group: {groupName}");
        TriggerGroup(groupName, false);
    }

    private void TriggerGroup(string groupName, bool turnOn)
    {
        GroupData group = loadedGroups.Find(g => g.groupName == groupName);
        if (group == null)
        {
            Debug.LogWarning($"[GroupManager] Group not found: {groupName}");
            return;
        }

        foreach (string deviceId in group.deviceIds)
        {
            Debug.Log($"[GroupManager] Sending command to device: {deviceId} ({(turnOn ? "ON" : "OFF")})");
            TriggerDeviceById(deviceId, turnOn);
        }
    }

    private void TriggerDeviceById(string id, bool turnOn)
    {
        LocalTuyaController[] locals = FindObjectsOfType<LocalTuyaController>();
        foreach (var device in locals)
        {
            if (device.deviceId == id)
            {

                DropdownCommandHandler cmdHandler = device.GetComponent<DropdownCommandHandler>();
                if (cmdHandler != null && cmdHandler.tuyaCommands.Length >= 2)
                {
                    int index = turnOn ? 0 : 1;
                    var cmd = cmdHandler.tuyaCommands[index];
                    device.SendLocalCommand(cmd.command, cmd.dps, cmd.value);
                }
                else
                {
                    if (turnOn) device.TurnOn();
                    else device.TurnOff();
                }

                return;
            }
        }

        TuyaController[] clouds = FindObjectsOfType<TuyaController>();
        foreach (var device in clouds)
        {
            if (device.deviceId == id)
            {
                Debug.Log($"[GroupManager] Found cloud device: {id}");
                string cmd = turnOn ? "turn_on" : "turn_off";
                string val = turnOn ? "true" : "false";
                device.SendCommand(cmd, val);
                return;
            }
        }

        Debug.LogWarning($"[GroupManager] Device with ID {id} not found in scene.");
    }

}
