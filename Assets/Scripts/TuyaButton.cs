using UnityEngine;
using UnityEngine.UI;

public class TuyaButton : MonoBehaviour
{
    [Header("Tuya Command Details")]
    public string command = "custom";
    public string dps = "20";
    public string value = "true";

    [Header("Controller Reference")]
    public LocalTuyaController controller;

    public void SendCommand()
    {
        if (controller != null)
        {
            controller.SendLocalCommand(command, dps, value);
        }
        else
        {
            Debug.LogError("No LocalTuyaController assigned to TuyaButton on " + gameObject.name);
        }
    }
}
