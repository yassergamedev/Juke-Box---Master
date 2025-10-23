using UnityEngine;
using System.Runtime.InteropServices;

public class MinimizeCloseScript : MonoBehaviour
{ 
    public MasterNetworkHandler masterNetworkHandler;
    [DllImport("user32.dll")]
    private static extern int ShowWindow(System.IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern System.IntPtr GetActiveWindow();

    private const int SW_MINIMIZE = 6;

    public void MinimizeWindow()
    {
        ShowWindow(GetActiveWindow(), SW_MINIMIZE);
    }

    public void CloseApplication()
    {
        masterNetworkHandler?.Pause_Resume();
        Application.Quit();
    }
}
