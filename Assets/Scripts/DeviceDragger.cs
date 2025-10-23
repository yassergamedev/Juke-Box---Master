using System.IO;
using System.Xml.Linq;
using TMPro;
using UnityEditor;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class DeviceDragger : MonoBehaviour, IDragHandler, IPointerDownHandler
{
    [Header("Settings")]
    public float resizeSpeed = 0.1f;
    public float minSize = 0.5f;
    public float maxSize = 2f;
    public KeyCode saveKey = KeyCode.Return;
    public KeyCode lockKey = KeyCode.L;

    [Header("Drag Control")]
    public bool allowDragging = false; // <-- NEW: Toggle dragging on/off

    [Header("Text Display")]
    public bool showText = true;
    public Vector2 textOffset = new Vector2(0, -50f);
    public Vector2 textSize = new Vector2(200f, 50f);
    public int fontSize = 14;

    [Header("State")]
    public bool isLocked = false;
    public Vector2 savedPosition;
    public string iconPath;

    private RectTransform rectTransform;
    private GameObject textLabel;
    private TMP_Text textComponent;
    private bool isSelected = false;
    private Vector2 originalSize;
    private Image deviceImage;
    private DeviceIconManager iconManager;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        deviceImage = GetComponent<Image>();
        iconManager = GetComponent<DeviceIconManager>();

        // Load saved position if locked
        if (isLocked)
        {
            rectTransform.anchoredPosition = savedPosition;
        }
        GlobalDragController.Instance.RegisterDevice(this);
    }
  
    // --- DRAG HANDLING (Modified) ---
    public void OnPointerDown(PointerEventData eventData)
    {
        if (!allowDragging) return; // <-- Skip if dragging is disabled
        isSelected = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!allowDragging || isLocked) return; // <-- Skip if dragging is disabled or locked

        if (eventData.pointerDrag == gameObject)
        {
            rectTransform.anchoredPosition += eventData.delta / GetComponentInParent<Canvas>().scaleFactor;
        }
    }

    // --- LOCK/UNLOCK (Now respects allowDragging) ---
    public void ToggleLock()
    {
        if (!allowDragging) return; // <-- Skip if dragging is disabled

        isLocked = !isLocked;
        deviceImage.color = isLocked ? new Color(0.8f, 0.8f, 0.8f, 0.7f) : Color.white;

        if (isLocked)
        {
            savedPosition = rectTransform.anchoredPosition;
            PlayerPrefs.SetFloat($"{name}_posX", savedPosition.x);
            PlayerPrefs.SetFloat($"{name}_posY", savedPosition.y);
            PlayerPrefs.Save();
        }
    }

    // --- RESIZE (Now respects allowDragging) ---
    private void Update()
    {
        if (Input.GetKeyDown(lockKey)) ToggleLock();
        if (Input.GetKeyDown(saveKey)) SaveDevice();

        if (isLocked || !allowDragging) return; // <-- Skip if locked or dragging disabled

        // Mouse wheel resize
        if (Input.GetKey(KeyCode.LeftShift) && !TextEditHandler.IsEditing)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0) ResizeDevice(scroll > 0 ? resizeSpeed : -resizeSpeed);
        }
    }




    public void SaveDevice()
    {
        allowDragging = false;
        Debug.Log($"[SaveDevice] Attempting to save device on GameObject: {gameObject.name}");

        var registry = FindObjectOfType<DeviceRegistryManager>();
        if (registry == null)
        {
            Debug.LogError("[SaveDevice]  DeviceRegistryManager not found in scene.");
            return;
        }
        else
        {
            Debug.Log("[SaveDevice] Found DeviceRegistryManager.");
        }

        var localController = GetComponentInChildren<LocalTuyaController>();
        var cloudController = GetComponentInChildren<TuyaController>();

        if (localController == null && cloudController == null)
        {
            Debug.LogWarning("[SaveDevice]  No LocalTuyaController or TuyaController found on this GameObject.");
        }

        var deviceId = localController?.deviceId ?? cloudController?.deviceId;
        if (string.IsNullOrEmpty(deviceId))
        {
            Debug.LogWarning("[SaveDevice]  No device ID found. Cannot proceed.");
            return;
        }

        Debug.Log($"[SaveDevice] Device ID: {deviceId}");

        bool isLocal = localController != null;

        Debug.Log($"[SaveDevice] Mode: {(isLocal ? "Local" : "Cloud")}");

        var existing = registry.GetDeviceById(deviceId);
        bool isNew = existing == null;

        if (isNew)
        {
            Debug.Log("[SaveDevice] Creating new device entry in registry.");
        }
        else
        {
            Debug.Log("[SaveDevice] Updating existing device entry.");
        }

        var deviceData = existing ?? new DeviceRegistryManager.DeviceData
        {
            deviceId = deviceId,
            device_name = localController?.device_name ?? cloudController?.device_name ?? "Unnamed Device",
            mode = isLocal ? "local" : "cloud",
            type = localController?.deviceType ?? cloudController?.deviceType ?? DeviceType.Unknown
        };

        Debug.Log($"[SaveDevice] Device Name: {deviceData.device_name}");
        Debug.Log($"[SaveDevice] Device Type: {deviceData.type}");

        // Visual data
        deviceData.position = rectTransform.anchoredPosition;
        deviceData.size = rectTransform.sizeDelta;

        Debug.Log($"[SaveDevice] RectTransform position: {deviceData.position}");
        Debug.Log($"[SaveDevice] RectTransform size: {deviceData.size}");

        // Icon path
        var iconManager = GetComponent<DeviceIconManager>();
        if (iconManager == null)
        {
            Debug.LogWarning("[SaveDevice]  DeviceIconManager not found. Icon will not be saved.");
            deviceData.iconPath = "";
        }
        else
        {
            deviceData.iconPath = iconManager.GetIconPath();
            Debug.Log($"[SaveDevice] Icon path: {deviceData.iconPath}");
        }

        registry.SaveDevice(deviceData);
        Debug.Log($"[SaveDevice]  Device saved to registry: {(isNew ? "New entry" : "Updated")}");

#if UNITY_EDITOR
        EditorSaveAsPrefab();
        Debug.Log($"[SaveDevice]  Device saved as prefab in Editor mode.");
#endif
    }


#if UNITY_EDITOR
    private void EditorSaveAsPrefab()
    {
        string prefabPath = "Assets/Resources/DevicePrefabs/";
        Directory.CreateDirectory(prefabPath);

        string prefabName = $"{GetComponentInChildren<LocalTuyaController>()?.deviceId ?? GetComponentInChildren<TuyaController>()?.deviceId}.prefab";

        PrefabUtility.SaveAsPrefabAsset(gameObject, Path.Combine(prefabPath, prefabName));
    }
#endif
    public void LoadIcon(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            byte[] fileData = File.ReadAllBytes(path);
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(fileData);

            Sprite iconSprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f)
            );

            if (deviceImage != null)
            {
                deviceImage.sprite = iconSprite;
            }

            iconPath = path;
            PlayerPrefs.SetString($"{name}_iconPath", path);
            PlayerPrefs.Save();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load icon: {e.Message}");
        }
    }
    private void AddTextEditComponent()
    {
        var editComponent = textLabel.AddComponent<TextEditHandler>();
        editComponent.Initialize(textComponent, this);
    }
 

   

    private void ResizeDevice(float amount)
    {
        Vector2 newSize = rectTransform.sizeDelta + (originalSize * amount);
        newSize.x = Mathf.Clamp(newSize.x, originalSize.x * minSize, originalSize.x * maxSize);
        newSize.y = Mathf.Clamp(newSize.y, originalSize.y * minSize, originalSize.y * maxSize);
        rectTransform.sizeDelta = newSize;

        // Scale text appropriately
        if (textLabel != null)
        {
            RectTransform textRect = textLabel.GetComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(
                textSize.x * (newSize.x / originalSize.x),
                textSize.y * (newSize.y / originalSize.y)
            );
        }
    }

    private void UpdateTextLabel()
    {
        if (textLabel == null) return;

        textLabel.SetActive(showText);
        if (showText)
        {
            RectTransform textRect = textLabel.GetComponent<RectTransform>();
            textRect.sizeDelta = textSize;
            textComponent.fontSize = fontSize;
        }
    }

    public void SaveAsPrefab()
    {
#if UNITY_EDITOR
        string prefabPath = "Assets/Prefabs/Devices/";
        if (!Directory.Exists(prefabPath))
        {
            Directory.CreateDirectory(prefabPath);
        }

        string deviceName = GetComponent<LocalTuyaController>()?.device_name ??
                          GetComponent<TuyaController>()?.device_name ?? "Device";

        // Save icon path in the prefab
        var iconPath = iconManager.iconPath;
        if (!string.IsNullOrEmpty(iconPath))
        {
            // Copy icon to project folder if it's not already there
            string targetIconPath = Path.Combine("Assets/Resources/DeviceIcons", Path.GetFileName(iconPath));
            if (!File.Exists(targetIconPath))
            {
                File.Copy(iconPath, targetIconPath);
                AssetDatabase.Refresh();
            }

            // Update to use relative path
            iconManager.iconPath = targetIconPath;
        }

        string uniqueName = $"{deviceName}_{System.DateTime.Now:yyyyMMddHHmmss}.prefab";
        string fullPath = Path.Combine(prefabPath, uniqueName);

        // Save the prefab
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(gameObject, fullPath);
        Debug.Log($"Saved prefab at {fullPath} with icon at {iconManager.iconPath}");
#endif
    }

    // Public methods to modify text properties
    public void SetText(string newText)
    {
        if (textComponent != null)
        {
            textComponent.text = newText;
        }
    }

    public void SetTextVisibility(bool visible)
    {
        showText = visible;
        UpdateTextLabel();
    }

    public void SetTextSize(int size)
    {
        fontSize = size;
        UpdateTextLabel();
    }

    private void OnDestroy()
    {
        if (textLabel != null)
        {
            Destroy(textLabel);

        }
        if (GlobalDragController.Instance != null)
            GlobalDragController.Instance.UnregisterDevice(this);
    }

    // Add this to DeviceDragger class
    public void OnTextChanged(string newText)
    {
        // Handle any additional logic when text changes
        Debug.Log($"Text changed to: {newText}");

        // If you want to update the device name in the controller:
        var localController = GetComponent<LocalTuyaController>();
        var cloudController = GetComponent<TuyaController>();

        if (localController != null) localController.device_name = newText;
        if (cloudController != null) cloudController.device_name = newText;
    }

  
}
// Separate class for text editing functionality
public class TextEditHandler : MonoBehaviour, IPointerClickHandler
{
    public static bool IsEditing { get; private set; }

    private TMP_Text textComponent;
    private TMP_InputField inputField;
    private DeviceDragger parentDragger;
    private string originalText;

    public void Initialize(TMP_Text textComp, DeviceDragger dragger)
    {
        textComponent = textComp;
        parentDragger = dragger;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.clickCount == 2) // Double-click
        {
            StartTextEditing();
        }
    }

    private void StartTextEditing()
    {
        if (IsEditing) return;

        IsEditing = true;
        originalText = textComponent.text;

        // Create input field
        GameObject inputFieldObj = new GameObject("TextEditField");
        inputFieldObj.transform.SetParent(textComponent.transform.parent, false);

        // Copy text component properties
        inputField = inputFieldObj.AddComponent<TMP_InputField>();
        RectTransform inputRT = inputFieldObj.GetComponent<RectTransform>();
        RectTransform textRT = textComponent.GetComponent<RectTransform>();

        inputRT.sizeDelta = textRT.sizeDelta;
        inputRT.anchoredPosition = textRT.anchoredPosition;
        inputRT.pivot = textRT.pivot;

        // Configure input field
        inputField.textComponent = textComponent;
        inputField.text = originalText;
        inputField.onEndEdit.AddListener(EndTextEditing);
        inputField.Select();
        inputField.ActivateInputField();

        // Hide original text during edit
        textComponent.enabled = false;
    }

    private void EndTextEditing(string newText)
    {
        IsEditing = false;
        textComponent.text = newText;
        textComponent.enabled = true;
        Destroy(inputField.gameObject);

        // Notify parent dragger of text change
        if (parentDragger != null)
        {
            parentDragger.OnTextChanged(newText);
        }
    }
}
