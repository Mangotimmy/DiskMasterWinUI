using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class PartitionInfo : ObservableObject
{
    [ObservableProperty] private int _number;
    [ObservableProperty] private int _diskNumber;
    [ObservableProperty] private string _driveLetter = "";
    [ObservableProperty] private string _type = "";
    [ObservableProperty] private string _sizeDisplay = "";
    [ObservableProperty] private string _offsetDisplay = "";
    [ObservableProperty] private long _offsetBytes;

    public string DisplayName => $"Partition {Number}";
    public string Summary => $"Partition {Number}  {Type,-16}  {SizeDisplay,-10}  Offset {OffsetDisplay}";
}
