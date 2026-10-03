using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DiskMasterWinUI.Models;

public enum PartitionBlockType
{
    EfiSystem,
    WindowsBoot,
    PrimaryData,
    Recovery,
    Unallocated
}

public partial class PartitionBlock : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private int _partitionNumber;

    [ObservableProperty] private int _volumeNumber = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private string _driveLetter = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private string _label = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleText))]
    private string _fileSystem = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleText))]
    private string _typeDescription = "";

    [ObservableProperty] private long _sizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleText))]
    private string _sizeDisplay = "";
    [ObservableProperty] private double _proportionalWeight = 1.0;
    [ObservableProperty] private double _displayWidth = 140.0;
    [ObservableProperty] private bool _isResizable = true;
    [ObservableProperty] private PartitionBlockType _blockType = PartitionBlockType.PrimaryData;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isUnallocated;

    public string HeaderText => IsUnallocated
        ? "Unallocated Free Space"
        : string.IsNullOrEmpty(DriveLetter)
            ? $"Partition {PartitionNumber}"
            : $"{DriveLetter}: [{Label}]";

    public string SubtitleText => IsUnallocated
        ? SizeDisplay
        : $"{SizeDisplay} \u2022 {(string.IsNullOrEmpty(FileSystem) ? TypeDescription : FileSystem)}";

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(BorderBrush));
        OnPropertyChanged(nameof(BorderThicknessValue));
    }

    public SolidColorBrush BackgroundBrush => BlockType switch
    {
        PartitionBlockType.EfiSystem => new SolidColorBrush(Color.FromArgb(255, 25, 118, 210)),    // Blue
        PartitionBlockType.WindowsBoot => new SolidColorBrush(Color.FromArgb(255, 0, 137, 123)),   // Teal
        PartitionBlockType.PrimaryData => new SolidColorBrush(Color.FromArgb(255, 46, 125, 50)),   // Green
        PartitionBlockType.Recovery => new SolidColorBrush(Color.FromArgb(255, 230, 81, 0)),      // Amber/Orange
        PartitionBlockType.Unallocated => new SolidColorBrush(Color.FromArgb(255, 66, 66, 66)),   // Charcoal Gray
        _ => new SolidColorBrush(Color.FromArgb(255, 55, 71, 79))
    };

    public SolidColorBrush BorderBrush => IsSelected
        ? new SolidColorBrush(Color.FromArgb(255, 0, 229, 255)) // Bright Cyan Highlight
        : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));

    public Microsoft.UI.Xaml.Thickness BorderThicknessValue => IsSelected
        ? new Microsoft.UI.Xaml.Thickness(2.5)
        : new Microsoft.UI.Xaml.Thickness(1.0);
}
