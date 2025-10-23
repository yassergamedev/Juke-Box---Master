using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;

public class GroupItemUI : MonoBehaviour
{
    public TMP_Text groupNameText;
    public Button onButton;
    public Button offButton;

    private string groupName;
    private Action<string> onCallback;
    private Action<string> offCallback;

    public void Setup(string name, Action<string> onAction, Action<string> offAction)
    {
        groupName = name;
        groupNameText.text = name;
        onCallback = onAction;
        offCallback = offAction;

        onButton.onClick.RemoveAllListeners();
        offButton.onClick.RemoveAllListeners();

        onButton.onClick.AddListener(() => onCallback?.Invoke(groupName));
        offButton.onClick.AddListener(() => offCallback?.Invoke(groupName));
    }
}
