using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Microsoft.Win32;
using WinTaskBlocker.Models;

namespace WinTaskBlocker.Services;

/// <summary>
/// Reads and manipulates all Windows Task Manager Startup programs across:
/// - Registry 64-bit (HKCU/HKLM Run + StartupApproved\Run)
/// - Registry 32-bit WOW64 (HKLM\SOFTWARE\WOW6432Node\...\Run + StartupApproved\Run32)
/// - User Startup Folder (%APPDATA%\...\Startup + HKCU StartupApproved\StartupFolder)
/// - Common Startup Folder (%ProgramData%\...\Startup + HKLM StartupApproved\StartupFolder)
/// - Packaged UWP / MSIX Apps (HKCU\...\AppModel\SystemAppData)
/// </summary>
public class StartupManagerService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32KeyPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedStartupFolderKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string DisabledBackupKeyPath = @"Software\WinTaskBlocker\DisabledStartupItems";
    private const string UwpSystemAppDataPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";
    private const string UwpRepositoryPackagesPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    private static readonly HashSet<string> NonStartupSystemAppDataSubKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "PSR",
        "SplashScreen",
        "PersistedStorageItemTable",
        "Schemas",
        "SystemData",
        "BackgroundTasks"
    };

    [DllImport("shlwapi.dll", BestFitMapping = false, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = false, ThrowOnUnmappableChar = true)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, uint cchOutBuf, IntPtr ppvReserved);

    public List<StartupEntry> GetStartupEntries()
    {
        var entries = new List<StartupEntry>();
        bool es = LocalizationService.Instance.IsSpanish;

        // 1. Current User Registry Run (64-bit / shared HKCU)
        ReadRegistryStartup(
            RegistryHive.CurrentUser,
            RunKeyPath,
            ApprovedRunKeyPath,
            es ? "Registro (HKCU Usuario)" : "Registry (HKCU User)",
            StartupEntryKind.RegistryRun,
            checkHkcuFallbackApproved: false,
            entries);

        // 2. Local Machine Registry Run (64-bit)
        ReadRegistryStartup(
            RegistryHive.LocalMachine,
            RunKeyPath,
            ApprovedRunKeyPath,
            es ? "Registro (HKLM Sistema)" : "Registry (HKLM System)",
            StartupEntryKind.RegistryRun,
            checkHkcuFallbackApproved: false,
            entries);

        // 3. Local Machine Registry Run 32-bit (WOW6432Node + StartupApproved\Run32)
        ReadRegistryStartup(
            RegistryHive.LocalMachine,
            Run32KeyPath,
            ApprovedRun32KeyPath,
            es ? "Registro 32-bit (HKLM WOW64)" : "Registry 32-bit (HKLM WOW64)",
            StartupEntryKind.RegistryRun32,
            checkHkcuFallbackApproved: true,
            entries);

        // 4. Legacy disabled items stored by WinTaskBlocker backup
        ReadDisabledBackupRegistry(entries);

        // 5. Current User Startup Folder (%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup)
        var userStartupFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ReadStartupFolder(
            Environment.SpecialFolder.Startup,
            RegistryHive.CurrentUser,
            es ? "Carpeta Inicio (Usuario)" : "Startup Folder (User)",
            StartupEntryKind.StartupFolderUser,
            checkHkcuFallbackApproved: false,
            userStartupFileNames,
            entries);

        // 6. Common Startup Folder (%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup)
        ReadStartupFolder(
            Environment.SpecialFolder.CommonStartup,
            RegistryHive.LocalMachine,
            es ? "Carpeta Inicio (ProgramData)" : "Common Startup (ProgramData)",
            StartupEntryKind.StartupFolderCommon,
            checkHkcuFallbackApproved: true,
            userStartupFileNames,
            entries);

        // 7. Packaged UWP / MSIX Apps (AppModel\SystemAppData)
        ReadUwpPackagedStartupApps(entries, es);

        return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ReadRegistryStartup(
        RegistryHive hive,
        string runPath,
        string approvedPath,
        string label,
        StartupEntryKind kind,
        bool checkHkcuFallbackApproved,
        List<StartupEntry> list)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var runKey = baseKey.OpenSubKey(runPath, writable: false);
            if (runKey == null) return;

            using var approvedKey = baseKey.OpenSubKey(approvedPath, writable: false);
            using var hkcuBase = checkHkcuFallbackApproved && hive == RegistryHive.LocalMachine
                ? RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)
                : null;
            using var hkcuApprovedFallback = hkcuBase?.OpenSubKey(approvedPath, writable: false);

            bool isCurrentUser = hive == RegistryHive.CurrentUser;

            foreach (var valueName in runKey.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(valueName)) continue;
                string command = runKey.GetValue(valueName)?.ToString() ?? string.Empty;
                string exePath = ExtractExecutableFromCommand(command);

                bool enabled = IsStartupApprovedEnabled(approvedKey, valueName, out bool hasValue);
                if (!hasValue && hkcuApprovedFallback != null)
                {
                    enabled = IsStartupApprovedEnabled(hkcuApprovedFallback, valueName, out _);
                }

                list.Add(new StartupEntry
                {
                    Name = valueName,
                    ValueName = valueName,
                    Command = command,
                    ExecutablePath = exePath,
                    SourceLocation = label,
                    RegistryKeyPath = runPath,
                    ApprovedRegistryKeyPath = approvedPath,
                    EntryKind = kind,
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
            using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var backupKey = hkcu.OpenSubKey(DisabledBackupKeyPath, writable: false);
            if (backupKey == null) return;
            bool es = LocalizationService.Instance.IsSpanish;

            foreach (var valueName in backupKey.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(valueName)) continue;
                if (list.Any(e => e.IsCurrentUser &&
                                  e.EntryKind == StartupEntryKind.RegistryRun &&
                                  string.Equals(e.ValueName, valueName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                string command = backupKey.GetValue(valueName)?.ToString() ?? string.Empty;
                string exePath = ExtractExecutableFromCommand(command);

                list.Add(new StartupEntry
                {
                    Name = valueName,
                    ValueName = valueName,
                    Command = command,
                    ExecutablePath = exePath,
                    SourceLocation = es ? "Registro (Deshabilitado)" : "Registry (Disabled)",
                    RegistryKeyPath = RunKeyPath,
                    ApprovedRegistryKeyPath = ApprovedRunKeyPath,
                    EntryKind = StartupEntryKind.RegistryRun,
                    IsCurrentUser = true,
                    IsStartupFolder = false,
                    IsEnabled = false,
                    Icon = IconExtractorService.GetIconForProcess(Path.GetFileName(exePath), exePath)
                });
            }
        }
        catch { }
    }

    private static void ReadStartupFolder(
        Environment.SpecialFolder folder,
        RegistryHive approvedHive,
        string label,
        StartupEntryKind kind,
        bool checkHkcuFallbackApproved,
        HashSet<string> userStartupFileNames,
        List<StartupEntry> list)
    {
        try
        {
            string startupDir = Environment.GetFolderPath(folder);
            if (string.IsNullOrWhiteSpace(startupDir) || !Directory.Exists(startupDir)) return;

            using var baseKey = RegistryKey.OpenBaseKey(approvedHive, RegistryView.Registry64);
            using var approvedKey = baseKey.OpenSubKey(ApprovedStartupFolderKeyPath, writable: false);
            using var hkcuBase = checkHkcuFallbackApproved && approvedHive == RegistryHive.LocalMachine
                ? RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)
                : null;
            using var hkcuApprovedFallback = hkcuBase?.OpenSubKey(ApprovedStartupFolderKeyPath, writable: false);

            bool isCurrentUser = approvedHive == RegistryHive.CurrentUser;

            foreach (var file in Directory.GetFiles(startupDir))
            {
                string fileName = Path.GetFileName(file);
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".ini" or ".db" || fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isLegacyDisabled = ext == ".disabled";
                string originalFileName = isLegacyDisabled ? Path.GetFileName(file[..^9]) : fileName;
                string displayName = Path.GetFileNameWithoutExtension(originalFileName);

                if (isCurrentUser)
                    userStartupFileNames.Add(originalFileName);

                bool enabled;
                if (isLegacyDisabled)
                {
                    enabled = false;
                }
                else
                {
                    enabled = IsStartupApprovedEnabled(approvedKey, originalFileName, out bool hasValue);
                    if (!hasValue && hkcuApprovedFallback != null && !userStartupFileNames.Contains(originalFileName))
                    {
                        enabled = IsStartupApprovedEnabled(hkcuApprovedFallback, originalFileName, out _);
                    }
                }

                list.Add(new StartupEntry
                {
                    Name = displayName,
                    ValueName = originalFileName,
                    Command = file,
                    ExecutablePath = file,
                    SourceLocation = label,
                    ApprovedRegistryKeyPath = ApprovedStartupFolderKeyPath,
                    EntryKind = kind,
                    IsCurrentUser = isCurrentUser,
                    IsStartupFolder = true,
                    IsEnabled = enabled,
                    Icon = IconExtractorService.GetIconForProcess(displayName, isLegacyDisabled ? null : file)
                });
            }
        }
        catch { }
    }

    private static void ReadUwpPackagedStartupApps(List<StartupEntry> list, bool es)
    {
        try
        {
            using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var systemAppDataKey = hkcu.OpenSubKey(UwpSystemAppDataPath, writable: false);
            if (systemAppDataKey == null) return;

            var packageLookup = BuildUwpPackageLookup(hkcu);
            string label = es ? "App UWP / MSIX (Store)" : "UWP / MSIX App (Store)";

            foreach (var packageFamilyName in systemAppDataKey.GetSubKeyNames())
            {
                if (string.IsNullOrWhiteSpace(packageFamilyName)) continue;

                try
                {
                    using var pkgKey = systemAppDataKey.OpenSubKey(packageFamilyName, writable: false);
                    if (pkgKey == null) continue;

                    var startupTasks = new List<(string TaskId, int State)>();
                    foreach (var subKeyName in pkgKey.GetSubKeyNames())
                    {
                        if (string.IsNullOrWhiteSpace(subKeyName) || NonStartupSystemAppDataSubKeys.Contains(subKeyName))
                            continue;

                        try
                        {
                            using var taskKey = pkgKey.OpenSubKey(subKeyName, writable: false);
                            if (taskKey == null) continue;

                            object? stateObj = taskKey.GetValue("State");
                            if (stateObj is int stateInt && stateInt is >= 0 and <= 4)
                            {
                                startupTasks.Add((subKeyName, stateInt));
                            }
                        }
                        catch { }
                    }

                    if (startupTasks.Count == 0) continue;

                    packageLookup.TryGetValue(packageFamilyName, out var pkgInfo);
                    var manifestInfo = TryParseAppxManifest(pkgInfo);

                    string baseDisplayName = ResolveUwpDisplayName(packageFamilyName, pkgInfo, manifestInfo, taskId: null);

                    foreach (var (taskId, stateInt) in startupTasks)
                    {
                        bool enabled = stateInt is 2 or 4;
                        string? taskSpecificName = ResolveUwpDisplayName(packageFamilyName, pkgInfo, manifestInfo, taskId);
                        string displayName = !string.IsNullOrWhiteSpace(taskSpecificName)
                            ? taskSpecificName
                            : baseDisplayName;

                        if (startupTasks.Count > 1 &&
                            string.Equals(displayName, baseDisplayName, StringComparison.OrdinalIgnoreCase))
                        {
                            displayName = $"{baseDisplayName} ({taskId})";
                        }

                        string exePath = string.Empty;
                        if (manifestInfo != null &&
                            manifestInfo.TaskExecutables.TryGetValue(taskId, out var taskExe) &&
                            !string.IsNullOrWhiteSpace(taskExe))
                        {
                            exePath = taskExe;
                        }
                        else if (!string.IsNullOrWhiteSpace(manifestInfo?.DefaultExecutablePath))
                        {
                            exePath = manifestInfo.DefaultExecutablePath;
                        }

                        string command = !string.IsNullOrWhiteSpace(exePath)
                            ? exePath
                            : $"{packageFamilyName} ({taskId})";

                        ImageSource icon = ResolveUwpIcon(displayName, exePath, manifestInfo?.LogoPath);

                        list.Add(new StartupEntry
                        {
                            Name = displayName,
                            ValueName = taskId,
                            PackageFamilyName = packageFamilyName,
                            Command = command,
                            ExecutablePath = exePath,
                            SourceLocation = label,
                            RegistryKeyPath = $@"{UwpSystemAppDataPath}\{packageFamilyName}\{taskId}",
                            EntryKind = StartupEntryKind.PackagedApp,
                            IsCurrentUser = true,
                            IsStartupFolder = false,
                            IsEnabled = enabled,
                            Icon = icon
                        });
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    public bool ToggleStartupEntry(StartupEntry entry, bool enable)
    {
        try
        {
            switch (entry.EntryKind)
            {
                case StartupEntryKind.PackagedApp:
                    return TogglePackagedAppStartup(entry, enable);

                case StartupEntryKind.StartupFolderUser:
                case StartupEntryKind.StartupFolderCommon:
                    return ToggleStartupFolderEntry(entry, enable);

                case StartupEntryKind.RegistryRun:
                case StartupEntryKind.RegistryRun32:
                default:
                    return ToggleRegistryStartupEntry(entry, enable);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TogglePackagedAppStartup(StartupEntry entry, bool enable)
    {
        if (string.IsNullOrWhiteSpace(entry.RegistryKeyPath)) return false;

        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var taskKey = hkcu.OpenSubKey(entry.RegistryKeyPath, writable: true);
        if (taskKey == null) return false;

        // Windows StartupTaskState enum: 1 = DisabledByUser, 2 = EnabledByUser
        taskKey.SetValue("State", enable ? 2 : 1, RegistryValueKind.DWord);
        if (enable)
        {
            taskKey.SetValue("UserEnabledStartupOnce", 1, RegistryValueKind.DWord);
        }

        entry.IsEnabled = enable;
        return true;
    }

    private static bool ToggleStartupFolderEntry(StartupEntry entry, bool enable)
    {
        if (string.IsNullOrWhiteSpace(entry.Command) || !File.Exists(entry.Command))
            return false;

        // Migrate legacy .disabled files back to their original name so StartupApproved\StartupFolder controls state natively
        if (entry.Command.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            string restoredPath = entry.Command[..^9];
            File.Move(entry.Command, restoredPath, overwrite: true);
            entry.Command = restoredPath;
            entry.ExecutablePath = restoredPath;
            if (string.IsNullOrWhiteSpace(entry.ValueName))
                entry.ValueName = Path.GetFileName(restoredPath);
        }

        string fileName = !string.IsNullOrWhiteSpace(entry.ValueName)
            ? entry.ValueName
            : Path.GetFileName(entry.Command);

        RegistryHive hive = entry.EntryKind == StartupEntryKind.StartupFolderCommon
            ? RegistryHive.LocalMachine
            : RegistryHive.CurrentUser;

        byte[] stateBytes = CreateStartupApprovedBytes(enable);

        using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
        using (var approvedKey = baseKey.CreateSubKey(ApprovedStartupFolderKeyPath, writable: true))
        {
            if (approvedKey == null) return false;
            approvedKey.SetValue(fileName, stateBytes, RegistryValueKind.Binary);
        }

        // Keep any existing HKCU override in sync for CommonStartup items (unless shadowed by a user startup file)
        if (entry.EntryKind == StartupEntryKind.StartupFolderCommon)
        {
            try
            {
                string userStartupFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), fileName);
                if (!File.Exists(userStartupFile))
                {
                    using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                    using var hkcuApproved = hkcu.OpenSubKey(ApprovedStartupFolderKeyPath, writable: true);
                    if (hkcuApproved?.GetValue(fileName) != null)
                    {
                        hkcuApproved.SetValue(fileName, stateBytes, RegistryValueKind.Binary);
                    }
                }
            }
            catch { }
        }

        entry.IsEnabled = enable;
        return true;
    }

    private static bool ToggleRegistryStartupEntry(StartupEntry entry, bool enable)
    {
        RegistryHive hive = entry.IsCurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        string runPath = !string.IsNullOrWhiteSpace(entry.RegistryKeyPath)
            ? entry.RegistryKeyPath
            : (entry.EntryKind == StartupEntryKind.RegistryRun32 ? Run32KeyPath : RunKeyPath);
        string approvedPath = !string.IsNullOrWhiteSpace(entry.ApprovedRegistryKeyPath)
            ? entry.ApprovedRegistryKeyPath
            : (entry.EntryKind == StartupEntryKind.RegistryRun32 ? ApprovedRun32KeyPath : ApprovedRunKeyPath);
        string valueName = !string.IsNullOrWhiteSpace(entry.ValueName) ? entry.ValueName : entry.Name;

        byte[] stateBytes = CreateStartupApprovedBytes(enable);

        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

        // Update Windows StartupApproved\Run or Run32 binary state (same mechanism as Windows 10/11 Task Manager)
        using (var approvedKey = baseKey.CreateSubKey(approvedPath, writable: true))
        {
            if (approvedKey == null) return false;
            approvedKey.SetValue(valueName, stateBytes, RegistryValueKind.Binary);
        }

        // Sync any existing HKCU StartupApproved\Run32 override for 32-bit HKLM entries
        if (entry.EntryKind == StartupEntryKind.RegistryRun32)
        {
            try
            {
                using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                using var hkcuApproved = hkcu.OpenSubKey(ApprovedRun32KeyPath, writable: true);
                if (hkcuApproved?.GetValue(valueName) != null)
                {
                    hkcuApproved.SetValue(valueName, stateBytes, RegistryValueKind.Binary);
                }
            }
            catch { }
        }

        // Ensure value exists in Run when enabled (especially for entries migrated from DisabledBackupKeyPath)
        if (enable)
        {
            if (!string.IsNullOrWhiteSpace(entry.Command))
            {
                using var runKey = baseKey.CreateSubKey(runPath, writable: true);
                runKey?.SetValue(valueName, entry.Command, RegistryValueKind.String);
            }

            if (entry.IsCurrentUser)
            {
                using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                using var backupKey = hkcu.OpenSubKey(DisabledBackupKeyPath, writable: true);
                backupKey?.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }

        entry.IsEnabled = enable;
        return true;
    }

    public bool AddCurrentUserStartupEntry(string name, string executablePath, string arguments = "")
    {
        try
        {
            string cmd = string.IsNullOrWhiteSpace(arguments)
                ? $"\"{executablePath}\""
                : $"\"{executablePath}\" {arguments}";

            using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var runKey = hkcu.CreateSubKey(RunKeyPath, writable: true);
            runKey?.SetValue(name, cmd, RegistryValueKind.String);

            using var approvedKey = hkcu.CreateSubKey(ApprovedRunKeyPath, writable: true);
            approvedKey?.SetValue(name, CreateStartupApprovedBytes(enable: true), RegistryValueKind.Binary);
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
            if (entry.EntryKind == StartupEntryKind.PackagedApp)
                return false;

            if (entry.IsStartupFolder ||
                entry.EntryKind is StartupEntryKind.StartupFolderUser or StartupEntryKind.StartupFolderCommon)
            {
                if (File.Exists(entry.Command))
                    File.Delete(entry.Command);

                string fileName = !string.IsNullOrWhiteSpace(entry.ValueName)
                    ? entry.ValueName
                    : Path.GetFileName(entry.Command);
                RegistryHive folderHive = entry.EntryKind == StartupEntryKind.StartupFolderCommon
                    ? RegistryHive.LocalMachine
                    : RegistryHive.CurrentUser;

                using var folderBase = RegistryKey.OpenBaseKey(folderHive, RegistryView.Registry64);
                using var folderApproved = folderBase.OpenSubKey(ApprovedStartupFolderKeyPath, writable: true);
                folderApproved?.DeleteValue(fileName, throwOnMissingValue: false);
                return true;
            }

            RegistryHive hive = entry.IsCurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
            string runPath = !string.IsNullOrWhiteSpace(entry.RegistryKeyPath)
                ? entry.RegistryKeyPath
                : (entry.EntryKind == StartupEntryKind.RegistryRun32 ? Run32KeyPath : RunKeyPath);
            string approvedPath = !string.IsNullOrWhiteSpace(entry.ApprovedRegistryKeyPath)
                ? entry.ApprovedRegistryKeyPath
                : (entry.EntryKind == StartupEntryKind.RegistryRun32 ? ApprovedRun32KeyPath : ApprovedRunKeyPath);
            string valueName = !string.IsNullOrWhiteSpace(entry.ValueName) ? entry.ValueName : entry.Name;

            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using (var runKey = baseKey.OpenSubKey(runPath, writable: true))
                runKey?.DeleteValue(valueName, throwOnMissingValue: false);

            using (var approvedKey = baseKey.OpenSubKey(approvedPath, writable: true))
                approvedKey?.DeleteValue(valueName, throwOnMissingValue: false);

            if (entry.EntryKind == StartupEntryKind.RegistryRun32)
            {
                try
                {
                    using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                    using var hkcuApproved = hkcu.OpenSubKey(ApprovedRun32KeyPath, writable: true);
                    hkcuApproved?.DeleteValue(valueName, throwOnMissingValue: false);
                }
                catch { }
            }

            if (entry.IsCurrentUser)
            {
                using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                using var backupKey = hkcu.OpenSubKey(DisabledBackupKeyPath, writable: true);
                backupKey?.DeleteValue(valueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsStartupApprovedEnabled(RegistryKey? approvedKey, string valueName, out bool hasValue)
    {
        if (approvedKey?.GetValue(valueName) is byte[] bytes && bytes.Length >= 1)
        {
            hasValue = true;
            return bytes[0] % 2 == 0;
        }

        hasValue = false;
        return true;
    }

    private static byte[] CreateStartupApprovedBytes(bool enable)
    {
        byte[] stateBytes = new byte[12];
        stateBytes[0] = enable ? (byte)0x02 : (byte)0x03;
        if (!enable)
        {
            byte[] fileTimeBytes = BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc());
            Buffer.BlockCopy(fileTimeBytes, 0, stateBytes, 4, 8);
        }
        return stateBytes;
    }

    private sealed class UwpPackageInfo
    {
        public string PackageFullName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string PackageRootFolder { get; init; } = string.Empty;
    }

    private sealed class AppxManifestInfo
    {
        public string DisplayName { get; set; } = string.Empty;
        public string DefaultExecutablePath { get; set; } = string.Empty;
        public string LogoPath { get; set; } = string.Empty;
        public Dictionary<string, string> TaskDisplayNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> TaskExecutables { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, UwpPackageInfo> BuildUwpPackageLookup(RegistryKey hkcu)
    {
        var map = new Dictionary<string, UwpPackageInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var packagesKey = hkcu.OpenSubKey(UwpRepositoryPackagesPath, writable: false);
            if (packagesKey == null) return map;

            foreach (var fullName in packagesKey.GetSubKeyNames())
            {
                string? familyName = ExtractPackageFamilyName(fullName);
                if (string.IsNullOrWhiteSpace(familyName)) continue;

                try
                {
                    using var pkgKey = packagesKey.OpenSubKey(fullName, writable: false);
                    if (pkgKey == null) continue;

                    string displayName = pkgKey.GetValue("DisplayName")?.ToString() ?? string.Empty;
                    string rootFolder = pkgKey.GetValue("PackageRootFolder")?.ToString() ?? string.Empty;

                    var info = new UwpPackageInfo
                    {
                        PackageFullName = fullName,
                        DisplayName = displayName,
                        PackageRootFolder = rootFolder
                    };

                    if (!map.TryGetValue(familyName, out var existing) ||
                        (!Directory.Exists(existing.PackageRootFolder) && Directory.Exists(rootFolder)))
                    {
                        map[familyName] = info;
                    }
                }
                catch { }
            }
        }
        catch { }
        return map;
    }

    private static string? ExtractPackageFamilyName(string packageFullName)
    {
        if (string.IsNullOrWhiteSpace(packageFullName)) return null;
        int firstUnderscore = packageFullName.IndexOf('_');
        int lastUnderscore = packageFullName.LastIndexOf('_');
        if (firstUnderscore <= 0 || lastUnderscore <= firstUnderscore || lastUnderscore >= packageFullName.Length - 1)
            return null;

        return $"{packageFullName[..firstUnderscore]}_{packageFullName[(lastUnderscore + 1)..]}";
    }

    private static AppxManifestInfo? TryParseAppxManifest(UwpPackageInfo? pkgInfo)
    {
        if (pkgInfo == null || string.IsNullOrWhiteSpace(pkgInfo.PackageRootFolder))
            return null;

        try
        {
            string manifestPath = Path.Combine(pkgInfo.PackageRootFolder, "AppxManifest.xml");
            if (!File.Exists(manifestPath)) return null;

            var doc = XDocument.Load(manifestPath);
            var root = doc.Root;
            if (root == null) return null;

            var info = new AppxManifestInfo();

            var props = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Properties");
            if (props != null)
            {
                info.DisplayName = props.Elements().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value?.Trim() ?? string.Empty;
                string? propLogo = props.Elements().FirstOrDefault(e => e.Name.LocalName == "Logo")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(propLogo))
                    info.LogoPath = ResolveAppxAssetPath(pkgInfo.PackageRootFolder, propLogo) ?? string.Empty;
            }

            foreach (var appEl in root.Descendants().Where(e => e.Name.LocalName == "Application"))
            {
                string? appExeRel = appEl.Attribute("Executable")?.Value?.Trim();
                string appExeFull = ResolveRelativePackageFile(pkgInfo.PackageRootFolder, appExeRel);
                if (string.IsNullOrWhiteSpace(info.DefaultExecutablePath) && !string.IsNullOrWhiteSpace(appExeFull))
                {
                    info.DefaultExecutablePath = appExeFull;
                }

                var visualEl = appEl.Elements().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
                if (visualEl != null)
                {
                    string? visualDisplay = visualEl.Attribute("DisplayName")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(info.DisplayName) && !string.IsNullOrWhiteSpace(visualDisplay))
                        info.DisplayName = visualDisplay;

                    string? squareLogo = visualEl.Attribute("Square44x44Logo")?.Value?.Trim()
                                         ?? visualEl.Attribute("Square150x150Logo")?.Value?.Trim();
                    string? resolvedSquare = ResolveAppxAssetPath(pkgInfo.PackageRootFolder, squareLogo);
                    if (!string.IsNullOrWhiteSpace(resolvedSquare))
                        info.LogoPath = resolvedSquare;
                }

                foreach (var extEl in appEl.Descendants().Where(e => e.Name.LocalName == "Extension"))
                {
                    string? extExeRel = extEl.Attribute("Executable")?.Value?.Trim();
                    string extExeFull = ResolveRelativePackageFile(pkgInfo.PackageRootFolder, extExeRel);
                    if (string.IsNullOrWhiteSpace(extExeFull))
                        extExeFull = appExeFull;

                    foreach (var taskEl in extEl.Descendants().Where(e => e.Name.LocalName == "StartupTask"))
                    {
                        string? taskId = taskEl.Attribute("TaskId")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(taskId)) continue;

                        string? taskDisplay = taskEl.Attribute("DisplayName")?.Value?.Trim();
                        if (!string.IsNullOrWhiteSpace(taskDisplay))
                            info.TaskDisplayNames[taskId] = taskDisplay;

                        if (!string.IsNullOrWhiteSpace(extExeFull))
                            info.TaskExecutables[taskId] = extExeFull;
                    }
                }
            }

            return info;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveRelativePackageFile(string rootFolder, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        try
        {
            string full = Path.Combine(rootFolder, relativePath.Replace('/', '\\'));
            return File.Exists(full) ? full : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? ResolveAppxAssetPath(string rootFolder, string? relativeAsset)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || string.IsNullOrWhiteSpace(relativeAsset))
            return null;

        try
        {
            string normalized = Path.Combine(rootFolder, relativeAsset.Replace('/', '\\'));
            if (File.Exists(normalized))
                return normalized;

            string? dir = Path.GetDirectoryName(normalized);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return null;

            string baseName = Path.GetFileNameWithoutExtension(normalized);
            string ext = Path.GetExtension(normalized);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".png";

            string[] preferredSuffixes =
            [
                ".targetsize-32",
                ".targetsize-48",
                ".targetsize-24",
                ".scale-100",
                ".scale-125",
                ".scale-150",
                ".scale-200"
            ];

            foreach (var suffix in preferredSuffixes)
            {
                string candidate = Path.Combine(dir, $"{baseName}{suffix}{ext}");
                if (File.Exists(candidate))
                    return candidate;
            }

            return Directory.EnumerateFiles(dir, $"{baseName}*{ext}").FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveUwpDisplayName(
        string packageFamilyName,
        UwpPackageInfo? pkgInfo,
        AppxManifestInfo? manifestInfo,
        string? taskId)
    {
        string? fullName = pkgInfo?.PackageFullName;

        // 1. Task-specific DisplayName from AppxManifest.xml
        if (!string.IsNullOrWhiteSpace(taskId) &&
            manifestInfo != null &&
            manifestInfo.TaskDisplayNames.TryGetValue(taskId, out var rawTaskName))
        {
            string? resolved = ResolveResourceString(rawTaskName, fullName);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        // 2. Package DisplayName from AppModel\Repository\Packages
        if (!string.IsNullOrWhiteSpace(pkgInfo?.DisplayName))
        {
            string? resolved = ResolveResourceString(pkgInfo.DisplayName, fullName);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        // 3. Manifest DisplayName
        if (!string.IsNullOrWhiteSpace(manifestInfo?.DisplayName))
        {
            string? resolved = ResolveResourceString(manifestInfo.DisplayName, fullName);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        // 4. Clean human-readable fallback derived from PackageFamilyName
        return FormatFallbackPackageName(packageFamilyName);
    }

    private static string? ResolveResourceString(string rawValue, string? packageFullName)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;
        string trimmed = rawValue.Trim();

        if (trimmed.StartsWith("@{", StringComparison.Ordinal))
        {
            return TryLoadIndirectString(trimmed);
        }

        if (trimmed.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(packageFullName)) return null;

            string? direct = TryLoadIndirectString($"@{{{packageFullName}?{trimmed}}}");
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            string keyPart = trimmed["ms-resource:".Length..].TrimStart('/');
            if (!keyPart.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
            {
                return TryLoadIndirectString($"@{{{packageFullName}?ms-resource:///Resources/{keyPart}}}");
            }

            return null;
        }

        return trimmed;
    }

    private static string? TryLoadIndirectString(string indirectSource)
    {
        try
        {
            var sb = new StringBuilder(512);
            int hr = SHLoadIndirectString(indirectSource, sb, (uint)sb.Capacity, IntPtr.Zero);
            if (hr == 0)
            {
                string result = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(result) &&
                    !result.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) &&
                    !result.StartsWith("@{", StringComparison.Ordinal))
                {
                    return result;
                }
            }
        }
        catch { }

        return null;
    }

    private static string FormatFallbackPackageName(string packageFamilyName)
    {
        int lastUnderscore = packageFamilyName.LastIndexOf('_');
        string clean = lastUnderscore > 0 ? packageFamilyName[..lastUnderscore] : packageFamilyName;

        // Strip opaque numeric/hex publisher prefix such as "5319275A.WhatsAppDesktop"
        int firstDot = clean.IndexOf('.');
        if (firstDot > 0 && firstDot < clean.Length - 1)
        {
            string prefix = clean[..firstDot];
            if (prefix.All(c => char.IsDigit(c) || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
            {
                clean = clean[(firstDot + 1)..];
            }
        }

        return clean;
    }

    private static ImageSource ResolveUwpIcon(string displayName, string exePath, string? logoPath)
    {
        if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 32;
                bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
        {
            return IconExtractorService.GetIconForProcess(displayName, exePath);
        }

        return IconExtractorService.CreateModernBadgeIcon(displayName);
    }

    public static void SetSelfStartup(bool enable, bool startMinimized = true)
    {
        try
        {
            string appName = "WinTaskBlocker";
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath)) return;

            string args = startMinimized ? "--startup --minimized" : "--startup";

            // Remove HKCU\Run entry for WinTaskBlocker because Windows blocks requireAdministrator EXEs in HKCU\Run
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                runKey?.DeleteValue(appName, throwOnMissingValue: false);
                using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedRunKeyPath, writable: true);
                approvedKey?.DeleteValue(appName, throwOnMissingValue: false);
            }
            catch { }

            if (enable)
            {
                _ = Task.Run(() =>
                {
                    string? tempXml = null;
                    try
                    {
                        string userId = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                        string exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;

                        string escUser = System.Security.SecurityElement.Escape(userId) ?? userId;
                        string escExe = System.Security.SecurityElement.Escape(exePath) ?? exePath;
                        string escArgs = System.Security.SecurityElement.Escape(args) ?? args;
                        string escDir = System.Security.SecurityElement.Escape(exeDir) ?? exeDir;

                        string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>{escUser}</Author>
    <Description>Win Task Blocker — Automatic Startup Guardian</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{escUser}</UserId>
      <Delay>PT3S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{escUser}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>false</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
    <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{escExe}</Command>
      <Arguments>{escArgs}</Arguments>
      <WorkingDirectory>{escDir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";

                        tempXml = Path.Combine(Path.GetTempPath(), $"WinTaskBlocker_Startup_{Guid.NewGuid():N}.xml");
                        File.WriteAllText(tempXml, taskXml, Encoding.Unicode);

                        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = $"/Create /TN \"{appName}\" /XML \"{tempXml}\" /F",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                        });
                        p?.WaitForExit(3000);

                        if (p == null || p.ExitCode != 0)
                        {
                            using var fallback = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "schtasks.exe",
                                Arguments = $"/Create /TN \"{appName}\" /TR \"\\\"{exePath}\\\" {args}\" /SC ONLOGON /RL HIGHEST /F",
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                            });
                            fallback?.WaitForExit(2000);
                        }
                    }
                    catch { }
                    finally
                    {
                        if (tempXml != null)
                        {
                            try { File.Delete(tempXml); } catch { }
                        }
                    }
                });
            }
            else
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = $"/Delete /TN \"{appName}\" /F",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                        });
                        p?.WaitForExit(2000);
                    }
                    catch { }
                });
            }
        }
        catch { }
    }

    public static string ExtractExecutableFromCommand(string rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand)) return string.Empty;
        string expanded = Environment.ExpandEnvironmentVariables(rawCommand.Trim());

        if (expanded.StartsWith('"'))
        {
            int endQuote = expanded.IndexOf('"', 1);
            if (endQuote > 1)
                return expanded[1..endQuote];
        }

        int exeIdx = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx > 0)
            return expanded[..(exeIdx + 4)].Trim('"');

        return expanded.Split(' ')[0].Trim('"');
    }
}
