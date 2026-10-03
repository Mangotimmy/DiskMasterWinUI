using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Model representing a VHD / VHDX virtual hard disk file and its mount state.
/// </summary>
public partial class VhdDiskItem : ObservableObject
{
    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private long _sizeBytes;

    [ObservableProperty]
    private string _sizeDisplay = "";

    [ObservableProperty]
    private string _format = "VHDX"; // VHD or VHDX

    [ObservableProperty]
    private string _diskType = "Dynamic"; // Dynamic (Expandable) or Fixed

    [ObservableProperty]
    private bool _isAttached;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private int? _diskNumber;

    [ObservableProperty]
    private string _driveLetter = "";

    [ObservableProperty]
    private string _fileSystem = "NTFS";

    [ObservableProperty]
    private string _label = "";

    public string StatusBadge => IsAttached
        ? (string.IsNullOrEmpty(DriveLetter) ? $"[已掛載 Disk {DiskNumber}]" : $"[已掛載 {DriveLetter}:]")
        : "[未掛載]";

    public string Summary => $"{FileName} ({Format}, {DiskType}, {SizeDisplay}) {StatusBadge}";
}
