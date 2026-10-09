using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class ProcessHunterItem : ObservableObject
{
    [ObservableProperty] private int _pid;
    [ObservableProperty] private int _parentPid;
    [ObservableProperty] private string _processName = "";
    [ObservableProperty] private string _executablePath = "";
    [ObservableProperty] private string _commandLine = "";
    [ObservableProperty] private long _memoryBytes;
    [ObservableProperty] private string _displayMemory = "";
    [ObservableProperty] private bool _isResponding = true;
    [ObservableProperty] private bool _isOrphan = false;
    [ObservableProperty] private bool _isSystemProtected = false;
    [ObservableProperty] private string _statusBadge = "Normal";

    public string HierarchyDisplay => ParentPid > 0 ? $"PID {Pid} (PPID {ParentPid})" : $"PID {Pid}";
}

public partial class SystemMetricsSnapshot : ObservableObject
{
    [ObservableProperty] private double _cpuUsagePercent;
    [ObservableProperty] private double _ramUsagePercent;
    [ObservableProperty] private long _totalRamMb;
    [ObservableProperty] private long _availableRamMb;
    [ObservableProperty] private string _diskReadRateDisplay = "0 KB/s";
    [ObservableProperty] private string _diskWriteRateDisplay = "0 KB/s";
    [ObservableProperty] private string _netThroughputDisplay = "0 KB/s";

    public string DisplayCpuPercent => $"{CpuUsagePercent:F1}%";
    public string DisplayRamPercent => $"{RamUsagePercent:F1}%";

    partial void OnCpuUsagePercentChanged(double value) => OnPropertyChanged(nameof(DisplayCpuPercent));
    partial void OnRamUsagePercentChanged(double value) => OnPropertyChanged(nameof(DisplayRamPercent));
}

public partial class SystemInfoSummary : ObservableObject
{
    [ObservableProperty] private string _hostName = "";
    [ObservableProperty] private string _osName = "";
    [ObservableProperty] private string _osVersion = "";
    [ObservableProperty] private string _systemManufacturer = "";
    [ObservableProperty] private string _systemModel = "";
    [ObservableProperty] private string _biosVersion = "";
    [ObservableProperty] private string _systemBootTime = "";
    [ObservableProperty] private string _uptime = "";
    [ObservableProperty] private string _totalPhysicalMemory = "";
    [ObservableProperty] private string _availablePhysicalMemory = "";
    [ObservableProperty] private int _hotfixCount;
    [ObservableProperty] private List<string> _hotfixes = new();
}
