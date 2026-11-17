namespace WinTaskBlocker.Models;

public class AppPreferences
{
    // General Options (from classic Task Blocker v1.5 + modern additions)
    public bool LaunchAtWindowsStartup { get; set; } = true;
    public bool ShowInForegroundOnStart { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;

    // Log Options
    public bool EnableLogOnFile { get; set; } = false;
    public bool WarnIfLogExceedsSize { get; set; } = false;
    public double MaxLogSizeMb { get; set; } = 15.0;
    public string DefaultLogEditor { get; set; } = "notepad.exe";

    // Task Monitoring Options
    public bool StartMonitoringOnAppStart { get; set; } = true;
    public bool ShowNotificationWhenBlocked { get; set; } = true;
    public bool MonitorModeOnly { get; set; } = false; // Tasks counted/logged but not killed
    public bool FastRealtimeCheck { get; set; } = true; // Uses WMI + optimized low-CPU snapshot (350ms)
    public int ScanIntervalMs { get; set; } = 400;

    // Appearance (Wintoys + Acrylic Glass style like example.jpg)
    public double GlassOpacity { get; set; } = 0.72;
    public string AccentTheme { get; set; } = "PurpleNebula"; // PurpleNebula, CyanFrost, MidnightGlass
}
