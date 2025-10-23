using UnityEngine;

public class ResolutionSetter : MonoBehaviour
{
    // Resolution to set (1920x1080)
    private int targetWidth = 1920;
    private int targetHeight = 1080;
    private bool isFullScreen = true; // Set to true if you want fullscreen

    void Start()
    {
        // Set the resolution at the start of the game
        SetResolution();
    }

    void SetResolution()
    {
        Screen.SetResolution(targetWidth, targetHeight, isFullScreen);
    }
}
