using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinTaskBlocker.Models;

public class HostEntryItem : INotifyPropertyChanged
{
    private bool _isEnabled = true;
    private string _ipAddress = "0.0.0.0";
    private string _domain = string.Empty;
    private string _comment = string.Empty;

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public string IpAddress
    {
        get => _ipAddress;
        set { _ipAddress = value; OnPropertyChanged(); }
    }

    public string Domain
    {
        get => _domain;
        set { _domain = value; OnPropertyChanged(); }
    }

    public string Comment
    {
        get => _comment;
        set { _comment = value; OnPropertyChanged(); }
    }

    public string ToHostsLine()
    {
        string prefix = IsEnabled ? string.Empty : "# ";
        string commentPart = string.IsNullOrWhiteSpace(Comment) ? string.Empty : $" # {Comment.Trim()}";
        return $"{prefix}{IpAddress.Trim()}\t{Domain.Trim()}{commentPart}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
