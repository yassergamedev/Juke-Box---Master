using TMPro;
using UnityEngine;
using System.Text.RegularExpressions;

public class TimeInputValidator : MonoBehaviour
{
    public TMP_InputField timeField;

    private void Start()
    {
        timeField.onEndEdit.AddListener(OnEndEdit);
    }

    private void OnEndEdit(string input)
    {
        // Only validate if input is in DD:DD format
        if (!Regex.IsMatch(input, @"^\d{2}:\d{2}$"))
        {
            Debug.LogWarning("Time format must be HH:MM.");
            timeField.text = "";
            return;
        }

        string[] parts = input.Split(':');
        int hour = int.Parse(parts[0]);
        int minute = int.Parse(parts[1]);

        if (hour > 23 || minute > 59)
        {
            Debug.LogWarning("Invalid time. Hour must be 0–23 and minute 0–59.");
            timeField.text = "";
        }
    }
}
