using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using WinTaskBlocker.Models;

namespace WinTaskBlocker.Services;

/// <summary>
/// Manages reading, editing, backing up, and restoring the Windows hosts file
/// at C:\Windows\System32\drivers\etc\hosts (requires Administrator privileges).
/// </summary>
public class HostsFileService
{
    public string HostsFilePath { get; }
    public string BackupFilePath { get; }

    private static readonly Regex HostLineRegex = new(
        @"^(?<commented>#\s*)?(?<ip>(?:\d{1,3}\.){3}\d{1,3}|[0-9a-fA-F:]+)\s+(?<domains>[^#]+?)(?:\s*#\s*(?<comment>.*))?$",
        RegexOptions.Compiled);

    public HostsFileService()
    {
        string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        HostsFilePath = Path.Combine(sysDir, "drivers", "etc", "hosts");
        BackupFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hosts.backup");
    }

    public bool HasBackup => File.Exists(BackupFilePath);

    public DateTime? GetLastBackupTime()
    {
        if (!File.Exists(BackupFilePath)) return null;
        return File.GetLastWriteTime(BackupFilePath);
    }

    public List<HostEntryItem> LoadEntries()
    {
        var entries = new List<HostEntryItem>();
        if (!File.Exists(HostsFilePath)) return entries;

        foreach (var rawLine in File.ReadAllLines(HostsFilePath, Encoding.UTF8))
        {
            string trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var match = HostLineRegex.Match(trimmed);
            if (!match.Success) continue;

            bool isEnabled = !match.Groups["commented"].Success;
            string ip = match.Groups["ip"].Value.Trim();
            string domainsPart = match.Groups["domains"].Value.Trim();
            string comment = match.Groups["comment"].Value.Trim();

            // Split multiple domains on one line into individual entries for clean UI management
            var domains = domainsPart.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var domain in domains)
            {
                entries.Add(new HostEntryItem
                {
                    IsEnabled = isEnabled,
                    IpAddress = ip,
                    Domain = domain,
                    Comment = comment
                });
            }
        }

        return entries;
    }

    public void SaveEntries(IEnumerable<HostEntryItem> entries)
    {
        // Auto-create an initial backup if none exists yet before first modification
        if (!HasBackup && File.Exists(HostsFilePath))
        {
            CreateBackup();
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Copyright (c) 1993-2009 Microsoft Corp.");
        sb.AppendLine("# Managed by Win Task Blocker (Hosts File Manager)");
        sb.AppendLine($"# Last updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.IpAddress) || string.IsNullOrWhiteSpace(entry.Domain))
                continue;
            sb.AppendLine(entry.ToHostsLine());
        }

        // Write UTF-8 without BOM (standard for Windows hosts file)
        File.WriteAllText(HostsFilePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // Flush Windows DNS cache silently so hosts changes apply immediately
        FlushDnsAsync();
    }

    public void CreateBackup()
    {
        if (!File.Exists(HostsFilePath)) return;
        File.Copy(HostsFilePath, BackupFilePath, overwrite: true);
    }

    public bool RestoreLastBackup()
    {
        if (!File.Exists(BackupFilePath)) return false;
        File.Copy(BackupFilePath, HostsFilePath, overwrite: true);
        FlushDnsAsync();
        return true;
    }

    private static void FlushDnsAsync()
    {
        _ = Task.Run(() =>
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "ipconfig.exe",
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                p?.WaitForExit(1500);
            }
            catch { }
        });
    }
}
