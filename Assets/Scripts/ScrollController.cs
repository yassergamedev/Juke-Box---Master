using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class ScrollController : MonoBehaviour
{
    public ScrollRect scrollRect;
    public Button scrollUpButton;
    public Button scrollDownButton;
    public float scrollAmount = 0.1f; // Adjust for smooth scrolling
    public float autoScrollDelay = 5f; // Time (in seconds) before auto-scroll

    private Coroutine autoScrollCoroutine;

    private void Start()
    {
        // Bind button clicks to scrolling functions
        scrollUpButton.onClick.AddListener(() => OnScroll(ScrollUp));
        scrollDownButton.onClick.AddListener(() => OnScroll(ScrollDown));

        // Start auto-scroll coroutine
        ResetAutoScrollTimer();
    }

    private void OnScroll(System.Action scrollAction)
    {
        scrollAction.Invoke();  // Execute the scroll action
        ResetAutoScrollTimer(); // Restart the auto-scroll timer
    }

    private void ScrollUp()
    {
        float newY = Mathf.Clamp(scrollRect.verticalNormalizedPosition + scrollAmount, 0f, 1f);
        scrollRect.verticalNormalizedPosition = newY;
    }

    private void ScrollDown()
    {
        float newY = Mathf.Clamp(scrollRect.verticalNormalizedPosition - scrollAmount, 0f, 1f);
        scrollRect.verticalNormalizedPosition = newY;
    }

    private void ResetAutoScrollTimer()
    {
        if (autoScrollCoroutine != null)
            StopCoroutine(autoScrollCoroutine);

        autoScrollCoroutine = StartCoroutine(AutoScrollToTop());
    }

    private IEnumerator AutoScrollToTop()
    {
        yield return new WaitForSeconds(autoScrollDelay);
        scrollRect.verticalNormalizedPosition = 1f; // Scroll to the top
    }
}
