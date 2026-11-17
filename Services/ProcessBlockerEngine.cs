using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using WinTaskBlocker.Models;

namespace WinTaskBlocker.Services;

public record BlockEventNotification(string ProcessName, int Pid, string Reason, DateTime Timestamp, bool WasMonitorOnly);

/// <summary>
/// High-efficiency, multi-stage process monitor and blocker engine.
/// Evaluates direct blocked tasks (unconditional priority) and conditional parent/child dependency rules.
/// </summary>
public class ProcessBlockerEngine : IDisposable
{
    private CancellationTokenSource? _loopCts;
    private ManagementEventWatcher? _wmiStartWatcher;
    private volatile bool _isRunning;
    private int _isScanningFlag;

    public bool IsRunning => _isRunning;

    public event Action<BlockEventNotification>? ProcessBlocked;
    public event Action? CountsChanged;

    private IList<BlockedTaskItem> _blockedTasks = new List<BlockedTaskItem>();
    private IList<ConditionalRule> _conditionalRules = new List<ConditionalRule>();
    private AppPreferences _preferences = new();
    private readonly string _logFilePath;

    // Win32 Native APIs for SeDebugPrivilege and direct TerminateProcess
    private const uint PROCESS_TERMINATE = 0x0001;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        int BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

    public ProcessBlockerEngine(string logFilePath)
    {
        _logFilePath = logFilePath;
        TryEnableDebugPrivilege();
    }

    private static void TryEnableDebugPrivilege()
    {
        try
        {
            if (OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
            {
                try
                {
                    if (LookupPrivilegeValue(null, "SeDebugPrivilege", out LUID luid))
                    {
                        var tp = new TOKEN_PRIVILEGES
                        {
                            PrivilegeCount = 1,
                            Luid = luid,
                            Attributes = SE_PRIVILEGE_ENABLED
                        };
                        AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }
        }
        catch { }
    }

    public void Configure(IList<BlockedTaskItem> blockedTasks, IList<ConditionalRule> conditionalRules, AppPreferences preferences)
    {
        _blockedTasks = blockedTasks;
        _conditionalRules = conditionalRules;
        _preferences = preferences;
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        _loopCts?.Cancel();
        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;

        // Dedicated background monitoring loop (runs immediately at 0ms, then every ~250ms)
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    await ScanAndEnforceAsync();
                }
                catch { }

                int delayMs = Math.Clamp(_preferences.ScanIntervalMs, 150, 2000);
                if (_preferences.FastRealtimeCheck)
                    delayMs = Math.Min(delayMs, 250);

                try
                {
                    await Task.Delay(delayMs, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);

        TryStartWmiWatcher();
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        try
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _loopCts = null;
        }
        catch { }
        StopWmiWatcher();
    }

    public void ForceScanNow()
    {
        if (!_isRunning) return;
        _ = Task.Run(ScanAndEnforceAsync);
    }

    private void TryStartWmiWatcher()
    {
        try
        {
            var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
            _wmiStartWatcher = new ManagementEventWatcher(query);
            _wmiStartWatcher.EventArrived += (_, _) => ForceScanNow();
            _wmiStartWatcher.Start();
        }
        catch
        {
            // Dedicated 250ms background loop handles interception when WMI trace is unavailable
        }
    }

    private void StopWmiWatcher()
    {
        try
        {
            _wmiStartWatcher?.Stop();
            _wmiStartWatcher?.Dispose();
            _wmiStartWatcher = null;
        }
        catch { }
    }

    public async Task ScanAndEnforceAsync()
    {
        if (!_isRunning) return;
        if (Interlocked.CompareExchange(ref _isScanningFlag, 1, 0) != 0) return;

        try
        {
            await Task.Run(ExecuteScanPass);
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref _isScanningFlag, 0);
        }
    }

    private void ExecuteScanPass()
    {
        if (!_isRunning) return;

        List<BlockedTaskItem> tasksSnapshot;
        List<ConditionalRule> rulesSnapshot;
        bool monitorOnly = _preferences.MonitorModeOnly;

        try
        {
            tasksSnapshot = _blockedTasks.ToList();
            rulesSnapshot = _conditionalRules.ToList();
        }
        catch
        {
            return;
        }

        Process[] allProcesses;
        try
        {
            allProcesses = Process.GetProcesses();
        }
        catch
        {
            return;
        }

        try
        {
            // 1. Build active process lookup sets:
            // - allRunningExeNames: every running process (normalized & compact)
            // - windowedAnchorExeNames: processes that actually have an active user window (MainWindowHandle != 0)
            //   so headless background preloaders (like background Acrobat.exe) do NOT fool conditional rules into thinking the parent app is open!
            var allRunningExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var windowedAnchorExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in allProcesses)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(p.ProcessName)) continue;
                    string norm = NormalizeExeName(p.ProcessName);
                    string compact = NormalizeCompactKey(norm);
                    allRunningExeNames.Add(norm);
                    allRunningExeNames.Add(compact);

                    bool hasWindow = false;
                    try
                    {
                        hasWindow = p.MainWindowHandle != IntPtr.Zero;
                    }
                    catch { }

                    if (hasWindow)
                    {
                        windowedAnchorExeNames.Add(norm);
                        windowedAnchorExeNames.Add(compact);
                    }
                }
                catch { }
            }

            // 2. Evaluate Conditional Rules
            var conditionallyBlocked = new Dictionary<string, ConditionalRule>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in rulesSnapshot)
            {
                bool anyAnchorActive = rule.AnchorProcesses.Any(a =>
                {
                    string normA = NormalizeExeName(a);
                    string compactA = NormalizeCompactKey(normA);
                    // For GUI parent apps (like Acrobat.exe, Photoshop.exe), require a visible window handle
                    // so background preloaders don't falsely mark the parent app as "open".
                    return windowedAnchorExeNames.Contains(normA) || windowedAnchorExeNames.Contains(compactA);
                });

                if (rule.IsParentCurrentlyRunning != anyAnchorActive)
                {
                    bool capturedState = anyAnchorActive;
                    var capturedRule = rule;
                    RunOnUiThread(() => capturedRule.IsParentCurrentlyRunning = capturedState);
                }

                if (!rule.IsEnabled)
                    continue;

                switch (rule.Mode)
                {
                    case ConditionalRuleMode.BlockWhenParentInactive:
                        if (!anyAnchorActive)
                        {
                            foreach (var target in rule.TargetProcesses)
                            {
                                string normT = NormalizeExeName(target);
                                conditionallyBlocked[normT] = rule;
                                conditionallyBlocked[NormalizeCompactKey(normT)] = rule;
                            }
                        }
                        break;

                    case ConditionalRuleMode.BlockWhenParentActive:
                        if (anyAnchorActive)
                        {
                            foreach (var target in rule.TargetProcesses)
                            {
                                string normT = NormalizeExeName(target);
                                conditionallyBlocked[normT] = rule;
                                conditionallyBlocked[NormalizeCompactKey(normT)] = rule;
                            }
                        }
                        break;

                    case ConditionalRuleMode.ProfileAlwaysBlock:
                        foreach (var target in rule.TargetProcesses)
                        {
                            string normT = NormalizeExeName(target);
                            conditionallyBlocked[normT] = rule;
                            conditionallyBlocked[NormalizeCompactKey(normT)] = rule;
                        }
                        break;
                }
            }

            // 3. Build Direct Blocked Tasks lookup (UNCONDITIONAL PRIORITY)
            // Anything checked in the main "Blocked Tasks List" is ALWAYS blocked!
            var directBlocked = new Dictionary<string, BlockedTaskItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in tasksSnapshot)
            {
                if (item.IsEnabled && !string.IsNullOrWhiteSpace(item.ProcessName))
                {
                    string norm = NormalizeExeName(item.ProcessName);
                    directBlocked[norm] = item;
                    directBlocked[NormalizeCompactKey(norm)] = item;
                }
            }

            // 4. Inspect running processes and terminate any matches
            int currentPid = Environment.ProcessId;
            foreach (var proc in allProcesses)
            {
                try
                {
                    int pid = proc.Id;
                    if (pid == currentPid || pid <= 4) continue;

                    string rawName = proc.ProcessName;
                    if (string.IsNullOrWhiteSpace(rawName)) continue;

                    string exeName = NormalizeExeName(rawName);
                    string compactName = NormalizeCompactKey(exeName);

                    // Direct blocked list ALWAYS wins unconditionally
                    if (!directBlocked.TryGetValue(exeName, out var matchedDirectItem))
                        directBlocked.TryGetValue(compactName, out matchedDirectItem);

                    // Conditional rules match
                    if (!conditionallyBlocked.TryGetValue(exeName, out var matchedRule))
                        conditionallyBlocked.TryGetValue(compactName, out matchedRule);

                    // Special handling for Adobe companion processes disguised under generic names in Task Manager:
                    // - "CCLibrary.exe" ("Creative Cloud Libraries Synchronizer") when CoreSync.exe / Adobe Suite is blocked
                    // - "node.exe" spawned from "Adobe Creative Cloud Experience" ("Creative Cloud Content Manager") when ccxprocess.exe is blocked
                    if (matchedDirectItem == null && matchedRule == null)
                    {
                        if (string.Equals(exeName, "CCLibrary.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!directBlocked.TryGetValue("ccxprocess.exe", out matchedDirectItem))
                                conditionallyBlocked.TryGetValue("CoreSync.exe", out matchedRule);
                        }
                        else if (string.Equals(exeName, "node.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            if (directBlocked.TryGetValue("ccxprocess.exe", out var ccxDirect) ||
                                conditionallyBlocked.TryGetValue("ccxprocess.exe", out matchedRule))
                            {
                                try
                                {
                                    string? modPath = proc.MainModule?.FileName;
                                    string? modDesc = proc.MainModule?.FileVersionInfo?.FileDescription;
                                    if ((!string.IsNullOrEmpty(modPath) &&
                                         (modPath.Contains("Adobe Creative Cloud Experience", StringComparison.OrdinalIgnoreCase) ||
                                          modPath.Contains("CCXProcess", StringComparison.OrdinalIgnoreCase))) ||
                                        (!string.IsNullOrEmpty(modDesc) &&
                                         modDesc.Contains("Creative Cloud Content Manager", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        matchedDirectItem = ccxDirect;
                                    }
                                    else
                                    {
                                        matchedRule = null;
                                    }
                                }
                                catch
                                {
                                    matchedRule = null;
                                }
                            }
                        }
                    }

                    if (matchedDirectItem != null || matchedRule != null)
                    {
                        bool killed = monitorOnly || TryKillProcessRobust(proc);
                        if (killed)
                        {
                            string nowStr = DateTime.Now.ToString("HH:mm:ss");
                            bool es = LocalizationService.Instance.IsSpanish;
                            string reason = matchedDirectItem != null
                                ? (es ? "Lista de Tareas Bloqueadas" : "Blocked Tasks List")
                                : (es ? $"Perfil: {matchedRule!.Name}" : $"Profile: {matchedRule!.Name}");

                            var capturedDirect = matchedDirectItem;
                            var capturedRule = matchedRule;

                            RunOnUiThread(() =>
                            {
                                if (capturedDirect != null)
                                {
                                    capturedDirect.BlockedCount++;
                                    capturedDirect.LastBlockedText = nowStr;
                                }
                                if (capturedRule != null)
                                {
                                    capturedRule.TotalBlockedByRule++;
                                }
                            });

                            var notification = new BlockEventNotification(
                                exeName,
                                pid,
                                reason,
                                DateTime.Now,
                                monitorOnly);

                            WriteLogLineIfEnabled(notification);
                            ProcessBlocked?.Invoke(notification);
                            CountsChanged?.Invoke();
                        }
                    }
                }
                catch { }
            }
        }
        finally
        {
            foreach (var p in allProcesses)
            {
                try { p.Dispose(); } catch { }
            }
        }
    }

    /// <summary>
    /// Multi-stage process termination:
    /// 1. Native Win32 OpenProcess(PROCESS_TERMINATE) + TerminateProcess
    /// 2. .NET Process.Kill() (single process) + best-effort Kill(entireProcessTree: true)
    /// 3. Silent taskkill.exe /F /PID fallback for stubborn service-spawned processes
    /// </summary>
    private static bool TryKillProcessRobust(Process proc)
    {
        int pid;
        try
        {
            pid = proc.Id;
        }
        catch
        {
            return false;
        }

        bool terminated = false;

        // Stage 1: Direct Win32 TerminateProcess (fastest, requires only 0x0001 PROCESS_TERMINATE bit)
        try
        {
            IntPtr hProc = OpenProcess(PROCESS_TERMINATE, false, pid);
            if (hProc != IntPtr.Zero)
            {
                try
                {
                    if (TerminateProcess(hProc, 1))
                        terminated = true;
                }
                finally
                {
                    CloseHandle(hProc);
                }
            }
        }
        catch { }

        // Stage 2: .NET Process.Kill() & best-effort child tree kill
        if (!terminated)
        {
            try
            {
                proc.Kill();
                terminated = true;
            }
            catch { }
        }

        try
        {
            proc.Kill(entireProcessTree: true);
            terminated = true;
        }
        catch { }

        // Stage 3: Fallback to taskkill /F /PID <pid> /T if still alive
        if (!terminated)
        {
            try
            {
                using var tk = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/F /PID {pid} /T",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                tk?.WaitForExit(400);
                if (tk?.ExitCode == 0)
                    terminated = true;
            }
            catch { }
        }

        return terminated;
    }

    private static void RunOnUiThread(Action action)
    {
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.InvokeAsync(action);
            }
            else
            {
                action();
            }
        }
        catch { }
    }

    private void WriteLogLineIfEnabled(BlockEventNotification ev)
    {
        if (!_preferences.EnableLogOnFile) return;
        try
        {
            string modeTag = ev.WasMonitorOnly ? "MONITOR" : "BLOCKED";
            string line = $"[{ev.Timestamp:yyyy-MM-dd HH:mm:ss}] [{modeTag}] {ev.ProcessName} (PID {ev.Pid}) - {ev.Reason}{Environment.NewLine}";
            File.AppendAllText(_logFilePath, line);
        }
        catch { }
    }

    public static string NormalizeExeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        string trimmed = name.Trim().Trim('"');
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ".exe";
    }

    /// <summary>
    /// Removes spaces, hyphens, and underscores for resilient matching (e.g. "adobe crash processor.exe" == "adobecrashprocessor.exe").
    /// </summary>
    public static string NormalizeCompactKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        string norm = NormalizeExeName(name).ToLowerInvariant();
        return norm.Replace(" ", "").Replace("-", "").Replace("_", "");
    }

    public static List<ActiveProcessInfo> GetActiveProcessesSnapshot()
    {
        var result = new List<ActiveProcessInfo>();
        Process[] procs;
        try
        {
            procs = Process.GetProcesses();
        }
        catch
        {
            return result;
        }

        foreach (var p in procs)
        {
            try
            {
                if (p.Id <= 4) continue;
                string exeName = NormalizeExeName(p.ProcessName);
                string fullPath = string.Empty;
                string desc = string.Empty;

                try
                {
                    fullPath = p.MainModule?.FileName ?? string.Empty;
                    desc = p.MainModule?.FileVersionInfo?.FileDescription ?? string.Empty;
                }
                catch { }

                if (string.IsNullOrWhiteSpace(desc))
                {
                    desc = BlockedTaskItem.InferDefaultDescription(exeName);
                }

                double memMb = p.WorkingSet64 / (1024.0 * 1024.0);
                result.Add(new ActiveProcessInfo
                {
                    ProcessName = exeName,
                    Id = p.Id,
                    Description = desc,
                    ExecutablePath = fullPath,
                    MemoryText = $"{memMb:F1} MB",
                    Icon = IconExtractorService.GetIconForProcess(exeName, fullPath)
                });
            }
            catch { }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        }

        return result.OrderBy(r => r.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Dispose()
    {
        Stop();
    }
}
