using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace WinTaskBlocker.Models;

public class BlockedTaskItem : INotifyPropertyChanged
{
    private bool _isEnabled = true;
    private string _processName = string.Empty;
    private int _blockedCount;
    private string _executablePath = string.Empty;
    private string _description = string.Empty;
    private string _profileGroup = "General";
    private string _lastBlockedText = "Nunca";
    private ImageSource? _icon;

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public string ProcessName
    {
        get => _processName;
        set { _processName = value; OnPropertyChanged(); }
    }

    public int BlockedCount
    {
        get => _blockedCount;
        set { _blockedCount = value; OnPropertyChanged(); }
    }

    public string ExecutablePath
    {
        get => _executablePath;
        set { _executablePath = value; OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public string ProfileGroup
    {
        get => _profileGroup;
        set { _profileGroup = value; OnPropertyChanged(); }
    }

    public string LastBlockedText
    {
        get => _lastBlockedText;
        set { _lastBlockedText = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Formats into classic Task Blocker v1.5 config.ini line: True|process.exe|23
    /// </summary>
    public string ToLegacyIniLine() => $"{IsEnabled}|{ProcessName}|{BlockedCount}";

    public static BlockedTaskItem? FromLegacyIniLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var parts = line.Trim().Split('|');
        if (parts.Length < 2) return null;

        bool enabled = !bool.TryParse(parts[0], out var b) || b;
        string name = parts[1].Trim();
        int count = parts.Length >= 3 && int.TryParse(parts[2].Trim(), out var c) ? c : 0;

        return new BlockedTaskItem
        {
            IsEnabled = enabled,
            ProcessName = name,
            BlockedCount = count,
            ProfileGroup = InferProfileGroup(name),
            Description = InferDefaultDescription(name)
        };
    }

    public static string InferProfileGroup(string processName)
    {
        var lower = processName.ToLowerInvariant();
        if (lower.Contains("adobe") || lower.Contains("ccxprocess") || lower.Contains("acro") || lower.Contains("armsvc"))
            return "Suite Adobe";
        if (lower.Contains("lghub") || lower.Contains("logi"))
            return "Logitech";
        if (lower.Contains("anydesk") || lower.Contains("logmein") || lower.Contains("teamviewer"))
            return "Acceso Remoto";
        if (lower.Contains("gaming"))
            return "Gaming / OSD";
        return "General";
    }

    public static string InferDefaultDescription(string processName)
    {
        var lower = processName.ToLowerInvariant();
        return lower switch
        {
            "adobe crash processor.exe" => "Adobe Crash Reporter Background Service",
            "adobeipcbroker.exe" => "Adobe IPC Broker (Comunicación en segundo plano)",
            "ccxprocess.exe" => "Adobe Creative Cloud Experience Process",
            "anydesk.exe" => "AnyDesk Remote Desktop Service",
            "cmd.exe" => " Símbolo del sistema de Windows",
            "cscript.exe" => "Microsoft Console Based Script Host",
            "devenv.exe" => "Microsoft Visual Studio IDE",
            "gamingapp.exe" => "MSI Gaming App Background Service",
            "gaminghotkey_service.exe" => "Gaming Hotkey Background Service",
            "java.exe" => "Java(TM) Platform SE Binary",
            "lghub.exe" => "Logitech G HUB Client",
            "logiregistryservice.exe" => "Logitech Surround Sound / Registry Service",
            "logmein.exe" => "LogMeIn Remote Access Service",
            "logmeinavserver.exe" => "LogMeIn AV Server",
            _ => "Ejecutable de Windows (.exe)"
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
