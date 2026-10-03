using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class BcdEntry : ObservableObject
{
    [ObservableProperty] private string _identifier = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _device = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _rawText = "";
    [ObservableProperty] private bool _isDefault;

    public string DisplayName => string.IsNullOrEmpty(Description)
        ? Identifier
        : $"{Description} ({Identifier})";
}
