using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Threading.Tasks;

public class KeypadScript : MonoBehaviour
{
    [Header("Keypad Settings")]
    public List<Button> digitButtons;
    public Button clearButton;
    public Button enterButton;
    public TMP_Text outputText;

    [Header("Special UI")]
    public GameObject Config; // Reference to Config GameObject

    private string input = "";
    private const int maxInputLength = 4;

    private TrackQueueManager trackQueueManager;
    private SlaveController slaveController;
    private MasterNetworkHandler masterNetworkHandler;
    private AlbumManager albumManager;

    private void Start()
    {
        if (digitButtons == null || digitButtons.Count != 10)
        {
            Debug.LogError("Assign all 10 digit buttons (0-9) in the inspector!");
            return;
        }

        for (int i = 0; i < digitButtons.Count; i++)
        {
            int digit = i;
            digitButtons[i].onClick.AddListener(() => OnDigitButtonPressed(digit));
        }

        clearButton.onClick.AddListener(ClearInput);
        enterButton.onClick.AddListener(ValidateInput);

        ResetOutput();

        trackQueueManager = GetComponent<TrackQueueManager>();
        slaveController = GetComponentInChildren<SlaveController>();
        masterNetworkHandler = GetComponentInChildren<MasterNetworkHandler>();
        albumManager = FindAnyObjectByType<AlbumManager>();
    }

    private void OnDigitButtonPressed(int digit)
    {
        if (input.Length < maxInputLength)
        {
            input += digit;
            UpdateOutput();
        }
    }

    private void ClearInput()
    {
        input = "";
        ResetOutput();
    }

    private async void ValidateInput()
    {
        string formattedInput = FormatInput(input);

        if (formattedInput == "99-99")
        {
            Debug.Log("Activating Config screen!");
            if (Config != null)
                Config.SetActive(true);
            else
                Debug.LogWarning("Config GameObject is not assigned!");
        }
        else if (input.Length == maxInputLength)
        {
            Debug.Log($"[KEYPAD] Valid input: {formattedInput}");

            // Use complete process: validate path, add to MongoDB tracklist, add to Unity queue
            if (trackQueueManager != null)
            {
                Debug.Log($"[KEYPAD] Starting complete song addition process: {formattedInput}");
                await trackQueueManager.AddSongToQueue(formattedInput, "user");
                Debug.Log($"[KEYPAD] Complete song addition process finished: {formattedInput}");
            }
            else
            {
                Debug.LogError("[KEYPAD] TrackQueueManager reference is null!");
                albumManager.UpdateDebugText("TrackQueueManager reference is null!");
            }
        }
        else
        {
            Debug.Log("Incomplete input: " + input);
        }

        ClearInput();
    }

    private void UpdateOutput()
    {
        outputText.text = FormatInput(input);
    }

    private void ResetOutput()
    {
        outputText.text = "00-00";
    }

    private string FormatInput(string input)
    {
        string part1 = input.Length >= 2 ? input.Substring(0, 2) : input.PadRight(2, '0').Substring(0, 2);
        string part2 = input.Length > 2 ? input.Substring(2).PadRight(2, '0') : "00";

        return $"{part1}-{part2}";
    }
}
