using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

/// <summary>A local Windows account or group shown in the shared account picker.</summary>
public partial class LocalAccountItem : ObservableObject
{
    [ObservableProperty] private string _accountName = "";
    [ObservableProperty] private string _accountType = "User";
    [ObservableProperty] private bool? _isEnabled;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _sid = "";
    [ObservableProperty] private string _lastLogonDisplay = "";
    [ObservableProperty] private bool _isAdministrator;
    [ObservableProperty] private string _source = "Local";

    public string StateText => IsEnabled switch
    {
        true => "Enabled",
        false => "Disabled",
        _ => "Unknown"
    };

    public string AdministratorText => IsAdministrator ? "Yes" : "";

    partial void OnIsEnabledChanged(bool? value)
    {
        OnPropertyChanged(nameof(StateText));
    }
}
