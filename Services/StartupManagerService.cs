using System.IO;
using Microsoft.Win32;
using WinTaskBlocker.Models;

namespace WinTaskBlocker.Services;

/// <summary>
/// Reads and manipulates Windows Startup programs across Registry (HKCU/HKLM Run + StartupApproved)
/// and the Windows Startup folder.
/// </summary>
public class StartupManagerService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string DisabledBackupKeyPath = @"Software\WinTaskBlocker\DisabledStartupItems";

    public List<StartupEntry> GetStartupEntries()
    {
        var entries = new List<StartupEntry>();
        bool es = LocalizationService.Instance.IsSpanish;

        // 1. Current User Registry Run
        ReadRegistryStartup(Registry.CurrentUser, es ? "Registro (HKCU Usuario)" : "Registry (HKCU User)", isCurrentUser: true, entries);

        // 2. Local Machine Registry Run
        ReadRegistryStartup(Registry.LocalMachine, es ? "Registro (HKLM Sistema)" : "Registry (HKLM System)", isCurrentUser: false, entries);

        // 3. Disabled items stored by WinTaskBlocker backup
        ReadDisabledBackupRegistry(entries);

        // 4. Startup Folder (%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup)
        ReadStartupFolder(entries);

        return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ReadRegistryStartup(RegistryKey root, string label, bool isCurrentUser, List<StartupEntry> list)
    {
        try
        {
            using var runKey = root.OpenSubKey(RunKeyPath, writable: false);
            if (runKey == null) return;

            using var approvedKey = root.OpenSubKey(ApprovedRunKeyPath, writable: false);

            foreach (var valueName in runKey.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(valueName)) continue;
                string command = runKey.GetValue(valueName)?.ToString() ?? string.Empty;
                string exePath = ExtractExecutableFromCommand(command);

                bool enabled = true;
                if (approvedKey?.GetValue(valueName) is byte[] bytes && bytes.Length >= 1)
                {
                    enabled = bytes[0] % 2 == 0;
                }

                list.Add(new StartupEntry
                {
                    Name = valueName,
                    Command = command,
                    ExecutablePath = exePath,
                    SourceLocation = label,
                    RegistryKeyPath = RunKeyPath,
                    IsCurrentUser = isCurrentUser,
                    IsStartupFolder = false,
                    IsEnabled = enabled,
                    Icon = IconExtractorService.GetIconForProcess(Path.GetFileName(exePath), exePath)
                });
            }
        }
        catch { }
    }

    private static void ReadDisabledBackupRegistry(List<StartupEntry> list)
    {
        try
        {
            using var backupKey = Registry.CurrentUser.OpenSubKey(DisabledBackupKeyPath, writable: false);
            if (backupKey == null) return;
            bool es = LocalizationService.Instance.IsSpanish;

            foreach (var valueName in backupKey.GetValueNames())
            {
                if (list.Any(e => string.Equals(e.Name, valueName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                string command = backupKey.GetValue(valueName)?.ToString() ?? string.Empty;
                string exePath = ExtractExecutableFromCommand(command);

                list.Add(new StartupEntry
                {
                    Name = valueName,
                    Command = command,
                    ExecutablePath = exePath,
                    SourceLocation = es ? "Registro (Deshabilitado)" : "Registry (Disabled)",
                    RegistryKeyPath = RunKeyPath,
                    IsCurrentUser = true,
                    IsStartupFolder = false,
                    IsEnabled = false,
                    Icon = IconExtractorService.GetIconForProcess(Path.GetFileName(exePath), exePath)
                });
            }
        }
        catch { }
    }

    private static void ReadStartupFolder(List<StartupEntry> list)
    {
        try
        {
            string startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (!Directory.Exists(startupDir)) return;
            bool es = LocalizationService.Instance.IsSpanish;

            foreach (var file in Directory.GetFiles(startupDir))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".ini" or ".db") continue;

                bool isDisabled = ext == ".disabled";
                string displayName = Path.GetFileNameWithoutExtension(isDisabled ? Path.GetFileNameWithoutExtension(file) : file);

                list.Add(new StartupEntry
                {
                    Name = displayName,
                    Command = file,
                    ExecutablePath = file,
                    SourceLocation = es ? "Carpeta Inicio de Windows" : "Windows Startup Folder",
                    IsCurrentUser = true,
                    IsStartupFolder = true,
                    IsEnabled = !isDisabled,
                    Icon = IconExtractorService.GetIconForProcess(displayName, file)
                });
            }
        }
        catch { }
    }

    public bool ToggleStartupEntry(StartupEntry entry, bool enable)
    {
        try
        {
            if (entry.IsStartupFolder)
            {
                if (!File.Exists(entry.Command)) return false;
                string newPath = enable
                    ? (entry.Command.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? entry.Command[..^9]
                        : entry.Command)
                    : (entry.Command.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? entry.Command
                        : entry.Command + ".disabled");

                if (newPath != entry.Command)
                {
                    File.Move(entry.Command, newPath, overwrite: true);
                    entry.Command = newPath;
                    entry.ExecutablePath = newPath;
                }
                entry.IsEnabled = enable;
                return true;
            }

            RegistryKey root = entry.IsCurrentUser ? Registry.CurrentUser : Registry.LocalMachine;

            // Update Windows StartupApproved\Run byte array (same mechanism as Windows 11 Task Manager)
            using (var approvedKey = root.CreateSubKey(ApprovedRunKeyPath, writable: true))
            {
                if (approvedKey != null)
                {
                    byte[] stateBytes = new byte[12];
                    stateBytes[0] = enable ? (byte)0x02 : (byte)0x03;
                    approvedKey.SetValue(entry.Name, stateBytes, RegistryValueKind.Binary);
                }
            }

            // Also ensure value exists in Run when enabled
            if (enable)
            {
                using var runKey = root.CreateSubKey(RunKeyPath, writable: true);
                runKey?.SetValue(entry.Name, entry.Command, RegistryValueKind.String);

                using var backupKey = Registry.CurrentUser.CreateSubKey(DisabledBackupKeyPath, writable: true);
                backupKey?.DeleteValue(entry.Name, throwOnMissingValue: false);
            }
            else if (entry.IsCurrentUser)
            {
                using var backupKey = Registry.CurrentUser.CreateSubKey(DisabledBackupKeyPath, writable: true);
                backupKey?.SetValue(entry.Name, entry.Command, RegistryValueKind.String);
            }

            entry.IsEnabled = enable;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool AddCurrentUserStartupEntry(string name, string executablePath, string arguments = "")
    {
        try
        {
            string cmd = string.IsNullOrWhiteSpace(arguments)
                ? $"\"{executablePath}\""
                : $"\"{executablePath}\" {arguments}";

            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            runKey?.SetValue(name, cmd, RegistryValueKind.String);

            using var approvedKey = Registry.CurrentUser.CreateSubKey(ApprovedRunKeyPath, writable: true);
            byte[] enabledBytes = new byte[12];
            enabledBytes[0] = 0x02;
            approvedKey?.SetValue(name, enabledBytes, RegistryValueKind.Binary);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool DeleteStartupEntry(StartupEntry entry)
    {
        try
        {
            if (entry.IsStartupFolder)
            {
                if (File.Exists(entry.Command))
                    File.Delete(entry.Command);
                return true;
            }

            RegistryKey root = entry.IsCurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
            using var runKey = root.OpenSubKey(RunKeyPath, writable: true);
            runKey?.DeleteValue(entry.Name, throwOnMissingValue: false);

            using var approvedKey = root.OpenSubKey(ApprovedRunKeyPath, writable: true);
            approvedKey?.DeleteValue(entry.Name, throwOnMissingValue: false);

            using var backupKey = Registry.CurrentUser.OpenSubKey(DisabledBackupKeyPath, writable: true);
            backupKey?.DeleteValue(entry.Name, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetSelfStartup(bool enable)
    {
        try
        {
            string appName = "WinTaskBlocker";
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath)) return;

            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enable)
                runKey?.SetValue(appName, $"\"{exePath}\" --startup", RegistryValueKind.String);
            else
                runKey?.DeleteValue(appName, throwOnMissingValue: false);
        }
        catch { }
    }

    public static string ExtractExecutableFromCommand(string rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand)) return string.Empty;
        string trimmed = rawCommand.Trim();

        if (trimmed.StartsWith('"'))
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1)
                return trimmed[1..endQuote];
        }

        int exeIdx = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx > 0)
            return trimmed[..(exeIdx + 4)].Trim('"');

        return trimmed.Split(' ')[0].Trim('"');
    }
}
