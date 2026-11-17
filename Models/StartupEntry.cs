using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WinTaskBlocker.Models;

public class StartupEntry : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _name = string.Empty;
    private string _command = string.Empty;
    private string _executablePath = string.Empty;
    private string _sourceLocation = string.Empty;
    private string _registryKeyPath = string.Empty;
    private bool _isCurrentUser = true;
    private bool _isStartupFolder;
    private ImageSource? _icon;

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Command
    {
        get => _command;
        set { _command = value; OnPropertyChanged(); }
    }

    public string ExecutablePath
    {
        get => _executablePath;
        set { _executablePath = value; OnPropertyChanged(); }
    }

    public string SourceLocation
    {
        get => _sourceLocation;
        set { _sourceLocation = value; OnPropertyChanged(); }
    }

    public string RegistryKeyPath
    {
        get => _registryKeyPath;
        set { _registryKeyPath = value; OnPropertyChanged(); }
    }

    public bool IsCurrentUser
    {
        get => _isCurrentUser;
        set { _isCurrentUser = value; OnPropertyChanged(); }
    }

    public bool IsStartupFolder
    {
        get => _isStartupFolder;
        set { _isStartupFolder = value; OnPropertyChanged(); }
    }

    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); }
    }

    public string StatusText => IsEnabled ? "Habilitado" : "Deshabilitado";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ActiveProcessInfo
{
    public string ProcessName { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string MemoryText { get; set; } = string.Empty;
    public ImageSource? Icon { get; set; }
}
