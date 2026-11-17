namespace WinTaskBlocker.Models;

public class AppPreferences
{
    // Language ("en" default as requested, or "es")
    public string Language { get; set; } = "en";

    // General & Startup Options
    public bool LaunchAtWindowsStartup { get; set; } = true;
    public bool StartMinimizedOnWindowsStartup { get; set; } = true;
    public bool ShowInForegroundOnStart { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;

    // Log Options
    public bool EnableLogOnFile { get; set; } = false;
    public bool WarnIfLogExceedsSize { get; set; } = false;
    public double MaxLogSizeMb { get; set; } = 15.0;
    public string DefaultLogEditor { get; set; } = "notepad.exe";

    // Task Monitoring Options (Always starts blocking enabled on launch/startup)
    public bool StartMonitoringOnAppStart { get; set; } = true;
    public bool ShowNotificationWhenBlocked { get; set; } = true;
    public bool MonitorModeOnly { get; set; } = false;
    public bool FastRealtimeCheck { get; set; } = true;
    public int ScanIntervalMs { get; set; } = 400;

    // Crystal Appearance Defaults: 40% Opacity (0.40) + Windows Blue ("WindowsBlue")
    public double GlassOpacity { get; set; } = 0.40;
    public string AccentTheme { get; set; } = "WindowsBlue"; // WindowsBlue, PurpleNebula, MidnightDark, ArcticCyan
}
