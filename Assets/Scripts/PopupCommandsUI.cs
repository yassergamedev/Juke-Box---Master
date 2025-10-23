using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PopupCommandsUI : MonoBehaviour
{
    [Header("Buttons in order")]
    public Button[] commandButtons;

    [Header("Debug & Info Texts")]
    public Text debugText;   
    public Text deviceIpText; 

    private LocalTuyaCommandData[] currentCommands;
    private LocalTuyaController currentController;

    public void Assign(LocalTuyaCommandData[] commands, LocalTuyaController controller)
    {
        currentCommands = commands;
        currentController = controller;

       
        if (deviceIpText != null)
        {
            deviceIpText.text = $"IP: {currentController.deviceIp}";
        }

        for (int i = 0; i < commandButtons.Length; i++)
        {
            int index = i;

            commandButtons[i].onClick.RemoveAllListeners();

            if (index < currentCommands.Length)
            {
                commandButtons[i].onClick.AddListener(() =>
                {
                    var cmd = currentCommands[index];

                    SetDebugText($"Sending: {cmd.command}");

                    currentController.SendLocalCommand(cmd.command, cmd.dps, cmd.value, (result) =>
                    {
                        SetDebugText(result);
                    });

                 
                });
            }
            else
            {
                commandButtons[i].onClick.AddListener(() =>
                {
                    SetDebugText("No command assigned to this button.");
                });
            }
        }
    }

    private void SetDebugText(string message)
    {
        if (debugText != null)
        {
            debugText.text = message;
        }
        else
        {
            Debug.Log(message);
        }
    }
}
