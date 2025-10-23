using UnityEngine;
using SFB;
using System.IO;
using System.Collections.Generic;

public class DeviceLoading : MonoBehaviour
{
    public List<GameObject> deviceGameObjects = new List<GameObject>();

    private void Start()
    {
        UpdateJsonWithGameObjectData();
    }
    [System.Serializable]
    public class DeviceDataListWrapper
    {
        public List<DeviceRegistryManager.DeviceData> devices = new List<DeviceRegistryManager.DeviceData>();
    }

    // Main method: Load JSON, update with GameObject data, then save
    public void UpdateJsonWithGameObjectData()
    {
        // Step 1: Load the existing device_list.json
        string loadedJson = LoadDeviceListJson();
        if (string.IsNullOrEmpty(loadedJson))
        {
            Debug.LogError("Failed to load device_list.json");
            return;
        }

        DeviceDataListWrapper deviceList = JsonUtility.FromJson<DeviceDataListWrapper>(loadedJson);
        if (deviceList == null || deviceList.devices == null)
        {
            Debug.LogError("Failed to parse device_list.json");
            return;
        }

        // Step 2: Match JSON devices with GameObjects and update JSON data
        foreach (DeviceRegistryManager.DeviceData jsonDevice in deviceList.devices)
        {
            GameObject matchingGO = FindMatchingGameObject(jsonDevice.deviceId);
            if (matchingGO != null)
            {
                UpdateJsonDeviceFromGameObject(jsonDevice, matchingGO);
            }
            else
            {
                Debug.LogWarning($"No GameObject found for device ID: {jsonDevice.deviceId}");
            }
        }

        // Step 3: Save the updated JSON
        string updatedJson = JsonUtility.ToJson(deviceList, true);
        string savePath = StandaloneFileBrowser.SaveFilePanel(
            "Save Updated Device List",
            "",
            "updated_device_list.json",
            "json"
        );

        if (!string.IsNullOrEmpty(savePath))
        {
            File.WriteAllText(savePath, updatedJson);
            Debug.Log($"Updated JSON saved to: {savePath}");
        }
    }

    // Helper: Load device_list.json from a file dialog
    private string LoadDeviceListJson()
    {
        var extensions = new[] { new ExtensionFilter("JSON Files", "json") };
        string[] paths = StandaloneFileBrowser.OpenFilePanel("Select device_list.json", "", extensions, false);

        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            return File.ReadAllText(paths[0]);
        }
        return null;
    }

    // Helper: Find a GameObject by deviceId
    private GameObject FindMatchingGameObject(string deviceId)
    {
        foreach (GameObject deviceGO in deviceGameObjects)
        {
            TuyaController tuyaController = deviceGO.GetComponentInChildren<TuyaController>();
            LocalTuyaController localTuyaController = deviceGO.GetComponentInChildren<LocalTuyaController>();

            string goDeviceId = tuyaController?.deviceId ?? localTuyaController?.deviceId;
            if (goDeviceId == deviceId)
            {
                return deviceGO;
            }
        }
        return null;
    }

    // Helper: Update JSON device data from GameObject
    private void UpdateJsonDeviceFromGameObject(DeviceRegistryManager.DeviceData jsonDevice, GameObject deviceGO)
    {
        // Update position & size from RectTransform
        RectTransform rect = deviceGO.GetComponent<RectTransform>();
        if (rect != null)
        {
            jsonDevice.position = rect.anchoredPosition;
            jsonDevice.size = rect.sizeDelta;
        }

        // Update icon path from DeviceIconManager
        DeviceIconManager iconManager = deviceGO.GetComponent<DeviceIconManager>();
        if (iconManager != null)
        {
            jsonDevice.iconPath = iconManager.iconPath;
        }

        Debug.Log($"Updated JSON data for device: {jsonDevice.device_name} (ID: {jsonDevice.deviceId})");
    }
}