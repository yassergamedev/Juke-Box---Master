using UnityEngine;
using System.Linq;
using UnityEngine.UI;
using System.Collections.Generic;
[System.Serializable]


public class DeviceInstantiater : MonoBehaviour
{

    [Header("Popup UI References")]
    public PopupCommandsUI fanPopupUI;
    public PopupCommandsUI rgbPopupUI;
    public PopupCommandsUI switchPopupUI;
    public PopupCommandsUI lightPopupUI;
    public PopupCommandsUI unknownPopupUI;

    public PopupTuyaCommandsUI fanPopupTuyaUI;
    public PopupTuyaCommandsUI rgbPopupTuyaUI;
    public PopupTuyaCommandsUI switchPopupTuyaUI;
    public PopupTuyaCommandsUI lightPopupTuyaUI;
    public PopupTuyaCommandsUI unknownPopupTuyaUI;


    [Header("References")]
    public DeviceRegistryManager registryManager;
    public Transform deviceContainer;
    public GameObject localDevicePrefab;
    public GameObject cloudDevicePrefab;
    public Button SaveButton;
    [Header("Settings")]
    public bool loadOnStart = true;
    public bool onlyWithIcons = true;

    private void Start()
    {
        if (loadOnStart)
        {
            InitializeDevices();
        }
    }

    public void InitializeDevices()
    {
        if (registryManager == null)
        {
            Debug.LogError("RegistryManager reference not set!");
            return;
        }

        if (!registryManager.TryLoadDevices(out var devices))
        {
            Debug.LogWarning("No devices loaded from registry");
            return;
        }

        LoadDevices(devices);
    }
    public void LoadDevices(List<DeviceRegistryManager.DeviceData> devices)
    {
        // Clear existing devices
        foreach (Transform child in deviceContainer)
        {
            Destroy(child.gameObject);
        }

        // Filter devices if needed
        var devicesToLoad = onlyWithIcons
            ? devices.Where(d => !string.IsNullOrEmpty(d.iconPath)).ToList()
            : devices;

        foreach (var deviceData in devicesToLoad)
        {
            InstantiateDevice(deviceData);
        }

        Debug.Log($"Instantiated {devicesToLoad.Count} devices (onlyWithIcons: {onlyWithIcons})");
    }
    private PopupCommandsUI GetLocalPopup(DeviceType type)
    {
        return type switch
        {
            DeviceType.Fan => fanPopupUI,
            DeviceType.RGB => rgbPopupUI,
            DeviceType.Switch => switchPopupUI,
            DeviceType.Light => lightPopupUI,
            _ => unknownPopupUI,
        };
    }

    private PopupTuyaCommandsUI GetCloudPopup(DeviceType type)
    {
        return type switch
        {
            DeviceType.Fan => fanPopupTuyaUI,
            DeviceType.RGB => rgbPopupTuyaUI,
            DeviceType.Switch => switchPopupTuyaUI,
            DeviceType.Light => lightPopupTuyaUI,
            _ => unknownPopupTuyaUI,
        };
    }

    private void InstantiateDevice(DeviceRegistryManager.DeviceData deviceData)
    {
        // Choose the correct prefab
        GameObject prefab = deviceData.mode == "local" ? localDevicePrefab : cloudDevicePrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"No prefab found for mode: {deviceData.mode}");
            return;
        }

        // Instantiate device
        GameObject device = Instantiate(prefab, deviceContainer);
        device.name = deviceData.device_name;

        // Set position and size
        var rt = device.GetComponent<RectTransform>();
        rt.anchoredPosition = deviceData.position;
        rt.sizeDelta = deviceData.size;

        // Setup controller
        if (deviceData.mode == "local")
        {
            var controller = device.GetComponentInChildren<LocalTuyaController>();
            if (controller != null)
            {
                controller.device_name = deviceData.device_name;
                controller.deviceId = deviceData.deviceId;
                controller.deviceKey = deviceData.deviceKey;
                controller.deviceIp = deviceData.deviceIp;
                controller.version = deviceData.version;
                controller.deviceType = deviceData.type;
            }
            var dropdownHandler = device.GetComponentInChildren<DropdownCommandHandler>();
            if (dropdownHandler != null)
            {
                dropdownHandler.popupScript = GetLocalPopup(deviceData.type);
                Debug.Log($"Assigned local popup for {deviceData.device_name}: {deviceData.type}");
            }
            else
            {
                Debug.LogWarning($"DropdownCommandHandler missing on local device {deviceData.device_name}");
            }
        }
        else
        {
            var controller = device.GetComponentInChildren<TuyaController>();
            if (controller != null)
            {
                controller.device_name = deviceData.device_name;
                controller.deviceId = deviceData.deviceId;
                controller.deviceType = deviceData.type;

                controller.popupScript = rgbPopupTuyaUI; 
                Debug.Log($"[TuyaController] Assigned popupScript to {rgbPopupTuyaUI} for {deviceData.device_name}");
            }
            else
            {
                Debug.LogWarning($"TuyaController not found on device: {deviceData.device_name}");
            }


        }
     
        // Handle icon
        var iconManager = device.GetComponent<DeviceIconManager>();
        if (iconManager != null)
        {
            if (!string.IsNullOrEmpty(deviceData.iconPath))
            {
                iconManager.LoadIcon(deviceData.iconPath);
            }
           /* else
            {
                // Fallback to type-based icon if no custom icon
                var image = device.GetComponentInChildren<Image>();
                if (image != null)
                {
                    image.sprite = registryManager.GetIcon(deviceData.type);
                }
            }*/
        }

        // Add dragger if needed
        if (!device.TryGetComponent<DeviceDragger>(out _))
        {
            var dragger = device.AddComponent<DeviceDragger>();
            dragger.savedPosition = deviceData.position;
            SaveButton.gameObject.SetActive(true);
        }
    }

   
}