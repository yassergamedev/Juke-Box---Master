using TMPro;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class LocalTuyaCommandData
{
    public string command = "custom";
    public string dps = "20";
    public string value = "true";
}

public class DropdownCommandHandler : MonoBehaviour
{
    public TMP_Dropdown dropdown; // Keep for inspector compatibility

    [Header("Local Tuya Controller")]
    public LocalTuyaController tuyaController;

    [Header("Tuya Commands Array")]
    public LocalTuyaCommandData[] tuyaCommands;

    [Header("Assigned Buttons (optional)")]
    public Button[] commandButtons;

    [Header("Popup Script (on same object as popup)")]
    public PopupCommandsUI popupScript;  // Assign the PopupCommandsUI component

    [Header("Popup Open Button")]
    public Button openPopupButton; // <-- NEW: Button to open the popup
    public bool isParentButton = false;
    private void Start()
    {
        // Optional: hide the TMP dropdown
        if (dropdown != null)
            dropdown.gameObject.SetActive(false);

        // Setup individual command buttons
        for (int i = 0; i < commandButtons.Length; i++)
        {
            int index = i;
            if (commandButtons[index] != null)
            {
                commandButtons[index].onClick.RemoveAllListeners();
                commandButtons[index].onClick.AddListener(() =>
                {
                    OnDropdownChanged(index);
                });
            }
        }
      
            openPopupButton =GetComponent<Button>();
        
      

        // Automatically connect popup opener button
        if (openPopupButton != null)
        {
            openPopupButton.onClick.RemoveAllListeners();
            openPopupButton.onClick.AddListener(OpenPopup);
        }
        else
        {
            Debug.LogWarning("Popup open button not assigned.");
        }
    }

    public void OnDropdownChanged(int index)
    {
        Debug.Log("Command button clicked, index: " + index);

        if (index >= 0 && index < tuyaCommands.Length)
        {
            LocalTuyaCommandData cmd = tuyaCommands[index];
            tuyaController.SendLocalCommand(cmd.command, cmd.dps, cmd.value);
        }
        else
        {
            Debug.LogWarning("Index exceeds tuyaCommands array.");
        }
    }

    public void OpenPopup()
    {
        if (popupScript != null)
        {
            popupScript.gameObject.SetActive(true);
            popupScript.Assign(tuyaCommands, tuyaController);
        }
        else
        {
            Debug.LogWarning("Popup script not assigned!");
        }
    }
}
