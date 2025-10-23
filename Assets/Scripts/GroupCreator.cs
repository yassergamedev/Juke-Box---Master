using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static DeviceRegistryManager;
[System.Serializable]
public class GroupData
{
    public string groupName;
    public List<string> deviceIds = new(); 
}

[System.Serializable]
public class GroupListWrapper
{
    public List<GroupData> groups = new();
}

public class DeviceListWrapper
{
    public List<DeviceData> devices = new List<DeviceData>();
}

public class GroupCreator : MonoBehaviour
{
    [Header("Device Toggles")]
    public Transform deviceToggleContainer; 
    public TMP_InputField groupNameInput;
    public Button createGroupButton;
    [Header("Result Text")]
    public TMP_Text resultText;

    private const string GROUP_SAVE_FILE = "group_list.json";

    private List<GroupData> allGroups = new();

    private void Start()
    {
        createGroupButton.onClick.AddListener(CreateGroup);
        LoadGroups(); 
    }

    public void CreateGroup()
    {
        string groupName = groupNameInput.text.Trim();
        if (string.IsNullOrEmpty(groupName))
        {
            ShowResult("Group name is empty.", Color.yellow);
            return;
        }

        List<string> selectedDeviceIds = new();

        foreach (Transform child in deviceToggleContainer)
        {
            DeviceItemUI item = child.GetComponent<DeviceItemUI>();
            if (item != null && item.IsSelected())
            {
                selectedDeviceIds.Add(item.GetDeviceId());
            }
        }

        if (selectedDeviceIds.Count == 0)
        {
            ShowResult(" No devices selected.", Color.yellow);
            return;
        }

        if (allGroups.Any(g => g.groupName == groupName))
        {
            ShowResult($" Group '{groupName}' already exists.", Color.red);
            return;
        }

        GroupData newGroup = new GroupData
        {
            groupName = groupName,
            deviceIds = selectedDeviceIds
        };

        allGroups.Add(newGroup);
        SaveGroups();

        ShowResult($" Group '{groupName}' created with {selectedDeviceIds.Count} devices.", Color.green);

        foreach (Transform child in deviceToggleContainer)
        {
            DeviceItemUI item = child.GetComponent<DeviceItemUI>();
            if (item != null)
                item.selectToggle.isOn = false;
        }

        groupNameInput.text = "";
    }

    private void ShowResult(string message, Color color)
    {
        if (resultText == null) return;

        resultText.text = message;
        resultText.color = color;
    }

    private void SaveGroups()
    {
        string json = JsonUtility.ToJson(new GroupListWrapper { groups = allGroups }, true);
        File.WriteAllText(Path.Combine(Application.persistentDataPath, GROUP_SAVE_FILE), json);
    }

    private void LoadGroups()
    {
        string fullPath = Path.Combine(Application.persistentDataPath, GROUP_SAVE_FILE);
        if (!File.Exists(fullPath)) return;

        string json = File.ReadAllText(fullPath);
        allGroups = JsonUtility.FromJson<GroupListWrapper>(json).groups;
    }

    public List<GroupData> GetGroups() => allGroups;
}
