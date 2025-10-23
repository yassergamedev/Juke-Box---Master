using UnityEngine;
using System.Collections.Generic;

public class GlobalDragController : MonoBehaviour
{
    public static GlobalDragController Instance;
    private List<DeviceDragger> allDevices = new List<DeviceDragger>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void RegisterDevice(DeviceDragger device)
    {
        if (!allDevices.Contains(device))
            allDevices.Add(device);
    }

    public void UnregisterDevice(DeviceDragger device)
    {
        if (allDevices.Contains(device))
            allDevices.Remove(device);
    }

    public void ToggleAllDragging(bool allow)
    {
        foreach (DeviceDragger device in allDevices)
        {
            device.allowDragging = true;
        }
    }
    public void SaveAllDragging()
    {
        foreach (DeviceDragger device in allDevices)
        {
            device.SaveDevice();
        }
    }

}