using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DeviceItemUI : MonoBehaviour
{
    public Image deviceTypeIcon;
    public TMP_Text deviceNameText;
    public Toggle selectToggle;

    private string deviceId;

    public void Setup(string name, string id, Sprite typeIcon)
    {
        deviceId = id;
        deviceNameText.text = name;
        deviceTypeIcon.sprite = typeIcon;
        selectToggle.isOn = false;
    }

    public string GetDeviceId() => deviceId;
    public bool IsSelected() => selectToggle.isOn;
}
