using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class OemDriverItem : ObservableObject
{
    [ObservableProperty] private string _publishedName = "";
    [ObservableProperty] private string _originalFileName = "";
    [ObservableProperty] private string _inbox = "";
    [ObservableProperty] private string _driverClass = "";
    [ObservableProperty] private string _providerName = "";
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private string _signerName = "";
    [ObservableProperty] private bool _isSelected;

    public string DisplayTitle => $"{PublishedName} ({OriginalFileName})";
    public string Subtitle => $"{DriverClass} | {ProviderName} | v{Version} ({Date})";
}
