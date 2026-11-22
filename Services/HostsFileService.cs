using System.Diagnostics;
using System.IO;
using System.Net;
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
        @"^(?<commented>#\s*)?(?<ip>(?:\d{1,3}\.){3}\d{1,3}|[0-9a-fA-F:]*:[0-9a-fA-F:]*)\s+(?<domains>[^#]+?)(?:\s*#\s*(?<comment>.*))?$",
        RegexOptions.Compiled);

    private static readonly Regex ValidHostnameRegex = new(
        @"^[a-zA-Z0-9._-]+$",
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

            // Strictly validate that IP is a genuine IPv4 or IPv6 address (prevents words like "be" in comments from matching)
            if (!IsValidIpAddress(ip)) continue;

            // Ignore Microsoft's built-in sample/documentation lines at the top of the default Windows hosts file
            if (!isEnabled && IsMicrosoftSampleEntry(ip, domainsPart))
                continue;

            var domains = domainsPart.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var domain in domains)
            {
                if (!ValidHostnameRegex.IsMatch(domain)) continue;

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

    private static bool IsValidIpAddress(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        // IPv6 must contain ':'; IPv4 must contain '.'
        if (!ip.Contains('.') && !ip.Contains(':')) return false;
        return IPAddress.TryParse(ip, out _);
    }

    private static bool IsMicrosoftSampleEntry(string ip, string domainsPart)
    {
        // Microsoft's default hosts template examples
        if (ip == "102.54.94.97" && domainsPart.Contains("rhino.acme.com", StringComparison.OrdinalIgnoreCase))
            return true;
        if (ip == "38.25.63.10" && domainsPart.Contains("x.acme.com", StringComparison.OrdinalIgnoreCase))
            return true;
        if ((ip == "127.0.0.1" || ip == "::1") &&
            string.Equals(domainsPart.Trim(), "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
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
        sb.AppendLine("#");
        sb.AppendLine("# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.");
        sb.AppendLine("# localhost name resolution is handled within DNS itself.");
        sb.AppendLine("#\t127.0.0.1       localhost");
        sb.AppendLine("#\t::1             localhost");
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
