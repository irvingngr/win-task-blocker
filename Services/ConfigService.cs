using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using WinTaskBlocker.Models;

namespace WinTaskBlocker.Services;

public class AppPersistedState
{
    public AppPreferences Preferences { get; set; } = new();
    public List<ConditionalRule> ConditionalRules { get; set; } = new();
}

/// <summary>
/// Manages reading/writing both the classic Task Blocker v1.5 config.ini (True|process.exe|23)
/// and the extended JSON configuration for conditional rules, profiles, and preferences.
/// </summary>
public class ConfigService
{
    private readonly string _baseDir;
    public string ConfigIniPath => Path.Combine(_baseDir, "config.ini");
    public string ExtendedStatePath => Path.Combine(_baseDir, "taskblocker.state.json");
    public string LogFilePath => Path.Combine(_baseDir, "taskblocker.log");

    public ConfigService()
    {
        _baseDir = AppDomain.CurrentDomain.BaseDirectory;
    }

    public List<BlockedTaskItem> LoadBlockedTasks(string? customIniPath = null)
    {
        string path = customIniPath ?? ConfigIniPath;
        var list = new List<BlockedTaskItem>();

        if (File.Exists(path))
        {
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var item = BlockedTaskItem.FromLegacyIniLine(rawLine);
                if (item != null)
                {
                    // Instant badge assignment on startup; native .exe icons resolve asynchronously in background
                    item.Icon = IconExtractorService.GetFastBadgeOrCachedIcon(item.ProcessName);
                    list.Add(item);
                }
            }
        }

        if (list.Count == 0)
        {
            list = GetDefaultBlockedTasks();
            SaveBlockedTasks(list, path);
        }

        return list;
    }

    public void SaveBlockedTasks(IEnumerable<BlockedTaskItem> items, string? customIniPath = null)
    {
        string path = customIniPath ?? ConfigIniPath;
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            sb.AppendLine(item.ToLegacyIniLine());
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public AppPersistedState LoadExtendedState()
    {
        if (File.Exists(ExtendedStatePath))
        {
            try
            {
                string json = File.ReadAllText(ExtendedStatePath, Encoding.UTF8);
                var state = JsonSerializer.Deserialize<AppPersistedState>(json);
                if (state != null && state.ConditionalRules.Count > 0)
                {
                    if (string.IsNullOrWhiteSpace(state.Preferences.Language))
                        state.Preferences.Language = "en";
                    if (string.IsNullOrWhiteSpace(state.Preferences.AccentTheme))
                        state.Preferences.AccentTheme = "WindowsBlue";
                    if (state.Preferences.GlassOpacity <= 0.05)
                        state.Preferences.GlassOpacity = 0.40;
                    return state;
                }
            }
            catch { }
        }

        var defaultState = new AppPersistedState
        {
            Preferences = new AppPreferences
            {
                Language = "en",
                GlassOpacity = 0.40,
                AccentTheme = "WindowsBlue",
                LaunchAtWindowsStartup = true,
                StartMinimizedOnWindowsStartup = true,
                ShowInForegroundOnStart = false,
                MinimizeToTrayOnClose = true,
                StartMonitoringOnAppStart = true
            },
            ConditionalRules = GetDefaultConditionalRules()
        };
        SaveExtendedState(defaultState);
        return defaultState;
    }

    public void SaveExtendedState(AppPersistedState state)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(state, options);
            File.WriteAllText(ExtendedStatePath, json, Encoding.UTF8);
        }
        catch { }
    }

    public static List<BlockedTaskItem> GetDefaultBlockedTasks()
    {
        string[] defaultIniLines =
        [
            "True|adobe crash processor.exe|2",
            "True|adobeipcbroker.exe|23",
            "True|ccxprocess.exe|4"
        ];

        var items = new List<BlockedTaskItem>();
        foreach (var line in defaultIniLines)
        {
            var parsed = BlockedTaskItem.FromLegacyIniLine(line);
            if (parsed != null)
            {
                parsed.Icon = IconExtractorService.GetFastBadgeOrCachedIcon(parsed.ProcessName);
                items.Add(parsed);
            }
        }
        return items;
    }

    /// <summary>
    /// Default profiles as requested: Adobe Suite and Gaming / Focus Mode.
    /// </summary>
    public static List<ConditionalRule> GetDefaultConditionalRules()
    {
        return
        [
            new ConditionalRule
            {
                Id = "582f29bae9c444fb87000c4808e4fc16",
                Name = "Adobe Suite",
                Description = "Blocks Adobe background brokers and crash reporters when Photoshop, Premiere, Illustrator, or After Effects are closed.",
                BadgeColor = "#EF4444",
                IsEnabled = true,
                KillOnParentExit = true,
                Mode = ConditionalRuleMode.BlockWhenParentInactive,
                AnchorProcesses = new ObservableCollection<string>
                {
                    "Photoshop.exe",
                    "Illustrator.exe",
                    "AfterFX.exe",
                    "Premiere Pro.exe",
                    "Acrobat.exe"
                },
                TargetProcesses = new ObservableCollection<string>
                {
                    "adobe crash processor.exe",
                    "adobeipcbroker.exe",
                    "ccxprocess.exe",
                    "Adobe Desktop Service.exe",
                    "CoreSync.exe",
                    "acrotray.exe"
                }
            },
            new ConditionalRule
            {
                Id = "f82399513f4449f197a3f04195066d8f",
                Name = "Gaming / Focus Mode",
                Description = "Blocks heavy IDE or background processes only while a game is actively running.",
                BadgeColor = "#38BDF8",
                IsEnabled = false,
                KillOnParentExit = false,
                Mode = ConditionalRuleMode.BlockWhenParentActive,
                AnchorProcesses = new ObservableCollection<string>
                {
                    "valorant.exe",
                    "cs2.exe",
                    "eldenring.exe"
                },
                TargetProcesses = new ObservableCollection<string>
                {
                    "devenv.exe",
                    "java.exe",
                    "cscript.exe"
                }
            }
        ];
    }
}
