using TMPro;
using UnityEngine;
using UnityEngine.UI; // For InputField and Button

public class PageFlipController : MonoBehaviour
{
    public TMP_InputField inputField;       // Input field for Page Flip time
    public TMP_InputField fadeInInputField; // Input field for Fade In speed

    public AutoFlip book1;  // Reference to the first AutoFlip component
    public AutoFlip book2;  // Reference to the second AutoFlip component

    public GameObject container; // The container holding all albums

   

    public void OnInputChange()
    {
        if (float.TryParse(inputField.text, out float result))
        {
            book1.PageFlipTime = result;
            book2.PageFlipTime = result;
        }
        else
        {
            Debug.LogError("Invalid input for page flip time. Please enter a valid number.");
        }
    }

    // Update Fade In Speed for all albums
    public void OnFadeInInputChange()
    {
        if (float.TryParse(fadeInInputField.text, out float fadeInSpeed))
        {
            // Change the Animator speed for all child albums
            foreach (Transform album in container.transform)
            {
                Animator albumAnimator = album.GetComponent<Animator>();
                if (albumAnimator != null)
                {
                    albumAnimator.speed = fadeInSpeed;  // Set the fade-in speed (Animator speed)
                }
            }
        }
        else
        {
            Debug.LogError("Invalid input for fade-in speed. Please enter a valid number.");
        }
    }

    // This method is triggered by a button click to call the input change logic
    public void OnButtonClick()
    {
        OnInputChange();
        OnFadeInInputChange();
    }
}
