using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class WimImageInfo : ObservableObject
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _sizeDisplay = "";
    [ObservableProperty] private string _architecture = "";
    [ObservableProperty] private string _edition = "";

    public string DisplayName => $"[{Index}] {Name} ({Architecture}) — {SizeDisplay}";
}
