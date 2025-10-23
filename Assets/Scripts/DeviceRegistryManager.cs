using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class DeviceRegistryManager : MonoBehaviour
{
    [Header("Device Display")]
    public Transform deviceContainer;
    public GameObject deviceItemPrefab;
    public Sprite fanIcon, rgbIcon, switchIcon, lightIcon, unknownIcon;

    private const string SAVE_FILE = "device_list.json";

    [System.Serializable]

    public class DeviceData
    {
        public string device_name;
        public string deviceId;
        public string deviceKey;
        public string deviceIp;
        public string version;
        public string mode;
        public DeviceType type;

        public Vector2 position;
        public Vector2 size;
        public string iconPath; 
    }

    [System.Serializable]
    public class DeviceListWrapper
    {
        public List<DeviceData> devices = new List<DeviceData>();
    }

    private DeviceListWrapper cachedDevices = new(); // Cache loaded devices in memory

    private void Start()
    {
        LoadAndDisplayDevices();
    }

    public void ScanAndSaveDevices()
    {
        Debug.Log("[DeviceRegistry] Scanning devices...");
        DeviceListWrapper allDevices = new DeviceListWrapper();

        foreach (LocalTuyaController local in FindObjectsOfType<LocalTuyaController>())
        {
            var existingData = GetDeviceById(local.deviceId);

            allDevices.devices.Add(new DeviceData
            {
                device_name = local.device_name,
                deviceId = local.deviceId,
                deviceKey = local.deviceKey,
                deviceIp = local.deviceIp,
                version = local.version,
                mode = "local",
                type = local.deviceType,
                position = existingData?.position ?? Vector2.zero,
                size = existingData?.size ?? new Vector2(100, 100),
                iconPath = existingData?.iconPath // Preserve existing icon
            });
            Debug.Log($"Added local device: {local.device_name}");
        }

        foreach (TuyaController cloud in FindObjectsOfType<TuyaController>())
        {
            var existingData = GetDeviceById(cloud.deviceId);

            allDevices.devices.Add(new DeviceData
            {
                device_name = cloud.device_name,
                deviceId = cloud.deviceId,
                mode = "cloud",
                type = existingData?.type ?? DeviceType.Unknown,
                position = existingData?.position ?? Vector2.zero,
                size = existingData?.size ?? new Vector2(100, 100),
                iconPath = existingData?.iconPath // Preserve existing icon
            });
            Debug.Log($"Added cloud device: {cloud.device_name}");
        }

        string savePath = Path.Combine(Application.persistentDataPath, SAVE_FILE);
        string json = JsonUtility.ToJson(allDevices, true);
        File.WriteAllText(savePath, json);

        Debug.Log($"Saved {allDevices.devices.Count} devices to: {savePath}");
        cachedDevices = allDevices;
        DisplayDevices(cachedDevices);
    }

    public void LoadAndDisplayDevices()
    {
        string fullPath = Path.Combine(Application.persistentDataPath, SAVE_FILE);
        if (!File.Exists(fullPath))
        {
            Debug.LogWarning($"No save file found at {fullPath}");
            return;
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            cachedDevices = JsonUtility.FromJson<DeviceListWrapper>(json);
            Debug.Log($"Loaded {cachedDevices.devices.Count} devices from save file");

            DisplayDevices(cachedDevices);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load devices: {e.Message}");
        }
    }

    private void DisplayDevices(DeviceListWrapper wrapper)
    {
        if (deviceContainer == null)
        {
            Debug.LogError("Device container not assigned!");
            return;
        }

        if (deviceItemPrefab == null)
        {
            Debug.LogError("Device item prefab not assigned!");
            return;
        }

        Debug.Log($"Displaying {wrapper.devices.Count} devices");

        // Clear existing devices
        foreach (Transform child in deviceContainer)
        {
            Destroy(child.gameObject);
        }

        foreach (var dev in wrapper.devices)
        {
            try
            {
                if (dev == null) continue;

                GameObject item = Instantiate(deviceItemPrefab, deviceContainer);
                if (item == null)
                {
                    Debug.LogWarning("Failed to instantiate device item");
                    continue;
                }

                // Set basic properties
                item.name = dev.device_name;

                // Set position and size
                var rt = item.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchoredPosition = dev.position;
                    rt.sizeDelta = dev.size;
                }

                // Set up UI
                DeviceItemUI ui = item.GetComponent<DeviceItemUI>();
                if (ui != null)
                {
                    Sprite icon = !string.IsNullOrEmpty(dev.iconPath) ?
                        LoadIconFromPath(dev.iconPath) :
                        GetIcon(dev.type);

                    ui.Setup(dev.device_name, dev.deviceId, icon);
                }

                // Add dragger component
                var dragger = item.GetComponent<DeviceDragger>();
                if (dragger != null)
                {
                    dragger.savedPosition = dev.position;
                }

                
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to display device: {e.Message}");
            }
        }
    }
    public bool TryLoadDevices(out List<DeviceData> devices)
    {
        devices = new List<DeviceData>();
        string fullPath = Path.Combine(Application.persistentDataPath, SAVE_FILE);

        if (!File.Exists(fullPath))
        {
            Debug.LogWarning("No device data file found");
            return false;
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            var wrapper = JsonUtility.FromJson<DeviceListWrapper>(json);
            devices = wrapper.devices;
            cachedDevices = wrapper;
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load devices: {e.Message}");
            return false;
        }
    }
    private DeviceType InferDeviceType(string name)
    {
        string lower = name.ToLower();
        if (lower.Contains("fan")) return DeviceType.Fan;
        if (lower.Contains("fan light")) return DeviceType.Light;
        if (lower.Contains("rgb")) return DeviceType.RGB;
        if (lower.Contains("light")) return DeviceType.Light;
        if (lower.Contains("switch")) return DeviceType.Switch;
        return DeviceType.Unknown;
    }

    private Sprite GetIcon(DeviceType type)
    {
        return type switch
        {
            DeviceType.Fan => fanIcon,
            DeviceType.RGB => rgbIcon,
            DeviceType.Switch => switchIcon,
            DeviceType.Light => lightIcon,
            _ => unknownIcon
        };
    }

    public List<DeviceData> GetDevices()
    {
        return cachedDevices?.devices ?? new List<DeviceData>();
    }
    public DeviceData GetDeviceById(string deviceId)
    {
        return cachedDevices?.devices?.FirstOrDefault(d => d.deviceId == deviceId);
    }

    public void SaveDevice(DeviceData deviceData)
    {
        // Remove existing if present
        cachedDevices.devices.RemoveAll(d => d.deviceId == deviceData.deviceId);

        // Add updated data
        cachedDevices.devices.Add(deviceData);

        // Save to file
        string json = JsonUtility.ToJson(cachedDevices, true);
        File.WriteAllText(Path.Combine(Application.persistentDataPath, SAVE_FILE), json);
    }

    // Modified to handle positions and icons
   

    private Sprite LoadIconFromPath(string path)
    {
        // Handle Resources-loaded icons
        if (path.Contains("Resources/"))
        {
            string resourcePath = path.Split(new[] { "Resources/" }, System.StringSplitOptions.None)[1]
                                .Replace(Path.GetExtension(path), "");
            return Resources.Load<Sprite>(resourcePath);
        }

        // Handle direct file paths
        if (File.Exists(path))
        {
            byte[] fileData = File.ReadAllBytes(path);
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(fileData);
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f);
        }

        return unknownIcon;
    }
}
