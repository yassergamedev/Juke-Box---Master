using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

public class TimerEnactor : MonoBehaviour
{
    [Header("References")]
    public DeviceRegistryManager deviceManager;
    public TMP_Text timerStatusText;
    public Transform timerListContainer;
    public GameObject timerItemPrefab;

    [Header("Settings")]
    [Range(10, 300)] public float checkInterval = 60f;
    private Dictionary<string, DateTime> lastExecutionTimes = new();
    private List<TimerDisplay> activeTimers = new();

    private class TimerDisplay
    {
        public TimerData timer;
        public GameObject uiObject;
        public TMP_Text timerText;
        public float timeRemaining;
    }

    private void Start()
    {
        UpdateTimerDisplay();
        StartCoroutine(TimerCheckRoutine());
        StartCoroutine(CountdownUpdateRoutine());
    }

    private IEnumerator TimerCheckRoutine()
    {
        while (true)
        {
            // Check at exact minute intervals
            var now = DateTime.Now;
            var nextMinute = now.AddMinutes(1).AddSeconds(-now.Second);
            yield return new WaitForSeconds((float)(nextMinute - now).TotalSeconds);

            CheckAndExecuteTimers();
            UpdateTimerDisplay();
        }
    }

    private IEnumerator CountdownUpdateRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);
            UpdateCountdowns();
        }
    }

    public void UpdateTimerDisplay()
    {
        // Clear existing UI
        foreach (Transform child in timerListContainer)
            Destroy(child.gameObject);
        activeTimers.Clear();

        var currentTime = DateTime.Now.TimeOfDay;
        var currentDay = DateTime.Now.DayOfWeek.ToString().Substring(0, 3); // Get 3-letter day (e.g. "Monday" -> "Mon")

        var timers = LoadAllTimers().timers
            .Where(t => t.days.Any(day =>
                string.Equals(day, currentDay, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t => t.time)
            .ToList();

        foreach (var timer in timers)
        {
            var timeParts = timer.time.Split(':');
            if (timeParts.Length != 2) continue;

            if (!int.TryParse(timeParts[0], out int hours) || !int.TryParse(timeParts[1], out int minutes))
                continue;

            var timerTime = new TimeSpan(hours, minutes, 0);
            var timeRemaining = (timerTime - currentTime).TotalSeconds;

            // Only show timers that haven't executed yet today
            string timerKey = $"{timer.deviceId}_{timer.time}_{string.Join("", timer.days)}";
            if (!lastExecutionTimes.ContainsKey(timerKey) ||
                lastExecutionTimes[timerKey].Date != DateTime.Today)
            {
                // Skip timers that should have already run today
                if (timeRemaining > 0)
                {
                    CreateTimerUI(timer, timeRemaining);
                }
            }
        }

        timerStatusText.text = activeTimers.Count > 0
            ? $"{activeTimers.Count} active timers today"
            : "No timers scheduled for today";
    }

    private void CreateTimerUI(TimerData timer, double secondsRemaining)
    {
        var item = Instantiate(timerItemPrefab, timerListContainer);
        var timerText = item.GetComponentInChildren<TMP_Text>();

        var device = deviceManager.GetDevices().FirstOrDefault(d => d.deviceId == timer.deviceId);
        string deviceName = device?.device_name ?? "Unknown Device";

        activeTimers.Add(new TimerDisplay
        {
            timer = timer,
            uiObject = item,
            timerText = timerText,
            timeRemaining = (float)secondsRemaining
        });

        UpdateTimerText(timerText, timer, deviceName, secondsRemaining);
    }

    private void UpdateCountdowns()
    {
        foreach (var timerDisplay in activeTimers.ToArray())
        {
            timerDisplay.timeRemaining -= 1f;

            var device = deviceManager.GetDevices()
                .FirstOrDefault(d => d.deviceId == timerDisplay.timer.deviceId);
            string deviceName = device?.device_name ?? "Unknown Device";

            if (timerDisplay.timeRemaining <= 0)
            {
                // Timer expired, remove from display
                Destroy(timerDisplay.uiObject);
                activeTimers.Remove(timerDisplay);
                continue;
            }

            UpdateTimerText(timerDisplay.timerText, timerDisplay.timer,
                deviceName, timerDisplay.timeRemaining);
        }
    }

    private void UpdateTimerText(TMP_Text textElement, TimerData timer,
        string deviceName, double secondsRemaining)
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(secondsRemaining);
        string timeString = timeSpan.ToString(@"hh\:mm\:ss");

        textElement.text = $"{deviceName}: " +
            $"{(timer.isOnTimer ? "ON" : "OFF")} at {timer.time} " +
            $"(in {timeString})\n" +
            $"Days: {string.Join(", ", timer.days)}";
    }

    private void CheckAndExecuteTimers()
    {
        var currentTime = DateTime.Now;
        var currentDay = currentTime.DayOfWeek.ToString();
        var currentTimeString = currentTime.ToString("HH:mm");

        var timers = LoadAllTimers();
        foreach (var timer in timers.timers)
        {
            try
            {
                // Check if timer should execute now
                if (ShouldExecuteTimer(timer, currentDay, currentTimeString))
                {
                    ExecuteTimer(timer);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing timer for device {timer.deviceId}: {ex.Message}");
            }
        }
    }

    private bool ShouldExecuteTimer(TimerData timer, string currentDay, string currentTime)
    {
        // Convert current day to 3-letter format for comparison
        string currentDayShort = DateTime.Now.DayOfWeek.ToString().Substring(0, 3);

        // Check if timer is for today (case-insensitive)
        if (!timer.days.Any(d => d.Equals(currentDayShort, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Check if time matches exactly
        if (timer.time != currentTime)
            return false;

        // Check execution history
        string timerKey = $"{timer.deviceId}_{timer.time}_{string.Join("", timer.days)}";
        if (lastExecutionTimes.TryGetValue(timerKey, out var lastTime))
        {
            if (lastTime.Date == DateTime.Today)
                return false;
        }

        lastExecutionTimes[timerKey] = DateTime.Now;
        return true;
    }

    private void ExecuteTimer(TimerData timer)
    {
        // Find the device in our registry
        var device = deviceManager.GetDevices().FirstOrDefault(d => d.deviceId == timer.deviceId);
        if (device == null)
        {
            Debug.LogWarning($"Device {timer.deviceId} not found in registry");
            return;
        }

        // Find the actual controller in the scene
        if (device.mode == "local")
        {
            ExecuteLocalTimer(timer, device);
        }
        else
        {
            ExecuteCloudTimer(timer, device);
        }
    }

    private void ExecuteLocalTimer(TimerData timer, DeviceRegistryManager.DeviceData device)
    {
        var localController = FindLocalController(device.deviceId);
        if (localController == null)
        {
            Debug.LogWarning($"[LocalTimer] Controller for device '{device.deviceId}' not found in scene.");
            return;
        }

        var commandHandler = localController.GetComponent<DropdownCommandHandler>();
        if (commandHandler == null || commandHandler.tuyaCommands.Length < 2)
        {
            Debug.LogWarning($"[LocalTimer] Missing or invalid command handler for local device '{device.deviceId}'.");
            return;
        }

        if (timer.isOnTimer)
        {
            var onCommand = commandHandler.tuyaCommands[0];
            Debug.Log($"[LocalTimer] Sending ON to '{device.deviceId}': command={onCommand.command}, dps={onCommand.dps}, value={onCommand.value}");

            localController.SendLocalCommand(onCommand.command, onCommand.dps, onCommand.value,
                result => Debug.Log($"[LocalTimer] ON result for '{device.deviceId}': {result}"));
        }
        else
        {
            var offCommand = commandHandler.tuyaCommands[1];
            Debug.Log($"[LocalTimer] Sending OFF to '{device.deviceId}': command={offCommand.command}, dps={offCommand.dps}, value={offCommand.value}");

            localController.SendLocalCommand(offCommand.command, offCommand.dps, offCommand.value,
                result => Debug.Log($"[LocalTimer] OFF result for '{device.deviceId}': {result}"));
        }
    }


    private void ExecuteCloudTimer(TimerData timer, DeviceRegistryManager.DeviceData device)
    {
        var cloudController = FindCloudController(device.deviceId);
        if (cloudController == null)
        {
            Debug.LogWarning($"[CloudTimer] Controller for device '{device.deviceId}' not found in scene.");
            return;
        }

        if (timer.isOnTimer)
        {
            if (cloudController.availableCommands.Length > 0)
            {
                var onCommand = cloudController.availableCommands[0];
                Debug.Log($"[CloudTimer] Sending ON to '{device.deviceId}': code={onCommand.code}, value={onCommand.value}");

                cloudController.SendCommand(onCommand.code, onCommand.value,
                    result => Debug.Log($"[CloudTimer] ON result for '{device.deviceId}': {result}"));
            }
            else
            {
                Debug.LogWarning($"[CloudTimer] No ON command available for '{device.deviceId}'.");
            }
        }
        else
        {
            if (cloudController.availableCommands.Length > 1)
            {
                var offCommand = cloudController.availableCommands[1];
                Debug.Log($"[CloudTimer] Sending OFF to '{device.deviceId}': code={offCommand.code}, value={offCommand.value}");

                cloudController.SendCommand(offCommand.code, offCommand.value,
                    result => Debug.Log($"[CloudTimer] OFF result for '{device.deviceId}': {result}"));
            }
            else
            {
                Debug.LogWarning($"[CloudTimer] No OFF command available for '{device.deviceId}'.");
            }
        }
    }

    private LocalTuyaController FindLocalController(string deviceId)
    {
        return FindObjectsOfType<LocalTuyaController>()
            .FirstOrDefault(c => c.deviceId == deviceId);
    }

    private TuyaController FindCloudController(string deviceId)
    {
        return FindObjectsOfType<TuyaController>()
            .FirstOrDefault(c => c.deviceId == deviceId);
    }

    private TimerListWrapper LoadAllTimers()
    {
        string path = Path.Combine(Application.persistentDataPath, "timer_list.json");
        if (!File.Exists(path)) return new TimerListWrapper();

        try
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<TimerListWrapper>(json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading timers: {ex.Message}");
            return new TimerListWrapper();
        }
    }
}