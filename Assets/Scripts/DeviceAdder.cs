using UnityEngine;
using TMPro;
using UnityEngine.UI;
using SFB;
using System.IO;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.EventSystems;

public class DeviceAdder : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Dropdown deviceTypeDropdown;
    public TMP_Dropdown modeDropdown;
    public TMP_Dropdown versionDropdown;
    public TMP_InputField nameInput;
    public TMP_InputField idInput;
    public TMP_InputField keyInput;
    public TMP_InputField ipInput;
    public Button addButton;
    public Button browseIconButton;
    public Image iconPreview;
    public GameObject addDeviceWindow;

    [Header("Prefab References")]
    public GameObject localDevicePrefab;
    public GameObject cloudDevicePrefab;
    public Transform deviceHubParent;

    private Texture2D selectedIcon;
    private string iconPath;

    private void Start()
    {
        // Initialize dropdowns
        versionDropdown.ClearOptions();
        versionDropdown.AddOptions(new List<string> { "3.3", "3.5" });

        // Set up button listeners
        addButton.onClick.AddListener(AddDevice);
        browseIconButton.onClick.AddListener(BrowseForIcon);
    }

    private void BrowseForIcon()
    {
        var extensions = new[] {
            new ExtensionFilter("Image Files", "png", "jpg", "jpeg")
        };

        var paths = StandaloneFileBrowser.OpenFilePanel("Select Device Icon", "", extensions, false);
        if (paths.Length > 0)
        {
            iconPath = paths[0];
            LoadIcon(iconPath);
        }
    }

    private void LoadIcon(string path)
    {
        byte[] fileData = File.ReadAllBytes(path);
        selectedIcon = new Texture2D(2, 2);
        selectedIcon.LoadImage(fileData);

        // Create sprite and display in preview
        Sprite iconSprite = Sprite.Create(
            selectedIcon,
            new Rect(0, 0, selectedIcon.width, selectedIcon.height),
            new Vector2(0.5f, 0.5f)
        );

        iconPreview.sprite = iconSprite;
        iconPreview.gameObject.SetActive(true);
    }

    private void AddDevice()
    {
        // Validate inputs
        if (string.IsNullOrEmpty(nameInput.text) ||
            string.IsNullOrEmpty(idInput.text) ||
            (modeDropdown.value == 0 && string.IsNullOrEmpty(ipInput.text)))
        {
            Debug.LogWarning("Please fill all required fields");
            return;
        }

        // Create device data
        DeviceRegistryManager.DeviceData newDevice = new DeviceRegistryManager.DeviceData
        {
            device_name = nameInput.text,
            deviceId = idInput.text,
            deviceKey = keyInput.text,
            deviceIp = ipInput.text,
            version = versionDropdown.options[versionDropdown.value].text,
            mode = modeDropdown.value == 0 ? "local" : "cloud",
            type = (DeviceType)deviceTypeDropdown.value,
            iconPath = iconPath // Add the icon path to the device data
        };

        // Add to registry
        AddDeviceToRegistry(newDevice);

        // Create visual representation in hub
        CreateDeviceInHub(newDevice);

        // Reset form and close window
        ResetForm();
        addDeviceWindow.SetActive(false);
    }


    private void AddDeviceToRegistry(DeviceRegistryManager.DeviceData device)
    {
        string path = Path.Combine(Application.persistentDataPath, "device_list.json");
        DeviceRegistryManager.DeviceListWrapper wrapper;

        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            wrapper = JsonUtility.FromJson<DeviceRegistryManager.DeviceListWrapper>(json);
        }
        else
        {
            wrapper = new DeviceRegistryManager.DeviceListWrapper();
        }

        // Check if device already exists
        if (wrapper.devices.Any(d => d.deviceId == device.deviceId))
        {
            Debug.LogWarning($"Device with ID {device.deviceId} already exists");
            return;
        }

        wrapper.devices.Add(device);
        string newJson = JsonUtility.ToJson(wrapper, true);
        File.WriteAllText(path, newJson);

        Debug.Log($"Device {device.device_name} added to registry");
    }

    private void CreateDeviceInHub(DeviceRegistryManager.DeviceData device)
    {
        GameObject prefabToUse = device.mode == "local" ? localDevicePrefab : cloudDevicePrefab;
        GameObject newDevice = Instantiate(prefabToUse, deviceHubParent);

        // Position randomly in hub area
        newDevice.transform.localPosition = new Vector3(
            Random.Range(-200f, 200f),
            Random.Range(-100f, 100f),
            0
        );

        // Set up controller
        if (device.mode == "local")
        {
            LocalTuyaController controller = newDevice.GetComponentInChildren<LocalTuyaController>();
            controller.device_name = device.device_name;
            controller.deviceId = device.deviceId;
            controller.deviceKey = device.deviceKey;
            controller.deviceIp = device.deviceIp;
            controller.version = device.version;
            controller.deviceType = device.type;
        }
        else
        {
            TuyaController controller = newDevice.GetComponentInChildren<TuyaController>();
            controller.device_name = device.device_name;
            controller.deviceId = device.deviceId;
            controller.deviceType = device.type;
        }

        // Set icon if available
        if (!string.IsNullOrEmpty(device.iconPath))
        {
            var iconManager = newDevice.GetComponentInChildren<DeviceIconManager>();
            if (iconManager != null)
            {
                iconManager.LoadIcon(device.iconPath);
            }
            else
            {
                Image deviceImage = newDevice.GetComponentInChildren<Image>();
                if (deviceImage != null && iconPreview.sprite != null)
                {
                    deviceImage.sprite = iconPreview.sprite;
                }
            }
        }

        // Add draggable component
        var dragger = newDevice.AddComponent<DeviceDragger>();
        dragger.savedPosition = newDevice.transform.localPosition;
    }


    private void ResetForm()
    {
        nameInput.text = "";
        idInput.text = "";
        keyInput.text = "";
        ipInput.text = "";
        deviceTypeDropdown.value = 0;
        modeDropdown.value = 0;
        versionDropdown.value = 0;
        iconPreview.sprite = null;
        iconPreview.gameObject.SetActive(false);
        selectedIcon = null;
        iconPath = "";
    }

}