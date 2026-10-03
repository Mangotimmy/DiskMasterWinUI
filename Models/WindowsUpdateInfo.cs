using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Status and metadata for Windows Update components, service state, and cache folder footprints.
/// </summary>
public partial class WindowsUpdateInfo : ObservableObject
{
    [ObservableProperty] private string _serviceStatus = "Unknown";
    [ObservableProperty] private string _startupType = "Unknown";
    [ObservableProperty] private bool _isDisabled;
    [ObservableProperty] private bool _isAutoUpdateEnabled = true;
    [ObservableProperty] private bool _isRebootRequired;
    [ObservableProperty] private long _softwareDistributionBytes;
    [ObservableProperty] private string _softwareDistributionDisplay = "0 B";
    [ObservableProperty] private int _softwareDistributionFiles;
    [ObservableProperty] private long _catroot2Bytes;
    [ObservableProperty] private string _catroot2Display = "0 B";
    [ObservableProperty] private string _lastCheckTime = "未記錄";
    [ObservableProperty] private string _statusBadge = "⏳ 偵測中";
}
