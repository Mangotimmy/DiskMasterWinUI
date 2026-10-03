using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class BootEntryDetail : ObservableObject
{
    [ObservableProperty] private string _identifier = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _device = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private bool _isDefault;
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isRecovery;
    [ObservableProperty] private string _rawText = "";

    public string DisplayTitle => string.IsNullOrEmpty(Description) ? Identifier : Description;
    public string Subtitle => $"{Identifier} • {(string.IsNullOrEmpty(Device) ? "No device" : Device)}";
    public string BadgeText => IsDefault ? "DEFAULT" : (IsCurrent ? "CURRENT" : "");
}
