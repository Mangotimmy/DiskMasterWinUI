using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class VolumeInfo : ObservableObject
{
    [ObservableProperty] private int _number;
    [ObservableProperty] private string _letter = "";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _fileSystem = "";
    [ObservableProperty] private string _type = "";
    [ObservableProperty] private string _sizeDisplay = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _info = "";

    public string DisplayName => string.IsNullOrEmpty(Letter)
        ? $"Volume {Number} [{Label}]"
        : $"{Letter}: [{Label}]";

    public string Summary => $"Vol {Number}  {Letter}  {Label,-12} {FileSystem,-6} {Type,-12} {SizeDisplay,-8} {Status,-10} {Info}";
}
