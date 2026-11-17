using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using WinTaskBlocker.Services;

namespace WinTaskBlocker.Models;

public enum ConditionalRuleMode
{
    BlockWhenParentInactive = 0,
    BlockWhenParentActive = 1,
    ProfileAlwaysBlock = 2
}

public class ConditionalRule : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _badgeColor = "#38BDF8";
    private bool _isEnabled = true;
    private bool _killOnParentExit = true;
    private ConditionalRuleMode _mode = ConditionalRuleMode.BlockWhenParentInactive;
    private bool _isParentCurrentlyRunning;
    private int _totalBlockedByRule;

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public string BadgeColor
    {
        get => _badgeColor;
        set { _badgeColor = value; OnPropertyChanged(); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentStateText)); }
    }

    public bool KillOnParentExit
    {
        get => _killOnParentExit;
        set { _killOnParentExit = value; OnPropertyChanged(); }
    }

    public ConditionalRuleMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeDisplayName));
            OnPropertyChanged(nameof(CurrentStateText));
        }
    }

    public ObservableCollection<string> AnchorProcesses { get; set; } = new();
    public ObservableCollection<string> TargetProcesses { get; set; } = new();

    [JsonIgnore]
    public bool IsParentCurrentlyRunning
    {
        get => _isParentCurrentlyRunning;
        set
        {
            if (_isParentCurrentlyRunning == value) return;
            _isParentCurrentlyRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentStateText));
        }
    }

    public int TotalBlockedByRule
    {
        get => _totalBlockedByRule;
        set { _totalBlockedByRule = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public string CurrentStateText
    {
        get
        {
            bool es = LocalizationService.Instance.IsSpanish;
            if (!IsEnabled) return es ? "Desactivado" : "Disabled";

            return Mode switch
            {
                ConditionalRuleMode.BlockWhenParentInactive => IsParentCurrentlyRunning
                    ? (es ? "Permitido (Padre activo)" : "Allowed (Parent active)")
                    : (es ? "Bloqueando (Padre cerrado)" : "Blocking (Parent closed)"),
                ConditionalRuleMode.BlockWhenParentActive => IsParentCurrentlyRunning
                    ? (es ? "Bloqueando (Padre activo)" : "Blocking (Parent active)")
                    : (es ? "En espera (Padre cerrado)" : "Standby (Parent closed)"),
                ConditionalRuleMode.ProfileAlwaysBlock => es ? "Bloqueando grupo" : "Blocking group",
                _ => es ? "Activo" : "Active"
            };
        }
    }

    [JsonIgnore]
    public string AnchorListSummary
    {
        get
        {
            bool es = LocalizationService.Instance.IsSpanish;
            string prefix = es ? "🎯 Programa(s) Padre: " : "🎯 Parent App(s): ";
            string val = AnchorProcesses.Count > 0
                ? string.Join(", ", AnchorProcesses)
                : (es ? "Ninguno (Bloqueo directo de perfil)" : "None (Direct profile bundle)");
            return prefix + val;
        }
    }

    [JsonIgnore]
    public string TargetListSummary
    {
        get
        {
            bool es = LocalizationService.Instance.IsSpanish;
            string prefix = es ? "🚫 Bloquear: " : "🚫 Block: ";
            string val = TargetProcesses.Count > 0
                ? string.Join(", ", TargetProcesses)
                : (es ? "Sin procesos asignados" : "No processes assigned");
            return prefix + val;
        }
    }

    [JsonIgnore]
    public string ModeDisplayName
    {
        get
        {
            var loc = LocalizationService.Instance;
            return Mode switch
            {
                ConditionalRuleMode.BlockWhenParentInactive => loc.ModeOptionInactive,
                ConditionalRuleMode.BlockWhenParentActive => loc.ModeOptionActive,
                ConditionalRuleMode.ProfileAlwaysBlock => loc.ModeOptionAlways,
                _ => loc.ModeOptionInactive
            };
        }
    }

    public void RefreshSummaries()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(AnchorListSummary));
        OnPropertyChanged(nameof(TargetListSummary));
        OnPropertyChanged(nameof(ModeDisplayName));
        OnPropertyChanged(nameof(CurrentStateText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
