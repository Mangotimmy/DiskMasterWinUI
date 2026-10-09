using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class FileExtensionBreakdownItem : ObservableObject
{
    [ObservableProperty] private string _extension = "";
    [ObservableProperty] private long _totalSizeBytes;
    [ObservableProperty] private string _displaySize = "";
    [ObservableProperty] private int _fileCount;
    [ObservableProperty] private double _percentage;
    [ObservableProperty] private string _percentageDisplay = "";
}
