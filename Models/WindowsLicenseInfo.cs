using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class WindowsLicenseInfo : ObservableObject
{
    [ObservableProperty] private string _biosOemKey = "Not Found / Querying...";
    [ObservableProperty] private string _installedProductKey = "Not Found / Querying...";
    [ObservableProperty] private string _windowsProductName = "";
    [ObservableProperty] private string _windowsEditionId = "";
    [ObservableProperty] private string _biosSerialNumber = "";
    [ObservableProperty] private string _motherboardProduct = "";
    [ObservableProperty] private string _motherboardManufacturer = "";
    [ObservableProperty] private string _offlineProductKey = "";
    [ObservableProperty] private string _offlineWindowsEdition = "";
    [ObservableProperty] private string _rawSummary = "";
}
