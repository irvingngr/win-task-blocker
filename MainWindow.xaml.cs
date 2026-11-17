using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WinTaskBlocker.Models;
using WinTaskBlocker.Services;
using Forms = System.Windows.Forms;

namespace WinTaskBlocker;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly StartupManagerService _startupService = new();
    private readonly ProcessBlockerEngine _engine;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    private readonly ObservableCollection<BlockedTaskItem> _blockedTasks = new();
    private readonly ObservableCollection<ConditionalRule> _conditionalRules = new();
    private readonly ObservableCollection<StartupEntry> _startupEntries = new();
    private readonly ObservableCollection<QuickProfileSidebarItem> _quickProfiles = new();
    private List<ActiveProcessInfo> _cachedActiveProcesses = new();

    private ICollectionView? _blockedTasksView;
    private ICollectionView? _conditionalRulesView;
    private ICollectionView? _startupEntriesView;

    private AppPersistedState _persistedState = new();
    private string _activeProfileFilter = string.Empty;
    private bool _isInitializing = true;
    private Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        _engine = new ProcessBlockerEngine(_configService.LogFilePath);
        _engine.ProcessBlocked += OnProcessBlocked;
        _engine.CountsChanged += OnEngineCountsChanged;
        _loc.LanguageChanged += OnLanguageChanged;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;

        // 1. Load persisted configuration (defaults to English "en")
        _persistedState = _configService.LoadExtendedState();
        _loc.CurrentLanguage = string.IsNullOrWhiteSpace(_persistedState.Preferences.Language)
            ? "en"
            : _persistedState.Preferences.Language;
        UpdateFlagVisual();

        // 2. Load tasks and conditional rules
        foreach (var item in _configService.LoadBlockedTasks())
            _blockedTasks.Add(item);

        foreach (var rule in _persistedState.ConditionalRules)
            _conditionalRules.Add(rule);

        // 3. Setup filterable CollectionViews for all 3 searchable tabs
        _blockedTasksView = CollectionViewSource.GetDefaultView(_blockedTasks);
        _blockedTasksView.Filter = FilterBlockedTaskItem;
        BlockedTasksListBox.ItemsSource = _blockedTasksView;

        _conditionalRulesView = CollectionViewSource.GetDefaultView(_conditionalRules);
        _conditionalRulesView.Filter = FilterConditionalRuleItem;
        ConditionalRulesListBox.ItemsSource = _conditionalRulesView;
        if (_conditionalRules.Count > 0)
            ConditionalRulesListBox.SelectedIndex = 0;

        RefreshStartupEntriesList();
        _startupEntriesView = CollectionViewSource.GetDefaultView(_startupEntries);
        _startupEntriesView.Filter = FilterStartupEntryItem;
        StartupItemsListBox.ItemsSource = _startupEntriesView;

        // 4. Build dynamic Quick Profiles in sidebar
        QuickProfilesItemsControl.ItemsSource = _quickProfiles;
        RebuildQuickProfilesSidebar();

        // 5. Bind preferences & apply saved Crystal Opacity + Crystal Theme
        BindPreferencesToUi();
        ApplyCrystalThemeAndOpacity(_persistedState.Preferences.AccentTheme, _persistedState.Preferences.GlassOpacity, saveNow: false);

        SetupSystemTray();

        // 6. Start Blocker Engine
        _engine.Configure(_blockedTasks, _conditionalRules, _persistedState.Preferences);
        if (_persistedState.Preferences.StartMonitoringOnAppStart)
        {
            _engine.Start();
        }

        _isInitializing = false;
        UpdateEngineStatusUi();
        UpdateCountersUi();

        string initMsg = _loc.IsSpanish
            ? $"[{DateTime.Now:HH:mm:ss}] Sistema iniciado ({_blockedTasks.Count} tareas, {_conditionalRules.Count} reglas condicionales)."
            : $"[{DateTime.Now:HH:mm:ss}] Engine initialized ({_blockedTasks.Count} tasks, {_conditionalRules.Count} conditional rules).";
        ActivityFeedListBox.Items.Insert(0, initMsg);
    }

    public void BringToFrontFromSecondInstance()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    // ==================== LANGUAGE SWITCHING (EN / ES) ====================

    private void LanguageFlagBtn_Click(object sender, RoutedEventArgs e)
    {
        _loc.ToggleLanguage();
        _persistedState.Preferences.Language = _loc.CurrentLanguage;
        SaveAllConfig();
    }

    private void OnLanguageChanged(string newLang)
    {
        UpdateFlagVisual();
        if (_trayOpenItem != null) _trayOpenItem.Text = _loc.TrayOpenText;
        if (_trayPauseItem != null) _trayPauseItem.Text = _loc.TrayPauseResumeText;
        if (_trayExitItem != null) _trayExitItem.Text = _loc.TrayExitText;

        foreach (var item in _blockedTasks)
            item.RefreshLocalization();
        foreach (var rule in _conditionalRules)
            rule.RefreshSummaries();
        foreach (var entry in _startupEntries)
            entry.RefreshLocalization();

        RefreshStartupEntriesList();
        UpdateSearchBarVisibilityAndPlaceholder();
        UpdateEngineStatusUi();
        UpdateCountersUi();
    }

    private void UpdateFlagVisual()
    {
        bool es = _loc.IsSpanish;
        FlagEnglishIcon.Visibility = es ? Visibility.Collapsed : Visibility.Visible;
        FlagSpanishIcon.Visibility = es ? Visibility.Visible : Visibility.Collapsed;
        LanguageCodeText.Text = es ? "ES" : "EN";
    }

    // ==================== DYNAMIC QUICK PROFILES SIDEBAR ====================

    private void RebuildQuickProfilesSidebar()
    {
        _quickProfiles.Clear();
        var addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Add all Conditional Rules / Profiles
        foreach (var rule in _conditionalRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Name)) continue;
            if (addedKeys.Add(rule.Name))
            {
                _quickProfiles.Add(new QuickProfileSidebarItem
                {
                    Name = rule.Name,
                    FilterKey = rule.Name,
                    DotColor = string.IsNullOrWhiteSpace(rule.BadgeColor) ? "#A855F7" : rule.BadgeColor
                });
            }
        }

        // 2. Add any additional distinct ProfileGroups from BlockedTasks
        foreach (var group in _blockedTasks.Select(t => t.ProfileGroup).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(group, "General", StringComparison.OrdinalIgnoreCase)) continue;
            if (addedKeys.Add(group))
            {
                _quickProfiles.Add(new QuickProfileSidebarItem
                {
                    Name = group,
                    FilterKey = group,
                    DotColor = GetColorForGroupName(group)
                });
            }
        }
    }

    private static string GetColorForGroupName(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("adobe")) return "#EF4444";
        if (lower.Contains("logi")) return "#06B6D4";
        if (lower.Contains("remote") || lower.Contains("remoto")) return "#F97316";
        if (lower.Contains("gaming") || lower.Contains("focus")) return "#A855F7";
        return "#38BDF8";
    }

    private void QuickFilterAll_Click(object sender, RoutedEventArgs e)
    {
        _activeProfileFilter = string.Empty;
        NavBlockedBtn.IsChecked = true;
        _blockedTasksView?.Refresh();
    }

    private void DynamicQuickProfileBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string filterKey })
        {
            _activeProfileFilter = filterKey;
            NavBlockedBtn.IsChecked = true;
            _blockedTasksView?.Refresh();

            bool es = _loc.IsSpanish;
            FooterStatusText.Text = es
                ? $"Filtro de perfil activo: {filterKey}"
                : $"Active profile filter: {filterKey}";
        }
    }

    // ==================== SYSTEM TRAY ====================

    private Forms.ToolStripMenuItem? _trayOpenItem;
    private Forms.ToolStripMenuItem? _trayPauseItem;
    private Forms.ToolStripMenuItem? _trayExitItem;

    private void SetupSystemTray()
    {
        try
        {
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            var trayIco = File.Exists(icoPath)
                ? new System.Drawing.Icon(icoPath)
                : (System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? System.Drawing.SystemIcons.Shield);

            _trayIcon = new Forms.NotifyIcon
            {
                Text = "Win Task Blocker",
                Visible = true,
                Icon = trayIco
            };
            _trayIcon.DoubleClick += (_, _) => BringToFrontFromSecondInstance();

            var menu = new Forms.ContextMenuStrip();
            _trayOpenItem = new Forms.ToolStripMenuItem(_loc.TrayOpenText, null, (_, _) => BringToFrontFromSecondInstance());
            _trayPauseItem = new Forms.ToolStripMenuItem(_loc.TrayPauseResumeText, null, (_, _) => ToggleEngine_Click(this, new RoutedEventArgs()));
            _trayExitItem = new Forms.ToolStripMenuItem(_loc.TrayExitText, null, (_, _) =>
            {
                _trayIcon.Visible = false;
                _engine.Dispose();
                SaveAllConfig();
                System.Windows.Application.Current.Shutdown();
            });

            menu.Items.Add(_trayOpenItem);
            menu.Items.Add(_trayPauseItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(_trayExitItem);
            _trayIcon.ContextMenuStrip = menu;
        }
        catch { }
    }

    private void OnProcessBlocked(BlockEventNotification ev)
    {
        Dispatcher.InvokeAsync(() =>
        {
            bool es = _loc.IsSpanish;
            string actionVerb = ev.WasMonitorOnly
                ? (es ? "Detectado (Monitor)" : "Detected (Monitor)")
                : (es ? "Bloqueado" : "Blocked");
            string msg = $"[{ev.Timestamp:HH:mm:ss}] {actionVerb}: {ev.ProcessName} (PID {ev.Pid}) — {ev.Reason}";
            ActivityFeedListBox.Items.Insert(0, msg);
            if (ActivityFeedListBox.Items.Count > 150)
                ActivityFeedListBox.Items.RemoveAt(ActivityFeedListBox.Items.Count - 1);

            FooterStatusText.Text = es
                ? $"Último bloqueo: {ev.ProcessName} a las {ev.Timestamp:HH:mm:ss} ({ev.Reason})"
                : $"Last blocked: {ev.ProcessName} at {ev.Timestamp:HH:mm:ss} ({ev.Reason})";

            if (_persistedState.Preferences.ShowNotificationWhenBlocked && _trayIcon != null)
            {
                string title = es ? "Proceso Bloqueado" : "Process Blocked";
                _trayIcon.ShowBalloonTip(1500, title, $"{ev.ProcessName} ({ev.Reason})", Forms.ToolTipIcon.Info);
            }
        });
    }

    private void OnEngineCountsChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdateCountersUi();
            _configService.SaveBlockedTasks(_blockedTasks);
        });
    }

    private void UpdateCountersUi()
    {
        int activeCount = _blockedTasks.Count(t => t.IsEnabled) + _conditionalRules.Count(r => r.IsEnabled);
        int totalBlocked = _blockedTasks.Sum(t => t.BlockedCount) + _conditionalRules.Sum(r => r.TotalBlockedByRule);

        SidebarActiveRulesCount.Text = activeCount.ToString();
        SidebarTotalBlockedCount.Text = totalBlocked.ToString();
        FooterBlockedSummaryText.Text = $"{_loc.FooterBlockedTotalPrefix} {totalBlocked}";
    }

    private void UpdateEngineStatusUi()
    {
        if (_engine.IsRunning)
        {
            EngineStatusHeaderText.Text = _loc.ProtectionActive;
            EngineStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128));
            HeaderEngineBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(45, 34, 197, 94));
            StartStopEngineBtn.Content = _loc.PauseEngineBtn;
            FooterStatusText.Text = _loc.FooterStatusRunning;
        }
        else
        {
            EngineStatusHeaderText.Text = _loc.EnginePaused;
            EngineStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
            HeaderEngineBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(45, 245, 158, 11));
            StartStopEngineBtn.Content = _loc.StartEngineBtn;
            FooterStatusText.Text = _loc.FooterStatusPaused;
        }
    }

    // ==================== WINDOW CHROME & NAVIGATION ====================

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeWindow_Click(sender, e);
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        SaveAllConfig();
        if (_persistedState.Preferences.MinimizeToTrayOnClose && _trayIcon != null && _trayIcon.Visible)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _trayIcon?.Dispose();
        _engine.Dispose();
    }

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewBlockedTasks == null) return;
        ViewBlockedTasks.Visibility = NavBlockedBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewConditionalRules.Visibility = NavConditionalBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewStartupManager.Visibility = NavStartupBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewPreferences.Visibility = NavPrefsBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ViewAboutCredits.Visibility = NavAboutBtn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        UpdateSearchBarVisibilityAndPlaceholder();
    }

    private void UpdateSearchBarVisibilityAndPlaceholder()
    {
        if (GlobalSearchBorder == null || GlobalSearchPlaceholder == null) return;

        if (NavBlockedBtn.IsChecked == true)
        {
            GlobalSearchBorder.Visibility = Visibility.Visible;
            GlobalSearchPlaceholder.Text = _loc.SearchPlaceholderBlocked;
        }
        else if (NavConditionalBtn.IsChecked == true)
        {
            GlobalSearchBorder.Visibility = Visibility.Visible;
            GlobalSearchPlaceholder.Text = _loc.SearchPlaceholderConditional;
        }
        else if (NavStartupBtn.IsChecked == true)
        {
            GlobalSearchBorder.Visibility = Visibility.Visible;
            GlobalSearchPlaceholder.Text = _loc.SearchPlaceholderStartup;
        }
        else
        {
            // Hide top search bar in Preferences & About tabs where it is not applicable
            GlobalSearchBorder.Visibility = Visibility.Collapsed;
        }
    }

    // ==================== MULTI-TAB FILTERING & PREDICTIVE SEARCH ====================

    private bool FilterBlockedTaskItem(object obj)
    {
        if (obj is not BlockedTaskItem item) return false;

        if (!string.IsNullOrWhiteSpace(_activeProfileFilter))
        {
            // Match either direct ProfileGroup OR if the process belongs to a matching ConditionalRule's TargetProcesses
            bool matchesGroup = string.Equals(item.ProfileGroup, _activeProfileFilter, StringComparison.OrdinalIgnoreCase);
            bool matchesRuleTargets = _conditionalRules.Any(r =>
                string.Equals(r.Name, _activeProfileFilter, StringComparison.OrdinalIgnoreCase) &&
                r.TargetProcesses.Any(tp => string.Equals(tp, item.ProcessName, StringComparison.OrdinalIgnoreCase)));

            if (!matchesGroup && !matchesRuleTargets)
                return false;
        }

        string query = GlobalSearchBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query)) return true;

        return item.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.ProfileGroup.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterConditionalRuleItem(object obj)
    {
        if (obj is not ConditionalRule rule) return false;
        string query = GlobalSearchBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query)) return true;

        return rule.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               rule.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               rule.AnchorProcesses.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
               rule.TargetProcesses.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private bool FilterStartupEntryItem(object obj)
    {
        if (obj is not StartupEntry entry) return false;
        string query = GlobalSearchBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query)) return true;

        return entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               entry.Command.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               entry.SourceLocation.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void GlobalSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool hasText = !string.IsNullOrEmpty(GlobalSearchBox.Text);
        GlobalSearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        ClearSearchBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

        _blockedTasksView?.Refresh();
        _conditionalRulesView?.Refresh();
        _startupEntriesView?.Refresh();
    }

    private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        GlobalSearchBox.Text = string.Empty;
    }

    // ==================== QUICK PREDICTIVE ADD BAR ====================

    private void QuickAddProcessBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string text = QuickAddProcessBox.Text.Trim();
        QuickAddPlaceholder.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;

        if (text.Length < 2)
        {
            PredictivePopup.IsOpen = false;
            return;
        }

        if (_cachedActiveProcesses.Count == 0)
            _cachedActiveProcesses = ProcessBlockerEngine.GetActiveProcessesSnapshot();

        var matches = _cachedActiveProcesses
            .Where(p => p.ProcessName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                        p.Description.Contains(text, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(8)
            .ToList();

        if (matches.Count > 0)
        {
            PredictiveSuggestionsList.ItemsSource = matches;
            PredictivePopup.IsOpen = true;
        }
        else
        {
            PredictivePopup.IsOpen = false;
        }
    }

    private void PredictiveSuggestionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PredictiveSuggestionsList.SelectedItem is ActiveProcessInfo selected)
        {
            QuickAddProcessBox.Text = selected.ProcessName;
            PredictivePopup.IsOpen = false;
            AddProcessByNameOrPath(selected.ProcessName, selected.ExecutablePath, selected.Description);
            QuickAddProcessBox.Text = string.Empty;
        }
    }

    private void QuickAddProcessBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            QuickAddConfirm_Click(sender, e);
        }
    }

    private void QuickAddConfirm_Click(object sender, RoutedEventArgs e)
    {
        string raw = QuickAddProcessBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;
        AddProcessByNameOrPath(raw);
        QuickAddProcessBox.Text = string.Empty;
        PredictivePopup.IsOpen = false;
    }

    private void AddProcessByNameOrPath(string nameOrPath, string? explicitPath = null, string? description = null)
    {
        string fileName = Path.GetFileName(nameOrPath.Trim('"'));
        string exeName = ProcessBlockerEngine.NormalizeExeName(fileName);

        var existing = _blockedTasks.FirstOrDefault(t => string.Equals(t.ProcessName, exeName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.IsEnabled = true;
            BlockedTasksListBox.SelectedItem = existing;
            BlockedTasksListBox.ScrollIntoView(existing);
            return;
        }

        string resolvedPath = explicitPath ?? (File.Exists(nameOrPath) ? nameOrPath : IconExtractorService.TryResolveExecutablePath(exeName) ?? string.Empty);
        var newItem = new BlockedTaskItem
        {
            IsEnabled = true,
            ProcessName = exeName,
            BlockedCount = 0,
            ExecutablePath = resolvedPath,
            ProfileGroup = BlockedTaskItem.InferProfileGroup(exeName),
            Description = !string.IsNullOrWhiteSpace(description) ? description : BlockedTaskItem.InferDefaultDescription(exeName),
            Icon = IconExtractorService.GetIconForProcess(exeName, resolvedPath)
        };

        _blockedTasks.Insert(0, newItem);
        RebuildQuickProfilesSidebar();
        SaveAllConfig();
        UpdateCountersUi();
    }

    // ==================== BLOCKED TASKS ACTIONS ====================

    private void ToggleEngine_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning) _engine.Stop();
        else _engine.Start();
        UpdateEngineStatusUi();
    }

    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        bool enable = SelectAllCheckBox.IsChecked == true;
        foreach (var item in _blockedTasks)
            item.IsEnabled = enable;
        SaveAllConfig();
        UpdateCountersUi();
    }

    private void TaskItemCheckBox_Click(object sender, RoutedEventArgs e)
    {
        SaveAllConfig();
        UpdateCountersUi();
    }

    private void DeleteSingleTaskRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: BlockedTaskItem item })
        {
            _blockedTasks.Remove(item);
            RebuildQuickProfilesSidebar();
            SaveAllConfig();
            UpdateCountersUi();
        }
    }

    private void RemoveSelectedTask_Click(object sender, RoutedEventArgs e)
    {
        if (BlockedTasksListBox.SelectedItem is BlockedTaskItem selected)
        {
            _blockedTasks.Remove(selected);
            RebuildQuickProfilesSidebar();
            SaveAllConfig();
            UpdateCountersUi();
        }
    }

    private void ResetCounters_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _blockedTasks)
            item.BlockedCount = 0;
        foreach (var rule in _conditionalRules)
            rule.TotalBlockedByRule = 0;
        SaveAllConfig();
        UpdateCountersUi();
    }

    private void ImportExportIni_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = _loc.IsSpanish ? "Seleccionar archivo config.ini" : "Select Task Blocker config.ini file",
            Filter = "Task Blocker Config (config.ini)|*.ini|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            var imported = _configService.LoadBlockedTasks(dlg.FileName);
            _blockedTasks.Clear();
            foreach (var item in imported)
                _blockedTasks.Add(item);
            RebuildQuickProfilesSidebar();
            SaveAllConfig();
            UpdateCountersUi();
        }
    }

    // ==================== ADD TASK MODAL ====================

    private void OpenAddTaskDialog_Click(object sender, RoutedEventArgs e)
    {
        AddTaskOverlay.Visibility = Visibility.Visible;
        RefreshActiveProcessesModal_Click(sender, e);
        ModalProcessInputBox.Focus();
    }

    private void CloseAddTaskDialog_Click(object sender, RoutedEventArgs e)
    {
        AddTaskOverlay.Visibility = Visibility.Collapsed;
    }

    private void RefreshActiveProcessesModal_Click(object sender, RoutedEventArgs e)
    {
        _cachedActiveProcesses = ProcessBlockerEngine.GetActiveProcessesSnapshot();
        FilterModalActiveProcesses();
    }

    private void ModalProcessInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterModalActiveProcesses();
    }

    private void FilterModalActiveProcesses()
    {
        string q = ModalProcessInputBox?.Text?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrWhiteSpace(q)
            ? _cachedActiveProcesses
            : _cachedActiveProcesses.Where(p =>
                p.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        ActiveProcessesModalList.ItemsSource = filtered;
        ActiveProcessCountLabel.Text = _loc.IsSpanish
            ? $"Procesos activos mostrados: {filtered.Count}"
            : $"Active processes shown: {filtered.Count}";
    }

    private void ActiveProcessesModalList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActiveProcessesModalList.SelectedItem is ActiveProcessInfo info)
        {
            ModalProcessInputBox.Text = info.ProcessName;
        }
    }

    private void ActiveProcessesModalList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmAddTaskFromModal_Click(sender, e);
    }

    private void BrowseExeFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = _loc.IsSpanish ? "Seleccionar ejecutable (.exe)" : "Select Windows executable (.exe)",
            Filter = "Windows Executables (*.exe)|*.exe|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            AddProcessByNameOrPath(dlg.FileName, dlg.FileName);
            AddTaskOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void ConfirmAddTaskFromModal_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveProcessesModalList.SelectedItem is ActiveProcessInfo selected)
        {
            AddProcessByNameOrPath(selected.ProcessName, selected.ExecutablePath, selected.Description);
            AddTaskOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        string manual = ModalProcessInputBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(manual))
        {
            AddProcessByNameOrPath(manual);
            AddTaskOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // ==================== CONDITIONAL BLOCKING & PROFILES (SYNCED WITH QUICK PROFILES) ====================

    private void ConditionalRulesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConditionalRulesListBox.SelectedItem is not ConditionalRule rule) return;

        RuleNameBox.Text = rule.Name;
        RuleDescBox.Text = rule.Description;
        RuleModeCombo.SelectedIndex = rule.Mode switch
        {
            ConditionalRuleMode.BlockWhenParentInactive => 0,
            ConditionalRuleMode.BlockWhenParentActive => 1,
            ConditionalRuleMode.ProfileAlwaysBlock => 2,
            _ => 0
        };
        RuleKillOnExitCheck.IsChecked = rule.KillOnParentExit;
        RuleAnchorsBox.Text = string.Join(", ", rule.AnchorProcesses);
        RuleTargetsBox.Text = string.Join(", ", rule.TargetProcesses);
    }

    private void ConditionalRuleToggle_Click(object sender, RoutedEventArgs e)
    {
        SaveAllConfig();
        UpdateCountersUi();
    }

    private void CreateNewConditionalRule_Click(object sender, RoutedEventArgs e)
    {
        bool es = _loc.IsSpanish;
        var newRule = new ConditionalRule
        {
            Name = es ? $"Perfil Personalizado {_conditionalRules.Count + 1}" : $"Custom Profile {_conditionalRules.Count + 1}",
            Description = es
                ? "Bloquea procesos secundarios cuando el programa principal no está en ejecución."
                : "Blocks background target processes when the main parent app is not running.",
            BadgeColor = "#38BDF8",
            IsEnabled = true,
            KillOnParentExit = true,
            Mode = ConditionalRuleMode.BlockWhenParentInactive,
            AnchorProcesses = new ObservableCollection<string> { "MainApp.exe" },
            TargetProcesses = new ObservableCollection<string> { "HelperService.exe" }
        };
        _conditionalRules.Add(newRule);
        ConditionalRulesListBox.SelectedItem = newRule;
        RebuildQuickProfilesSidebar();
        SaveAllConfig();
        UpdateCountersUi();
    }

    private void SaveConditionalRuleChanges_Click(object sender, RoutedEventArgs e)
    {
        if (ConditionalRulesListBox.SelectedItem is not ConditionalRule rule) return;

        rule.Name = string.IsNullOrWhiteSpace(RuleNameBox.Text) ? "Profile" : RuleNameBox.Text.Trim();
        rule.Description = RuleDescBox.Text.Trim();
        rule.Mode = RuleModeCombo.SelectedIndex switch
        {
            0 => ConditionalRuleMode.BlockWhenParentInactive,
            1 => ConditionalRuleMode.BlockWhenParentActive,
            _ => ConditionalRuleMode.ProfileAlwaysBlock
        };
        rule.KillOnParentExit = RuleKillOnExitCheck.IsChecked == true;

        rule.AnchorProcesses.Clear();
        foreach (var part in RuleAnchorsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            rule.AnchorProcesses.Add(ProcessBlockerEngine.NormalizeExeName(part));

        rule.TargetProcesses.Clear();
        foreach (var part in RuleTargetsBox.Text.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            rule.TargetProcesses.Add(ProcessBlockerEngine.NormalizeExeName(part));

        rule.BadgeColor = GetColorForGroupName(rule.Name);
        rule.RefreshSummaries();
        _conditionalRulesView?.Refresh();

        // Immediately sync Quick Profiles in the left sidebar!
        RebuildQuickProfilesSidebar();
        SaveAllConfig();

        FooterStatusText.Text = _loc.IsSpanish
            ? $"Perfil '{rule.Name}' actualizado en Perfiles Rápidos."
            : $"Profile '{rule.Name}' saved and updated in Quick Profiles.";
    }

    private void DeleteConditionalRule_Click(object sender, RoutedEventArgs e)
    {
        if (ConditionalRulesListBox.SelectedItem is ConditionalRule rule)
        {
            string deletedName = rule.Name;
            _conditionalRules.Remove(rule);
            if (_conditionalRules.Count > 0)
                ConditionalRulesListBox.SelectedIndex = 0;

            if (string.Equals(_activeProfileFilter, deletedName, StringComparison.OrdinalIgnoreCase))
                _activeProfileFilter = string.Empty;

            // Immediately sync Quick Profiles in the left sidebar!
            RebuildQuickProfilesSidebar();
            SaveAllConfig();
            UpdateCountersUi();
        }
    }

    // ==================== WINDOWS STARTUP MANAGER ====================

    private void RefreshStartupEntriesList()
    {
        _startupEntries.Clear();
        foreach (var entry in _startupService.GetStartupEntries())
            _startupEntries.Add(entry);
    }

    private void RefreshStartupItems_Click(object sender, RoutedEventArgs e) => RefreshStartupEntriesList();

    private void StartupItemToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox { Tag: StartupEntry entry } cb)
        {
            bool targetState = cb.IsChecked == true;
            _startupService.ToggleStartupEntry(entry, targetState);
            FooterStatusText.Text = _loc.IsSpanish
                ? $"Inicio automático de '{entry.Name}': {(targetState ? "Habilitado" : "Deshabilitado")}."
                : $"Startup entry '{entry.Name}': {(targetState ? "Enabled" : "Disabled")}.";
        }
    }

    private void AddStartupProgram_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = _loc.IsSpanish ? "Seleccionar programa (.exe) para agregar al Inicio" : "Select program (.exe) to add to Windows Startup",
            Filter = "Windows Executables (*.exe)|*.exe"
        };

        if (dlg.ShowDialog() == true)
        {
            string name = Path.GetFileNameWithoutExtension(dlg.FileName);
            if (_startupService.AddCurrentUserStartupEntry(name, dlg.FileName))
            {
                RefreshStartupEntriesList();
                FooterStatusText.Text = _loc.IsSpanish
                    ? $"Se agregó '{name}' al Inicio de Windows."
                    : $"Added '{name}' to Windows Startup.";
            }
        }
    }

    private void DeleteStartupItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: StartupEntry entry })
        {
            if (_startupService.DeleteStartupEntry(entry))
            {
                _startupEntries.Remove(entry);
                FooterStatusText.Text = _loc.IsSpanish
                    ? $"Se eliminó '{entry.Name}' del Inicio de Windows."
                    : $"Removed '{entry.Name}' from Windows Startup.";
            }
        }
    }

    // ==================== PREFERENCES & CRYSTAL APPEARANCE (PERSISTED AUTOMATICALLY) ====================

    private void BindPreferencesToUi()
    {
        var p = _persistedState.Preferences;
        PrefLaunchStartup.IsChecked = p.LaunchAtWindowsStartup;
        PrefShowForeground.IsChecked = p.ShowInForegroundOnStart;
        PrefMinimizeTray.IsChecked = p.MinimizeToTrayOnClose;
        PrefStartMonitoring.IsChecked = p.StartMonitoringOnAppStart;
        PrefShowNotification.IsChecked = p.ShowNotificationWhenBlocked;
        PrefMonitorModeOnly.IsChecked = p.MonitorModeOnly;
        PrefFastRealtime.IsChecked = p.FastRealtimeCheck;
        PrefEnableLog.IsChecked = p.EnableLogOnFile;
        PrefWarnLogSize.IsChecked = p.WarnIfLogExceedsSize;
        PrefMaxLogMbBox.Text = p.MaxLogSizeMb.ToString("F1");
        PrefLogEditorBox.Text = p.DefaultLogEditor;
        GlassOpacitySlider.Value = Math.Clamp(p.GlassOpacity, 0.30, 0.95);
        GlassOpacityPercentText.Text = $"{(int)Math.Round(GlassOpacitySlider.Value * 100)}%";
    }

    private void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        var p = _persistedState.Preferences;
        p.LaunchAtWindowsStartup = PrefLaunchStartup.IsChecked == true;
        p.ShowInForegroundOnStart = PrefShowForeground.IsChecked == true;
        p.MinimizeToTrayOnClose = PrefMinimizeTray.IsChecked == true;
        p.StartMonitoringOnAppStart = PrefStartMonitoring.IsChecked == true;
        p.ShowNotificationWhenBlocked = PrefShowNotification.IsChecked == true;
        p.MonitorModeOnly = PrefMonitorModeOnly.IsChecked == true;
        p.FastRealtimeCheck = PrefFastRealtime.IsChecked == true;
        p.EnableLogOnFile = PrefEnableLog.IsChecked == true;
        p.WarnIfLogExceedsSize = PrefWarnLogSize.IsChecked == true;
        if (double.TryParse(PrefMaxLogMbBox.Text, out double maxMb))
            p.MaxLogSizeMb = maxMb;
        p.DefaultLogEditor = PrefLogEditorBox.Text.Trim();
        p.GlassOpacity = GlassOpacitySlider.Value;

        StartupManagerService.SetSelfStartup(p.LaunchAtWindowsStartup);
        _engine.Configure(_blockedTasks, _conditionalRules, p);
        SaveAllConfig();
        FooterStatusText.Text = _loc.IsSpanish
            ? "Preferencias guardadas correctamente."
            : "Preferences saved and applied.";
    }

    private void OpenLogFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(_configService.LogFilePath))
                File.WriteAllText(_configService.LogFilePath, $"# Win Task Blocker Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");

            string editor = string.IsNullOrWhiteSpace(PrefLogEditorBox.Text) ? "notepad.exe" : PrefLogEditorBox.Text.Trim();
            Process.Start(new ProcessStartInfo(editor, $"\"{_configService.LogFilePath}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void GlassOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GradStop1 == null || GradStop2 == null || GradStop3 == null) return;
        if (GlassOpacityPercentText != null)
            GlassOpacityPercentText.Text = $"{(int)Math.Round(e.NewValue * 100)}%";

        if (_isInitializing) return;
        ApplyCrystalThemeAndOpacity(_persistedState.Preferences.AccentTheme, e.NewValue, saveNow: true);
    }

    private void ThemeNebula_Click(object sender, RoutedEventArgs e) =>
        ApplyCrystalThemeAndOpacity("PurpleNebula", GlassOpacitySlider.Value, saveNow: true);

    private void ThemeMidnightDark_Click(object sender, RoutedEventArgs e) =>
        ApplyCrystalThemeAndOpacity("MidnightDark", GlassOpacitySlider.Value, saveNow: true);

    private void ThemeWindowsBlue_Click(object sender, RoutedEventArgs e) =>
        ApplyCrystalThemeAndOpacity("WindowsBlue", GlassOpacitySlider.Value, saveNow: true);

    private void ThemeCyan_Click(object sender, RoutedEventArgs e) =>
        ApplyCrystalThemeAndOpacity("ArcticCyan", GlassOpacitySlider.Value, saveNow: true);

    private void ApplyCrystalThemeAndOpacity(string themeName, double opacity, bool saveNow)
    {
        opacity = Math.Clamp(opacity, 0.30, 0.95);
        byte a = (byte)Math.Clamp(opacity * 255, 65, 242);

        uint tintBgr;
        switch (themeName)
        {
            case "MidnightDark":
                GradStop1.Color = System.Windows.Media.Color.FromArgb(a, 18, 22, 34);
                GradStop2.Color = System.Windows.Media.Color.FromArgb(a, 14, 18, 28);
                GradStop3.Color = System.Windows.Media.Color.FromArgb(a, 10, 13, 21);
                tintBgr = 0x1E1410;
                break;

            case "WindowsBlue":
                // Authentic Windows 11 Sapphire / Fluent Blue Acrylic Glass
                GradStop1.Color = System.Windows.Media.Color.FromArgb(a, 16, 48, 104);
                GradStop2.Color = System.Windows.Media.Color.FromArgb(a, 14, 38, 86);
                GradStop3.Color = System.Windows.Media.Color.FromArgb(a, 11, 25, 58);
                tintBgr = 0x56260E;
                break;

            case "ArcticCyan":
                GradStop1.Color = System.Windows.Media.Color.FromArgb(a, 14, 58, 78);
                GradStop2.Color = System.Windows.Media.Color.FromArgb(a, 15, 40, 64);
                GradStop3.Color = System.Windows.Media.Color.FromArgb(a, 11, 22, 44);
                tintBgr = 0x4E3A0E;
                break;

            default: // "PurpleNebula"
                themeName = "PurpleNebula";
                GradStop1.Color = System.Windows.Media.Color.FromArgb(a, 59, 29, 94);
                GradStop2.Color = System.Windows.Media.Color.FromArgb(a, 43, 25, 77);
                GradStop3.Color = System.Windows.Media.Color.FromArgb(a, 21, 25, 54);
                tintBgr = 0x36192B;
                break;
        }

        // Highlight active theme button border
        HighlightThemeButton(BtnThemeNebula, themeName == "PurpleNebula");
        HighlightThemeButton(BtnThemeDark, themeName == "MidnightDark");
        HighlightThemeButton(BtnThemeWindowsBlue, themeName == "WindowsBlue");
        HighlightThemeButton(BtnThemeCyan, themeName == "ArcticCyan");

        // Update native Windows 10/11 DWM Acrylic Blur behind
        AcrylicWindowHelper.EnableAcrylicBlur(this, alphaOpacity: a, tintBgr: tintBgr);

        _persistedState.Preferences.AccentTheme = themeName;
        _persistedState.Preferences.GlassOpacity = opacity;

        if (saveNow)
        {
            SaveAllConfig();
        }
    }

    private static void HighlightThemeButton(System.Windows.Controls.Button? btn, bool isActive)
    {
        if (btn == null) return;
        btn.BorderBrush = isActive
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(192, 132, 252))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, 255, 255, 255));
        btn.BorderThickness = isActive ? new Thickness(1.6) : new Thickness(1);
    }

    private void SaveAllConfig()
    {
        _configService.SaveBlockedTasks(_blockedTasks);
        _persistedState.ConditionalRules = _conditionalRules.ToList();
        _configService.SaveExtendedState(_persistedState);
    }
}
