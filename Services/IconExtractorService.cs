using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinTaskBlocker.Models;

namespace WinTaskBlocker.Services;

/// <summary>
/// Extracts native icons from Windows executables (.exe) and caches them.
/// Uses instant in-memory vector badges on startup and resolves native icons asynchronously in the background.
/// </summary>
public static class IconExtractorService
{
    private static readonly ConcurrentDictionary<string, ImageSource> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> KnownPathsCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fast non-blocking icon lookup for instant UI startup.
    /// </summary>
    public static ImageSource GetFastBadgeOrCachedIcon(string processName, string? explicitPath = null)
    {
        string key = !string.IsNullOrWhiteSpace(explicitPath) ? explicitPath : processName;
        if (IconCache.TryGetValue(key, out var cached))
            return cached;

        return CreateModernBadgeIcon(processName);
    }

    /// <summary>
    /// Resolves native icons in the background after the window has already loaded so startup is instantaneous.
    /// </summary>
    public static void ResolveIconsInBackgroundAsync(IEnumerable<BlockedTaskItem> items, Dispatcher dispatcher)
    {
        var list = items.ToList();
        _ = Task.Run(() =>
        {
            foreach (var item in list)
            {
                try
                {
                    string? path = !string.IsNullOrWhiteSpace(item.ExecutablePath) && File.Exists(item.ExecutablePath)
                        ? item.ExecutablePath
                        : TryResolveExecutablePath(item.ProcessName);

                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        dispatcher.InvokeAsync(() =>
                        {
                            item.ExecutablePath = path;
                            item.Icon = GetIconForProcess(item.ProcessName, path);
                        }, DispatcherPriority.Background);
                    }
                }
                catch { }
            }
        });
    }

    public static ImageSource GetIconForProcess(string processName, string? explicitPath = null)
    {
        string key = !string.IsNullOrWhiteSpace(explicitPath) ? explicitPath : processName;
        if (IconCache.TryGetValue(key, out var cached))
            return cached;

        string? resolvedPath = explicitPath;
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            resolvedPath = TryResolveExecutablePath(processName);
        }

        if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
        {
            try
            {
                using Icon? sysIcon = Icon.ExtractAssociatedIcon(resolvedPath);
                if (sysIcon != null)
                {
                    var bmp = Imaging.CreateBitmapSourceFromHIcon(
                        sysIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    IconCache[key] = bmp;
                    IconCache[processName] = bmp;
                    return bmp;
                }
            }
            catch { }
        }

        var fallback = CreateModernBadgeIcon(processName);
        IconCache[key] = fallback;
        return fallback;
    }

    public static string? TryResolveExecutablePath(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        if (KnownPathsCache.TryGetValue(processName, out var cachedPath) && File.Exists(cachedPath))
            return cachedPath;

        string exeName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";

        // 1. Check System32 / Windows directory (Direct O(1) File.Exists)
        string sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exeName);
        if (File.Exists(sys32))
        {
            KnownPathsCache[processName] = sys32;
            return sys32;
        }

        // 2. Check currently running processes first (Fast in-memory check)
        try
        {
            string bareName = Path.GetFileNameWithoutExtension(exeName);
            foreach (var proc in Process.GetProcessesByName(bareName))
            {
                try
                {
                    string? fullPath = proc.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                    {
                        KnownPathsCache[processName] = fullPath;
                        return fullPath;
                    }
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }

        // 3. Check targeted known directories (Bounded depth to avoid heavy disk I/O)
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progFiles86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        string[] candidateFolders =
        [
            Path.Combine(progFiles, "Adobe"),
            Path.Combine(progFiles, "Common Files", "Adobe"),
            Path.Combine(progFiles86, "Common Files", "Adobe"),
            Path.Combine(progFiles, "LGHUB"),
            Path.Combine(progFiles, "AnyDesk"),
            Path.Combine(progFiles86, "AnyDesk")
        ];

        var enumOpts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true,
            MaxRecursionDepth = 3
        };

        foreach (var folder in candidateFolders)
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var match = Directory.EnumerateFiles(folder, exeName, enumOpts).FirstOrDefault();
                if (!string.IsNullOrEmpty(match))
                {
                    KnownPathsCache[processName] = match;
                    return match;
                }
            }
            catch { }
        }

        return null;
    }

    public static ImageSource CreateModernBadgeIcon(string processName)
    {
        string clean = Path.GetFileNameWithoutExtension(processName).Trim();
        string initials = clean.Length >= 2
            ? clean[..2].ToUpperInvariant()
            : (clean.Length == 1 ? clean.ToUpperInvariant() : "EX");

        var lower = clean.ToLowerInvariant();
        System.Windows.Media.Color c1, c2;
        if (lower.Contains("adobe") || lower.Contains("ccx") || lower.Contains("acro"))
        {
            c1 = System.Windows.Media.Color.FromRgb(239, 68, 68);
            c2 = System.Windows.Media.Color.FromRgb(185, 28, 28);
        }
        else if (lower.Contains("lghub") || lower.Contains("logi"))
        {
            c1 = System.Windows.Media.Color.FromRgb(6, 182, 212);
            c2 = System.Windows.Media.Color.FromRgb(37, 99, 235);
        }
        else if (lower.Contains("anydesk") || lower.Contains("logmein"))
        {
            c1 = System.Windows.Media.Color.FromRgb(249, 115, 22);
            c2 = System.Windows.Media.Color.FromRgb(220, 38, 38);
        }
        else if (lower.Contains("gaming"))
        {
            c1 = System.Windows.Media.Color.FromRgb(56, 189, 248);
            c2 = System.Windows.Media.Color.FromRgb(29, 78, 216);
        }
        else
        {
            c1 = System.Windows.Media.Color.FromRgb(14, 165, 233);
            c2 = System.Windows.Media.Color.FromRgb(37, 99, 235);
        }

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var gradient = new LinearGradientBrush(c1, c2, 45);
            var borderPen = new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 255, 255, 255)), 1);
            dc.DrawRoundedRectangle(gradient, borderPen, new Rect(0, 0, 32, 32), 8, 8);

            var text = new FormattedText(
                initials,
                CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                12,
                System.Windows.Media.Brushes.White,
                1.25);

            dc.DrawText(text, new System.Windows.Point((32 - text.Width) / 2, (32 - text.Height) / 2));
        }

        var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }
}
