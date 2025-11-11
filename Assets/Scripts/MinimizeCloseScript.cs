using UnityEngine;
using System.Runtime.InteropServices;
using System.Diagnostics;

public class MinimizeCloseScript : MonoBehaviour
{ 
    public MasterNetworkHandler masterNetworkHandler;
    
    // Windows-specific imports
    [DllImport("user32.dll")]
    private static extern int ShowWindow(System.IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern System.IntPtr GetActiveWindow();

    private const int SW_MINIMIZE = 6;

    public void MinimizeWindow()
    {
        #if UNITY_STANDALONE_WIN
            // Windows implementation using user32.dll
            ShowWindow(GetActiveWindow(), SW_MINIMIZE);
        #elif UNITY_STANDALONE_LINUX
            // Linux implementation using xdotool
            MinimizeWindowLinux();
        #else
            // Fallback for other platforms
            UnityEngine.Debug.LogWarning("Minimize not supported on this platform");
        #endif
    }

    private void MinimizeWindowLinux()
    {
        try
        {
            // Get the current process ID
            int processId = Process.GetCurrentProcess().Id;
            
            // Use xdotool to minimize the window
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "xdotool",
                Arguments = $"windowminimize $(xdotool search --pid {processId} --class Unity)",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process process = Process.Start(startInfo);
            process.WaitForExit(1000); // Wait up to 1 second

            if (process.ExitCode != 0)
            {
                UnityEngine.Debug.LogWarning("Failed to minimize window using xdotool. Make sure xdotool is installed.");
                // Fallback: try alternative method
                MinimizeWindowLinuxAlternative();
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Error minimizing window on Linux: {e.Message}");
            // Fallback: try alternative method
            MinimizeWindowLinuxAlternative();
        }
    }

    private void MinimizeWindowLinuxAlternative()
    {
        try
        {
            // Alternative method using wmctrl
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "wmctrl",
                Arguments = "-r :ACTIVE: -b add,hidden",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process process = Process.Start(startInfo);
            process.WaitForExit(1000);

            if (process.ExitCode != 0)
            {
                UnityEngine.Debug.LogWarning("Failed to minimize window using wmctrl. Neither xdotool nor wmctrl are available.");
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Error with alternative minimize method: {e.Message}");
        }
    }

    public void CloseApplication()
    {
        masterNetworkHandler?.Pause_Resume();
        Application.Quit();
    }
}
