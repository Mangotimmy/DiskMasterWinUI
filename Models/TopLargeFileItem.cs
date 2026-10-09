using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class TopLargeFileItem : ObservableObject
{
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _directoryPath = "";
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private string _displaySize = "";
    [ObservableProperty] private string _extension = "";
    [ObservableProperty] private DateTime _lastModified;

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
            return $"{(double)bytes / (1024L * 1024L * 1024L):F2} GB";
        if (bytes >= 1024L * 1024L)
            return $"{(double)bytes / (1024L * 1024L):F1} MB";
        if (bytes >= 1024L)
            return $"{(double)bytes / 1024L:F0} KB";
        return $"{bytes} B";
    }
}
