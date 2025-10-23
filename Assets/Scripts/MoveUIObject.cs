using UnityEngine;

public class MoveUIObject : MonoBehaviour
{
    public RectTransform targetUI;
    public float moveDistance = 100f;

    private Vector2 minimizedPosition;
    private Vector2 maximizedPosition;
    private bool isMinimized = false;

    void Start()
    {
        if (targetUI == null)
            targetUI = GetComponent<RectTransform>();

        maximizedPosition = targetUI.anchoredPosition;
        minimizedPosition = maximizedPosition - new Vector2(0, moveDistance);
    }

    public void Toggle()
    {
        if (isMinimized)
        {
            targetUI.anchoredPosition = maximizedPosition;
            isMinimized = false;
        }
        else
        {
            targetUI.anchoredPosition = minimizedPosition;
            isMinimized = true;
        }
    }
}
