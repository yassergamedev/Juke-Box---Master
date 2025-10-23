using UnityEngine;
using SFB;
using System.IO;


public class DeviceLoader : MonoBehaviour
{
    public void LoadDevicesJson()
    {
        var extensions = new[] { new ExtensionFilter("JSON Files", "json") };
        string[] paths = StandaloneFileBrowser.OpenFilePanel("Select Devices JSON", "", extensions, false);

        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            string jsonContent = File.ReadAllText(paths[0]);
            DeviceInfoListWrapper deviceList = JsonUtility.FromJson<DeviceInfoListWrapper>(jsonContent);

            if (deviceList != null && deviceList.devices != null)
            {
                UpdateControllers(deviceList.devices);
            }
            else
            {
                Debug.LogError("Failed to parse device list.");
            }
        }
    }

    private void UpdateControllers(DeviceInfo[] devices)
    {
        LocalTuyaController[] controllers = FindObjectsOfType<LocalTuyaController>();

        foreach (LocalTuyaController controller in controllers)
        {
            foreach (DeviceInfo device in devices)
            {
                if (controller.deviceId == device.id)
                {
                    controller.deviceIp = device.ip;
                    controller.version = string.IsNullOrEmpty(device.version) ? controller.version : device.version;

                    controller.SaveIp();  // Save updated IP to PlayerPrefs

                    Debug.Log($"Updated {controller.name} with IP: {device.ip} and Version: {controller.version}");
                    break;
                }
            }
        }
    }
}
[System.Serializable]
public class DeviceInfo
{
    public string name;
    public string id;
    public string key;
    public string mac;
    public string uuid;
    public string sn;
    public string category;
    public string product_name;
    public string product_id;
    public int biz_type;
    public string model;
    public bool sub;
    public string icon;
    public string ip;
    public string version;
}

[System.Serializable]
public class DeviceInfoListWrapper
{
    public DeviceInfo[] devices;
}
