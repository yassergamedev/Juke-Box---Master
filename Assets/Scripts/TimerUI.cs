using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
public class TimerUI : MonoBehaviour
{
    public TMP_Text timerTypeText;
    public TMP_Text timeAndDaysText;
    public Button deleteButton;

    private Action onDeleteCallback;

    public void Setup(TimerData timer, Action onDelete)
    {
        timerTypeText.text = timer.isOnTimer ? " ON Timer" : "OFF Timer";
        timeAndDaysText.text = $"{timer.time} — {string.Join(", ", timer.days)}";

        onDeleteCallback = onDelete;
        deleteButton.onClick.RemoveAllListeners();
        deleteButton.onClick.AddListener(() => onDeleteCallback?.Invoke());
    }
}
