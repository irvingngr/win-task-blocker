using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinTaskBlocker.Models;

public enum ConditionalRuleMode
{
    /// <summary>
    /// Bloquea los procesos objetivo cuando NINGUNO de los procesos padres/ancla está activo.
    /// (Ej. Bloquear adobeipcbroker.exe y ccxprocess.exe cuando Photoshop/Premiere están cerrados).
    /// </summary>
    BlockWhenParentInactive,

    /// <summary>
    /// Bloquea los procesos objetivo SOLO cuando alguno de los procesos padres está activo.
    /// (Ej. Modo Juego: bloquear actualizadores pesados mientras juegas).
    /// </summary>
    BlockWhenParentActive,

    /// <summary>
    /// Paquete/Perfil directo: bloquea todos los procesos del grupo siempre que el perfil esté activo.
    /// </summary>
    ProfileAlwaysBlock
}

public class ConditionalRule : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _badgeColor = "#A855F7";
    private bool _isEnabled = true;
    private bool _killOnParentExit = true;
    private ConditionalRuleMode _mode = ConditionalRuleMode.BlockWhenParentInactive;
    private string _currentStateText = "Vigilando";
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
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public bool KillOnParentExit
    {
        get => _killOnParentExit;
        set { _killOnParentExit = value; OnPropertyChanged(); }
    }

    public ConditionalRuleMode Mode
    {
        get => _mode;
        set { _mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModeDisplayName)); }
    }

    /// <summary>
    /// Programas principales ("Padres / Ancla") cuya presencia permite o dispara la regla.
    /// Ej: Photoshop.exe, Illustrator.exe, Premiere Pro.exe, AfterFX.exe
    /// </summary>
    public ObservableCollection<string> AnchorProcesses { get; set; } = new();

    /// <summary>
    /// Procesos secundarios/servicios que se bloquearán según el estado de los AnchorProcesses.
    /// Ej: adobe crash processor.exe, adobeipcbroker.exe, ccxprocess.exe
    /// </summary>
    public ObservableCollection<string> TargetProcesses { get; set; } = new();

    public string CurrentStateText
    {
        get => _currentStateText;
        set { _currentStateText = value; OnPropertyChanged(); }
    }

    public bool IsParentCurrentlyRunning
    {
        get => _isParentCurrentlyRunning;
        set { _isParentCurrentlyRunning = value; OnPropertyChanged(); }
    }

    public int TotalBlockedByRule
    {
        get => _totalBlockedByRule;
        set { _totalBlockedByRule = value; OnPropertyChanged(); }
    }

    public string AnchorListSummary => AnchorProcesses.Count > 0
        ? string.Join(", ", AnchorProcesses)
        : "Ninguno (Bloqueo de perfil directo)";

    public string TargetListSummary => TargetProcesses.Count > 0
        ? string.Join(", ", TargetProcesses)
        : "Sin procesos asignados";

    public string ModeDisplayName => Mode switch
    {
        ConditionalRuleMode.BlockWhenParentInactive => "Bloquear si el programa principal NO está activo",
        ConditionalRuleMode.BlockWhenParentActive => "Bloquear mientras el programa principal ESTÉ activo",
        ConditionalRuleMode.ProfileAlwaysBlock => "Perfil / Paquete (Bloquear todo el grupo)",
        _ => "Condicional"
    };

    public void RefreshSummaries()
    {
        OnPropertyChanged(nameof(AnchorListSummary));
        OnPropertyChanged(nameof(TargetListSummary));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
