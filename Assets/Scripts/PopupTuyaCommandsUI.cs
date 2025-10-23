using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PopupTuyaCommandsUI : MonoBehaviour
{
    [Header("UI")]
    public Button[] commandButtons;
    public Text deviceText;
    public Text resultText;

    private TuyaCommandData[] currentCommands;
    private TuyaController currentController;

    public void Assign(TuyaCommandData[] commands, TuyaController controller)
    {
        currentCommands = commands;
        currentController = controller;

        if (deviceText != null)
            deviceText.text = "Device ID: " + controller.deviceId;

        for (int i = 0; i < commandButtons.Length; i++)
        {
            int index = i;

            commandButtons[i].onClick.RemoveAllListeners();

            if (index < currentCommands.Length)
            {
                commandButtons[i].onClick.AddListener(() =>
                {
                    var cmd = currentCommands[index];
                    SetResultText("Sending...");

                    currentController.SendCommand(cmd.code, cmd.value, (result) =>
                    {
                        SetResultText(result);
                    });

                   
                });
            }
            else
            {
                commandButtons[i].onClick.AddListener(() =>
                {
                    SetResultText("No command assigned to this button.");
                });
            }
        }
    }

    private void SetResultText(string msg)
    {
        if (resultText != null)
            resultText.text = msg;
        else
            Debug.Log(msg);
    }
}
