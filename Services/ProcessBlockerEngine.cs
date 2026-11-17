using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows.Threading;
using WinTaskBlocker.Models;

namespace WinTaskBlocker.Services;

public record BlockEventNotification(string ProcessName, int Pid, string Reason, DateTime Timestamp, bool WasMonitorOnly);

/// <summary>
/// High-efficiency process monitor and blocker engine.
/// Evaluates both direct blocked tasks and conditional parent/child dependency rules.
/// </summary>
public class ProcessBlockerEngine : IDisposable
{
    private readonly DispatcherTimer _scanTimer;
    private ManagementEventWatcher? _wmiStartWatcher;
    private bool _isRunning;
    private int _isScanningFlag;

    public bool IsRunning => _isRunning;

    public event Action<BlockEventNotification>? ProcessBlocked;
    public event Action? CountsChanged;

    private IList<BlockedTaskItem> _blockedTasks = new List<BlockedTaskItem>();
    private IList<ConditionalRule> _conditionalRules = new List<ConditionalRule>();
    private AppPreferences _preferences = new();
    private readonly string _logFilePath;

    public ProcessBlockerEngine(string logFilePath)
    {
        _logFilePath = logFilePath;
        _scanTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _scanTimer.Tick += async (_, _) => await ScanAndEnforceAsync();
    }

    public void Configure(IList<BlockedTaskItem> blockedTasks, IList<ConditionalRule> conditionalRules, AppPreferences preferences)
    {
        _blockedTasks = blockedTasks;
        _conditionalRules = conditionalRules;
        _preferences = preferences;
        _scanTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(preferences.ScanIntervalMs, 150, 5000));
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _scanTimer.Start();
        TryStartWmiWatcher();
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _scanTimer.Stop();
        StopWmiWatcher();
    }

    private void TryStartWmiWatcher()
    {
        try
        {
            var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
            _wmiStartWatcher = new ManagementEventWatcher(query);
            _wmiStartWatcher.EventArrived += async (_, _) => await ScanAndEnforceAsync();
            _wmiStartWatcher.Start();
        }
        catch
        {
            // Win32_ProcessStartTrace requires admin; the low-CPU snapshot timer handles non-admin seamlessly
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
            var tasksSnapshot = _blockedTasks.ToList();
            var rulesSnapshot = _conditionalRules.ToList();
            bool monitorOnly = _preferences.MonitorModeOnly;

            await Task.Run(() =>
            {
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
                    // Build active process name lookup (normalized with .exe)
                    var runningExeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in allProcesses)
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(p.ProcessName))
                                runningExeNames.Add(NormalizeExeName(p.ProcessName));
                        }
                        catch { }
                    }

                    // 1. Evaluate conditional rules state
                    // Processes explicitly allowed right now because their parent anchor is running
                    var conditionallyAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    // Processes that must be blocked right now due to an active conditional rule
                    var conditionallyBlocked = new Dictionary<string, ConditionalRule>(StringComparer.OrdinalIgnoreCase);

                    foreach (var rule in rulesSnapshot)
                    {
                        bool anyAnchorActive = rule.AnchorProcesses.Any(a => runningExeNames.Contains(NormalizeExeName(a)));
                        rule.IsParentCurrentlyRunning = anyAnchorActive;

                        if (!rule.IsEnabled)
                            continue;

                        switch (rule.Mode)
                        {
                            case ConditionalRuleMode.BlockWhenParentInactive:
                                if (anyAnchorActive)
                                {
                                    foreach (var target in rule.TargetProcesses)
                                        conditionallyAllowed.Add(NormalizeExeName(target));
                                }
                                else
                                {
                                    foreach (var target in rule.TargetProcesses)
                                        conditionallyBlocked[NormalizeExeName(target)] = rule;
                                }
                                break;

                            case ConditionalRuleMode.BlockWhenParentActive:
                                if (anyAnchorActive)
                                {
                                    foreach (var target in rule.TargetProcesses)
                                        conditionallyBlocked[NormalizeExeName(target)] = rule;
                                }
                                break;

                            case ConditionalRuleMode.ProfileAlwaysBlock:
                                foreach (var target in rule.TargetProcesses)
                                    conditionallyBlocked[NormalizeExeName(target)] = rule;
                                break;
                        }
                    }

                    // 2. Build direct blocked map
                    var directBlocked = new Dictionary<string, BlockedTaskItem>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in tasksSnapshot)
                    {
                        if (item.IsEnabled)
                            directBlocked[NormalizeExeName(item.ProcessName)] = item;
                    }

                    // 3. Inspect running processes and terminate matches
                    int currentPid = Environment.ProcessId;
                    foreach (var proc in allProcesses)
                    {
                        try
                        {
                            if (proc.Id == currentPid || proc.Id <= 4) continue;
                            string exeName = NormalizeExeName(proc.ProcessName);

                            // If a conditional rule explicitly allows it while parent is open, skip blocking
                            if (conditionallyAllowed.Contains(exeName))
                                continue;

                            directBlocked.TryGetValue(exeName, out var matchedDirectItem);
                            conditionallyBlocked.TryGetValue(exeName, out var matchedRule);

                            if (matchedDirectItem != null || matchedRule != null)
                            {
                                bool killed = false;
                                if (!monitorOnly)
                                {
                                    try
                                    {
                                        proc.Kill(entireProcessTree: true);
                                        killed = true;
                                    }
                                    catch
                                    {
                                        // Process might already be exiting or require elevation
                                    }
                                }
                                else
                                {
                                    killed = true;
                                }

                                if (killed)
                                {
                                    string nowStr = DateTime.Now.ToString("HH:mm:ss");
                                    if (matchedDirectItem != null)
                                    {
                                        matchedDirectItem.BlockedCount++;
                                        matchedDirectItem.LastBlockedText = nowStr;
                                    }

                                    if (matchedRule != null)
                                    {
                                        matchedRule.TotalBlockedByRule++;
                                    }

                                    string reason = matchedRule != null
                                        ? $"Regla: {matchedRule.Name}"
                                        : "Lista de Bloqueo Directo";

                                    var notification = new BlockEventNotification(
                                        exeName,
                                        proc.Id,
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
            });
        }
        finally
        {
            Interlocked.Exchange(ref _isScanningFlag, 0);
        }
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
        string trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ".exe";
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
