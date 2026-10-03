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

    public string DisplayTitle => $"{PublishedName} ({OriginalFileName})";
    public string Subtitle => $"{DriverClass} | {ProviderName} | v{Version} ({Date})";
}
