using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class DnsPollutionItem : ObservableObject
{
    [ObservableProperty] private string _domain = "";
    [ObservableProperty] private string _localResolvedIps = "";
    [ObservableProperty] private string _dohResolvedIps = "";
    [ObservableProperty] private bool _isPolluted;
    [ObservableProperty] private string _statusBadge = "Normal";
    [ObservableProperty] private string _details = "";
}
