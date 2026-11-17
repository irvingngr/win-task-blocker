using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinTaskBlocker.Services;

public class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private string _currentLanguage = "en"; // Starts in English by default

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            string normalized = (value?.ToLowerInvariant() == "es") ? "es" : "en";
            if (_currentLanguage == normalized) return;
            _currentLanguage = normalized;
            OnPropertyChanged(string.Empty); // Refresh all bindings
            LanguageChanged?.Invoke(_currentLanguage);
        }
    }

    public bool IsSpanish => _currentLanguage == "es";
    public event Action<string>? LanguageChanged;

    public void ToggleLanguage()
    {
        CurrentLanguage = IsSpanish ? "en" : "es";
    }

    // Header & Window (Simplified status and engine button labels as requested)
    public string AppSubtitle => IsSpanish ? "v2.0.1 Edición Cristal Acrílico" : "v2.0.1 Acrylic Glass Edition";
    public string ProtectionActive => IsSpanish ? "Bloqueo Activo" : "Block Enabled";
    public string EnginePaused => IsSpanish ? "Bloqueo Desactivado" : "Block Disabled";
    public string PauseEngineBtn => IsSpanish ? "⏹ Detener" : "⏹ Stop";
    public string StartEngineBtn => IsSpanish ? "▶ Iniciar" : "▶ Start";
    public string FlagTooltip => IsSpanish ? "Idioma: Español (Clic para cambiar a Inglés)" : "Language: English (Click to switch to Spanish)";
    public string MinTooltip => IsSpanish ? "Minimizar" : "Minimize";
    public string MaxTooltip => IsSpanish ? "Maximizar / Restaurar" : "Maximize / Restore";
    public string CloseTooltip => IsSpanish ? "Cerrar" : "Close";

    // Sidebar Navigation
    public string ControlPanelHeader => IsSpanish ? "PANEL DE CONTROL" : "CONTROL PANEL";
    public string NavBlockedTasks => IsSpanish ? "Tareas Bloqueadas" : "Blocked Tasks";
    public string NavConditional => IsSpanish ? "Bloqueo Condicional" : "Conditional Rules";
    public string NavStartup => IsSpanish ? "Inicio de Windows" : "Windows Startup";
    public string NavPreferences => IsSpanish ? "Preferencias y Cristal" : "Preferences & Glass";
    public string NavAbout => IsSpanish ? "Acerca de / Créditos" : "About & Credits";
    public string QuickProfilesHeader => IsSpanish ? "PERFILES RÁPIDOS" : "QUICK PROFILES";
    public string AllProcessesLabel => IsSpanish ? "Todos los procesos" : "All Processes";
    public string SessionSummaryHeader => IsSpanish ? "RESUMEN DE SESIÓN" : "SESSION SUMMARY";
    public string ActiveRulesLabel => IsSpanish ? "Reglas Activas" : "Active Rules";
    public string BlockedCountLabel => IsSpanish ? "Bloqueados" : "Blocked";

    // Search Placeholders per Tab
    public string SearchPlaceholderBlocked => IsSpanish
        ? "Buscar procesos bloqueados, descripciones o perfiles..."
        : "Search blocked processes, descriptions or profiles...";
    public string SearchPlaceholderConditional => IsSpanish
        ? "Buscar reglas condicionales, programas padre o procesos..."
        : "Search conditional rules, parent apps or target processes...";
    public string SearchPlaceholderStartup => IsSpanish
        ? "Buscar programas de inicio automático de Windows..."
        : "Search Windows startup programs or registry paths...";

    // View 1: Blocked Tasks
    public string BlockedViewTitle => IsSpanish ? "Lista de Tareas Bloqueadas" : "Blocked Tasks List";
    public string BlockedViewSubtitle => IsSpanish
        ? "Intercepta procesos en tiempo real con íconos nativos, búsqueda predictiva y conteo."
        : "Intercepts processes in real time with native icons, predictive search, and block counters.";
    public string AddTaskBtn => IsSpanish ? "＋ Agregar Tarea" : "＋ Add Task";
    public string QuickAddPlaceholder => IsSpanish
        ? "⚡ Escritura predictiva: escribe el nombre de un proceso (ej. adobe, chrome, anydesk.exe) y presiona Enter..."
        : "⚡ Predictive input: type a process name (e.g. adobe, chrome, anydesk.exe) and press Enter...";
    public string BlockNowBtn => IsSpanish ? "Bloquear Ahora" : "Block Now";
    public string ResetCountersBtn => IsSpanish ? "↺ Reiniciar Conteos" : "↺ Reset Counts";
    public string DeleteSelectedBtn => IsSpanish ? "🗑 Eliminar" : "🗑 Delete";

    // Table Columns
    public string ColIcon => IsSpanish ? "Icono" : "Icon";
    public string ColProcess => IsSpanish ? "Proceso (.exe)" : "Process (.exe)";
    public string ColDescription => IsSpanish ? "Descripción / Ruta" : "Description / Path";
    public string ColProfile => IsSpanish ? "Perfil / Grupo" : "Profile / Group";
    public string ColBlocked => IsSpanish ? "Bloqueados" : "Blocked";
    public string LastBlockedPrefix => IsSpanish ? "Último:" : "Last:";
    public string NeverBlocked => IsSpanish ? "Nunca" : "Never";

    // View 2: Conditional Rules & Profiles
    public string CondViewTitle => IsSpanish
        ? "Bloqueo Condicional y Perfiles"
        : "Conditional Blocking & Profiles";
    public string CondViewSubtitle => IsSpanish
        ? "Bloquea procesos secundarios cuando su programa principal NO está activo, o agrupa varios procesos por perfil."
        : "Block background processes when their main parent application is NOT running, or group processes into profiles.";
    public string NewRuleBtn => IsSpanish ? "＋ Nueva Regla / Perfil" : "＋ New Rule / Profile";
    public string EditorHeader => IsSpanish ? "Configurar Regla / Perfil" : "Configure Rule / Profile";
    public string RuleNameLabel => IsSpanish ? "Nombre del Perfil o Regla:" : "Profile or Rule Name:";
    public string RuleDescLabel => IsSpanish ? "Descripción:" : "Description:";
    public string RuleModeLabel => IsSpanish ? "Modo de Comportamiento:" : "Behavior Mode:";
    public string ModeOptionInactive => IsSpanish
        ? "Bloquear procesos hijos cuando el programa principal NO esté activo"
        : "Block child processes when main parent app is NOT active";
    public string ModeOptionActive => IsSpanish
        ? "Bloquear procesos objetivo SOLO cuando el programa principal ESTÉ activo"
        : "Block target processes ONLY while main parent app IS active";
    public string ModeOptionAlways => IsSpanish
        ? "Perfil / Paquete directo (Bloquear todo el grupo siempre)"
        : "Direct Profile / Bundle (Always block entire group)";
    public string KillOnExitLabel => IsSpanish
        ? "Cerrar procesos residuales automáticamente al cerrar el programa principal"
        : "Automatically terminate residual processes when the main parent app closes";
    public string AnchorsLabel => IsSpanish
        ? "Programas Principales / Padres (separados por coma):"
        : "Main Parent / Anchor Programs (comma-separated):";
    public string AnchorsHint => IsSpanish
        ? "Ej: Photoshop.exe, Illustrator.exe, Premiere Pro.exe"
        : "E.g.: Photoshop.exe, Illustrator.exe, Premiere Pro.exe";
    public string TargetsLabel => IsSpanish
        ? "Procesos Dependientes a Bloquear (separados por coma):"
        : "Dependent Target Processes to Block (comma-separated):";
    public string TargetsHint => IsSpanish
        ? "Ej: adobe crash processor.exe, adobeipcbroker.exe, ccxprocess.exe"
        : "E.g.: adobe crash processor.exe, adobeipcbroker.exe, ccxprocess.exe";
    public string DeleteRuleBtn => IsSpanish ? "Eliminar Regla" : "Delete Rule";
    public string SaveRuleBtn => IsSpanish ? "Guardar Cambios" : "Save Changes";

    // View 3: Windows Startup Manager
    public string StartupViewTitle => IsSpanish
        ? "Gestor de Inicio Automático de Windows"
        : "Windows Automatic Startup Manager";
    public string StartupViewSubtitle => IsSpanish
        ? "Habilita, deshabilita o agrega aplicaciones que arrancan junto con Windows (Registro HKCU/HKLM y Carpeta Startup)."
        : "Enable, disable, or add applications that launch at Windows startup (HKCU/HKLM Registry & Startup Folder).";
    public string RefreshBtn => IsSpanish ? "🔄 Actualizar" : "🔄 Refresh";
    public string AddStartupBtn => IsSpanish ? "＋ Agregar al Inicio" : "＋ Add to Startup";

    // View 4: Preferences & Crystal Appearance
    public string PrefsTitle => IsSpanish ? "Preferencias del Sistema" : "System Preferences";
    public string GeneralSection => IsSpanish ? "OPCIONES GENERALES Y DE ARRANQUE" : "GENERAL & STARTUP OPTIONS";
    public string PrefLaunchStartup => IsSpanish
        ? "Ejecutar al iniciar Windows (Run on Windows startup)"
        : "Run on Windows startup";
    public string PrefStartMinimized => IsSpanish
        ? "Iniciar minimizado en bandeja al arrancar Windows (y activar bloqueo automáticamente)"
        : "Start minimized to tray on Windows startup (and auto-enable blocking)";
    public string PrefShowForeground => IsSpanish
        ? "Mostrar ventana en primer plano al abrir manualmente"
        : "Show window in foreground when launched manually";
    public string PrefMinimizeTray => IsSpanish
        ? "Minimizar a la bandeja del sistema (System Tray) al cerrar"
        : "Minimize to System Tray when closing window";
    public string EngineSection => IsSpanish ? "MOTOR DE TAREAS Y MONITOREO" : "TASK ENGINE & MONITORING";
    public string PrefStartMonitoring => IsSpanish
        ? "Iniciar siempre con el bloqueo activado (incluso si se cerró en Stop)"
        : "Always start with blocking enabled (even if previously stopped)";
    public string PrefShowNotification => IsSpanish
        ? "Mostrar notificación en bandeja cuando se bloquee un proceso"
        : "Show tray notification when a task is blocked";
    public string PrefMonitorMode => IsSpanish
        ? "Modo Monitor (solo contar y registrar sin terminar los procesos)"
        : "Monitor mode only (count and log tasks without killing them)";
    public string PrefRealtime => IsSpanish
        ? "Intercepción en tiempo real de bajo consumo (Eventos WMI + Snapshot rápido)"
        : "Low-CPU real-time interception (WMI Events + Fast Snapshot)";
    public string LogSection => IsSpanish ? "REGISTRO EN ARCHIVO (LOG)" : "FILE LOGGING";
    public string PrefEnableLog => IsSpanish
        ? "Guardar historial de bloqueos en archivo (taskblocker.log)"
        : "Enable logging on file (taskblocker.log)";
    public string PrefWarnLogSize => IsSpanish
        ? "Advertir si el archivo supera (MB):"
        : "Warning if log file size exceeds (MB):";
    public string DefaultLogEditorLabel => IsSpanish ? "Editor de log:" : "Default log editor:";
    public string OpenLogBtn => IsSpanish ? "Abrir Log" : "Open Log";
    public string SavePrefsBtn => IsSpanish ? "💾 Guardar Preferencias" : "💾 Save Preferences";

    // Crystal Appearance Card
    public string CrystalTitle => IsSpanish ? "✨ Personalización del Cristal Acrílico" : "✨ Acrylic Crystal Customization";
    public string CrystalSubtitle => IsSpanish
        ? "Ajusta la opacidad del cristal y elige el acabado de color (cambia bordes, textos acentuados y fondo automáticamente):"
        : "Adjust crystal opacity and select your glass finish (automatically updates borders, accent texts, and background):";
    public string CrystalOpacityLabel => IsSpanish ? "Opacidad del Cristal:" : "Crystal Opacity:";
    public string ThemeWindowsBlue => IsSpanish ? "💠 Azul Windows" : "💠 Windows Blue";
    public string ThemePurpleNebula => IsSpanish ? "🔮 Nebulosa Púrpura" : "🔮 Purple Nebula";
    public string ThemeMidnightDark => IsSpanish ? "🌑 Cristal Oscuro" : "🌑 Midnight Dark";
    public string ThemeArcticCyan => IsSpanish ? "❄️ Cian Ártico" : "❄️ Arctic Cyan";
    public string ActivityFeedTitle => IsSpanish ? "Actividad Reciente en Tiempo Real" : "Real-Time Activity Log";

    // Add Task Modal
    public string ModalTitle => IsSpanish ? "Agregar Proceso a la Lista de Bloqueo" : "Add Process to Blocked Tasks List";
    public string ModalSearchLabel => IsSpanish
        ? "Nombre del ejecutable o filtro predictivo en procesos activos:"
        : "Executable name or predictive filter on active processes:";
    public string BrowseFileBtn => IsSpanish ? "📁 Desde Archivo (.exe)..." : "📁 From File (.exe)...";
    public string RefreshActiveBtn => IsSpanish ? "🔄 Refrescar Activos" : "🔄 Refresh Active";
    public string ColMemory => IsSpanish ? "Memoria" : "Memory";
    public string CancelBtn => IsSpanish ? "Cancelar" : "Cancel";
    public string ConfirmAddBtn => IsSpanish ? "＋ Agregar a Bloqueados" : "＋ Add to Blocked";

    // About / Credits Card
    public string AboutHeaderTitle => "Win Task Blocker v2.0.1 — Process & Startup Guardian";
    public string AboutBadgeText => IsSpanish
        ? "Optimizado para Windows 10 y Windows 11 (x64)"
        : "Optimized for Windows 10 & Windows 11 (x64)";
    public string AboutDescription => IsSpanish
        ? "Construido sobre un motor nativo de intercepción de procesos de ultrabajo consumo, rediseñado desde cero con reglas de bloqueo condicional, gestor de inicio automático, íconos nativos en vivo y una interfaz de cristal acrílico para Windows 11."
        : "Built on an ultra-low resource native process interception engine, redesigned from the ground up with conditional dependency rules, Windows startup management, live native icons, and an intuitive Acrylic glass interface for Windows 11.";
    public string AboutDisclaimer => IsSpanish
        ? "Aviso legal: Este software se proporciona \"tal cual\", sin garantía de ningún tipo. El desarrollador no se hace responsable por la interrupción de procesos del sistema agregados manualmente por el usuario."
        : "Disclaimer: This software is provided \"as is\" without warranty. The developer is not responsible for system instability resulting from blocking critical operating system processes.";
    public string CraftedByPrefix => IsSpanish ? "Creado por" : "Crafted by";
    public string PoweredBySuffix => IsSpanish
        ? "• Compatible con config.ini (Task Blocker v1.5) • Control de Versión v2.0.1"
        : "• Legacy config.ini (Task Blocker v1.5) Compatible • Version Control v2.0.1";
    public string CardVersionHeader => IsSpanish ? "CONTROL DE VERSIÓN" : "VERSION CONTROL";
    public string CardVersionTitle => IsSpanish ? "v2.0.1 Estable" : "v2.0.1 Stable";
    public string CardVersionDesc => IsSpanish
        ? "Motor nativo .NET 8.0 WPF + Arranque Rápido ReadyToRun + Cristal Acrílico"
        : ".NET 8.0 Native WPF + ReadyToRun Fast Boot + Win32 DWM Acrylic Glass";
    public string CardAuthorHeader => IsSpanish ? "AUTOR Y DESARROLLADOR" : "DEVELOPER & AUTHOR";
    public string CardBilingualHeader => IsSpanish ? "SOPORTE BILINGÜE" : "BILINGUAL SUPPORT";
    public string CardBilingualDesc => IsSpanish
        ? "Instancia única (Mutex) + Bloqueador condicional en tiempo real"
        : "Single-Instance Mutex + Real-Time Conditional Blocker";

    // System Tray Menu
    public string TrayOpenText => IsSpanish ? "Abrir Win Task Blocker" : "Open Win Task Blocker";
    public string TrayPauseResumeText => IsSpanish ? "Iniciar / Detener Bloqueo" : "Start / Stop Blocking";
    public string TrayExitText => IsSpanish ? "Salir completamente" : "Exit Completely";

    // Footer
    public string FooterStatusRunning => IsSpanish
        ? "Estado: Bloqueo habilitado (interceptando procesos en tiempo real)."
        : "Status: Block Enabled (intercepting processes in real time).";
    public string FooterStatusPaused => IsSpanish
        ? "Estado: Bloqueo desactivado (Detenido)."
        : "Status: Block Disabled (Stopped).";
    public string FooterBlockedTotalPrefix => IsSpanish ? "Tareas Bloqueadas:" : "Blocked Tasks:";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
