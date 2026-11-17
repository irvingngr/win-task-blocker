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

    private readonly ObservableCollection<BlockedTaskItem> _blockedTasks = new();
    private readonly ObservableCollection<ConditionalRule> _conditionalRules = new();
    private readonly ObservableCollection<StartupEntry> _startupEntries = new();
    private List<ActiveProcessInfo> _cachedActiveProcesses = new();

    private ICollectionView? _blockedTasksView;
    private AppPersistedState _persistedState = new();
    private string _activeProfileFilter = string.Empty;
    private Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        _engine = new ProcessBlockerEngine(_configService.LogFilePath);
        _engine.ProcessBlocked += OnProcessBlocked;
        _engine.CountsChanged += OnEngineCountsChanged;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Enable native Windows 10/11 Acrylic Glass Blur behind the window
        AcrylicWindowHelper.EnableAcrylicBlur(this, alphaOpacity: 165, tintBgr: 0x36192B);

        // Load config.ini + extended JSON state
        _persistedState = _configService.LoadExtendedState();
        foreach (var item in _configService.LoadBlockedTasks())
            _blockedTasks.Add(item);

        foreach (var rule in _persistedState.ConditionalRules)
            _conditionalRules.Add(rule);

        _blockedTasksView = CollectionViewSource.GetDefaultView(_blockedTasks);
        _blockedTasksView.Filter = FilterBlockedTaskItem;
        BlockedTasksListBox.ItemsSource = _blockedTasksView;

        ConditionalRulesListBox.ItemsSource = _conditionalRules;
        if (_conditionalRules.Count > 0)
            ConditionalRulesListBox.SelectedIndex = 0;

        RefreshStartupEntriesList();
        BindPreferencesToUi();
        SetupSystemTray();

        // Configure & Start Blocker Engine
        _engine.Configure(_blockedTasks, _conditionalRules, _persistedState.Preferences);
        if (_persistedState.Preferences.StartMonitoringOnAppStart)
        {
            _engine.Start();
        }
        UpdateEngineStatusUi();
        UpdateCountersUi();

        ActivityFeedListBox.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Sistema iniciado con {_blockedTasks.Count} tareas y {_conditionalRules.Count} reglas condicionales.");
    }

    private void SetupSystemTray()
    {
        try
        {
            _trayIcon = new Forms.NotifyIcon
            {
                Text = "Task Blocker v2.0 - Protección Activa",
                Visible = true,
                Icon = System.Drawing.SystemIcons.Shield
            };
            _trayIcon.DoubleClick += (_, _) =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Abrir Task Blocker", null, (_, _) =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });
            menu.Items.Add("Pausar / Reanudar Protección", null, (_, _) => ToggleEngine_Click(this, new RoutedEventArgs()));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Salir completamente", null, (_, _) =>
            {
                _trayIcon.Visible = false;
                _engine.Dispose();
                SaveAllConfig();
                System.Windows.Application.Current.Shutdown();
            });
            _trayIcon.ContextMenuStrip = menu;
        }
        catch { }
    }

    private void OnProcessBlocked(BlockEventNotification ev)
    {
        Dispatcher.InvokeAsync(() =>
        {
            string actionVerb = ev.WasMonitorOnly ? "Detectado (Monitor)" : "Bloqueado";
            string msg = $"[{ev.Timestamp:HH:mm:ss}] {actionVerb}: {ev.ProcessName} (PID {ev.Pid}) — {ev.Reason}";
            ActivityFeedListBox.Items.Insert(0, msg);
            if (ActivityFeedListBox.Items.Count > 150)
                ActivityFeedListBox.Items.RemoveAt(ActivityFeedListBox.Items.Count - 1);

            FooterStatusText.Text = $"Último bloqueo: {ev.ProcessName} a las {ev.Timestamp:HH:mm:ss} ({ev.Reason})";

            if (_persistedState.Preferences.ShowNotificationWhenBlocked && _trayIcon != null)
            {
                _trayIcon.ShowBalloonTip(1500, "Proceso Bloqueado", $"{ev.ProcessName} fue interceptado ({ev.Reason}).", Forms.ToolTipIcon.Info);
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
        FooterBlockedSummaryText.Text = $"Tareas Bloqueadas: {totalBlocked}";
    }

    private void UpdateEngineStatusUi()
    {
        if (_engine.IsRunning)
        {
            EngineStatusHeaderText.Text = "Protección Activa";
            EngineStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128));
            HeaderEngineBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(45, 34, 197, 94));
            StartStopEngineBtn.Content = "⏸ Pausar Motor";
            FooterStatusText.Text = "Estado: Ejecutando motor de intercepción en tiempo real.";
        }
        else
        {
            EngineStatusHeaderText.Text = "Motor en Pausa";
            EngineStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
            HeaderEngineBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(45, 245, 158, 11));
            StartStopEngineBtn.Content = "▶ Iniciar Motor";
            FooterStatusText.Text = "Estado: Monitoreo en pausa.";
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
    }

    // ==================== FILTERING & PREDICTIVE SEARCH ====================

    private bool FilterBlockedTaskItem(object obj)
    {
        if (obj is not BlockedTaskItem item) return false;

        if (!string.IsNullOrWhiteSpace(_activeProfileFilter) &&
            !string.Equals(item.ProfileGroup, _activeProfileFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string query = GlobalSearchBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query)) return true;

        return item.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.ProfileGroup.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void GlobalSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool hasText = !string.IsNullOrEmpty(GlobalSearchBox.Text);
        GlobalSearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        ClearSearchBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        _blockedTasksView?.Refresh();
    }

    private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        GlobalSearchBox.Text = string.Empty;
    }

    private void QuickFilterAll_Click(object sender, RoutedEventArgs e)
    {
        _activeProfileFilter = string.Empty;
        NavBlockedBtn.IsChecked = true;
        _blockedTasksView?.Refresh();
    }

    private void QuickFilterAdobe_Click(object sender, RoutedEventArgs e)
    {
        _activeProfileFilter = "Suite Adobe";
        NavBlockedBtn.IsChecked = true;
        _blockedTasksView?.Refresh();
    }

    private void QuickFilterLogitech_Click(object sender, RoutedEventArgs e)
    {
        _activeProfileFilter = "Logitech";
        NavBlockedBtn.IsChecked = true;
        _blockedTasksView?.Refresh();
    }

    private void QuickFilterRemote_Click(object sender, RoutedEventArgs e)
    {
        _activeProfileFilter = "Acceso Remoto";
        NavBlockedBtn.IsChecked = true;
        _blockedTasksView?.Refresh();
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
            SaveAllConfig();
            UpdateCountersUi();
        }
    }

    private void RemoveSelectedTask_Click(object sender, RoutedEventArgs e)
    {
        if (BlockedTasksListBox.SelectedItem is BlockedTaskItem selected)
        {
            _blockedTasks.Remove(selected);
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
            Title = "Seleccionar archivo config.ini de Task Blocker",
            Filter = "Configuración Task Blocker (config.ini)|*.ini|Todos los archivos (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            var imported = _configService.LoadBlockedTasks(dlg.FileName);
            _blockedTasks.Clear();
            foreach (var item in imported)
                _blockedTasks.Add(item);
            SaveAllConfig();
            UpdateCountersUi();
        }
    }

    // ==================== ADD TASK MODAL (ACTIVE PROCESSES / FILE / MANUAL) ====================

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
        ActiveProcessCountLabel.Text = $"Procesos activos mostrados: {filtered.Count}";
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
            Title = "Seleccionar ejecutable (.exe) a bloquear",
            Filter = "Ejecutables de Windows (*.exe)|*.exe|Todos los archivos (*.*)|*.*"
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

    // ==================== CONDITIONAL BLOCKING & PROFILES ====================

    private void ConditionalRulesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConditionalRulesListBox.SelectedItem is not ConditionalRule rule) return;

        RuleNameBox.Text = rule.Name;
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
        var newRule = new ConditionalRule
        {
            Name = "Nuevo Perfil Condicional",
            Description = "Bloquea procesos secundarios cuando el programa principal no está en ejecución.",
            IsEnabled = true,
            KillOnParentExit = true,
            Mode = ConditionalRuleMode.BlockWhenParentInactive,
            AnchorProcesses = new ObservableCollection<string> { "ProgramaPrincipal.exe" },
            TargetProcesses = new ObservableCollection<string> { "ServicioSecundario.exe" }
        };
        _conditionalRules.Add(newRule);
        ConditionalRulesListBox.SelectedItem = newRule;
        SaveAllConfig();
        UpdateCountersUi();
    }

    private void SaveConditionalRuleChanges_Click(object sender, RoutedEventArgs e)
    {
        if (ConditionalRulesListBox.SelectedItem is not ConditionalRule rule) return;

        rule.Name = string.IsNullOrWhiteSpace(RuleNameBox.Text) ? "Regla sin nombre" : RuleNameBox.Text.Trim();
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

        rule.RefreshSummaries();
        SaveAllConfig();
        FooterStatusText.Text = $"Regla '{rule.Name}' actualizada correctamente.";
    }

    private void DeleteConditionalRule_Click(object sender, RoutedEventArgs e)
    {
        if (ConditionalRulesListBox.SelectedItem is ConditionalRule rule)
        {
            _conditionalRules.Remove(rule);
            if (_conditionalRules.Count > 0)
                ConditionalRulesListBox.SelectedIndex = 0;
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
        StartupItemsListBox.ItemsSource = _startupEntries;
    }

    private void RefreshStartupItems_Click(object sender, RoutedEventArgs e) => RefreshStartupEntriesList();

    private void StartupItemToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox { Tag: StartupEntry entry } cb)
        {
            bool targetState = cb.IsChecked == true;
            _startupService.ToggleStartupEntry(entry, targetState);
            FooterStatusText.Text = $"Inicio automático de '{entry.Name}': {(targetState ? "Habilitado" : "Deshabilitado")}.";
        }
    }

    private void AddStartupProgram_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Seleccionar programa (.exe) para agregar al Inicio de Windows",
            Filter = "Ejecutables (*.exe)|*.exe"
        };

        if (dlg.ShowDialog() == true)
        {
            string name = Path.GetFileNameWithoutExtension(dlg.FileName);
            if (_startupService.AddCurrentUserStartupEntry(name, dlg.FileName))
            {
                RefreshStartupEntriesList();
                FooterStatusText.Text = $"Se agregó '{name}' al Inicio de Windows.";
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
                FooterStatusText.Text = $"Se eliminó '{entry.Name}' del Inicio de Windows.";
            }
        }
    }

    // ==================== PREFERENCES & GLASS APPEARANCE ====================

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
        GlassOpacitySlider.Value = Math.Clamp(p.GlassOpacity, 0.25, 0.95);
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
        FooterStatusText.Text = "Preferencias guardadas y aplicadas correctamente.";
    }

    private void OpenLogFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(_configService.LogFilePath))
                File.WriteAllText(_configService.LogFilePath, $"# Task Blocker Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");

            string editor = string.IsNullOrWhiteSpace(PrefLogEditorBox.Text) ? "notepad.exe" : PrefLogEditorBox.Text.Trim();
            Process.Start(new ProcessStartInfo(editor, $"\"{_configService.LogFilePath}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void GlassOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GradStop1 == null || GradStop2 == null || GradStop3 == null) return;
        byte a = (byte)Math.Clamp(e.NewValue * 255, 50, 245);
        GradStop1.Color = System.Windows.Media.Color.FromArgb(a, GradStop1.Color.R, GradStop1.Color.G, GradStop1.Color.B);
        GradStop2.Color = System.Windows.Media.Color.FromArgb(a, GradStop2.Color.R, GradStop2.Color.G, GradStop2.Color.B);
        GradStop3.Color = System.Windows.Media.Color.FromArgb(a, GradStop3.Color.R, GradStop3.Color.G, GradStop3.Color.B);
    }

    private void ThemeNebula_Click(object sender, RoutedEventArgs e)
    {
        byte a = (byte)Math.Clamp(GlassOpacitySlider.Value * 255, 50, 245);
        GradStop1.Color = System.Windows.Media.Color.FromArgb(a, 59, 29, 94);
        GradStop2.Color = System.Windows.Media.Color.FromArgb(a, 43, 25, 77);
        GradStop3.Color = System.Windows.Media.Color.FromArgb(a, 21, 25, 54);
    }

    private void ThemeWintoys_Click(object sender, RoutedEventArgs e)
    {
        byte a = (byte)Math.Clamp(GlassOpacitySlider.Value * 255, 50, 245);
        GradStop1.Color = System.Windows.Media.Color.FromArgb(a, 18, 22, 34);
        GradStop2.Color = System.Windows.Media.Color.FromArgb(a, 15, 19, 30);
        GradStop3.Color = System.Windows.Media.Color.FromArgb(a, 10, 14, 23);
    }

    private void ThemeCyan_Click(object sender, RoutedEventArgs e)
    {
        byte a = (byte)Math.Clamp(GlassOpacitySlider.Value * 255, 50, 245);
        GradStop1.Color = System.Windows.Media.Color.FromArgb(a, 15, 52, 77);
        GradStop2.Color = System.Windows.Media.Color.FromArgb(a, 17, 36, 64);
        GradStop3.Color = System.Windows.Media.Color.FromArgb(a, 12, 20, 44);
    }

    private void SaveAllConfig()
    {
        _configService.SaveBlockedTasks(_blockedTasks);
        _persistedState.ConditionalRules = _conditionalRules.ToList();
        _configService.SaveExtendedState(_persistedState);
    }
}
