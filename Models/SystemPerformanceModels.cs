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

    // Real-Time Disk Performance Metrics
    [ObservableProperty] private double _diskUsagePercent;
    [ObservableProperty] private double _diskReadLatencyMs;
    [ObservableProperty] private double _diskWriteLatencyMs;
    [ObservableProperty] private string _diskReadRateDisplay = "0 KB/s";
    [ObservableProperty] private string _diskWriteRateDisplay = "0 KB/s";
    [ObservableProperty] private string _netThroughputDisplay = "0 KB/s";
    [ObservableProperty] private List<DiskPerformanceItem> _disks = new();

    // Real-Time GPU Performance Metrics & Graphics API
    [ObservableProperty] private double _gpuUsagePercent;
    [ObservableProperty] private long _gpuMemoryUsedMb;
    [ObservableProperty] private long _gpuMemoryTotalMb;
    [ObservableProperty] private string _gpuModelName = "GPU";
    [ObservableProperty] private string _activeGraphicsApi = "DX12";
    [ObservableProperty] private string _foregroundAppName = "Desktop";

    // Hardware Memory (RAM) Type & Clock Speed Metrics (SMBIOS)
    [ObservableProperty] private string _ramMemoryType = "DDR4/DDR5";
    [ObservableProperty] private int _ramSpeedMhz;
    [ObservableProperty] private int _ramModuleCount = 1;
    [ObservableProperty] private string _ramSpecSummary = "偵測中...";

    // Real-Time LAN / Network Performance Metrics
    [ObservableProperty] private double _netDownloadSpeedBps;
    [ObservableProperty] private double _netUploadSpeedBps;
    [ObservableProperty] private string _netAdapterName = "乙太網路 / Wi-Fi";
    [ObservableProperty] private string _netAdapterSpeedDisplay = "連線中";
    [ObservableProperty] private string _netDownloadRateDisplay = "0 KB/s";
    [ObservableProperty] private string _netUploadRateDisplay = "0 KB/s";

    public string DisplayCpuPercent => $"{CpuUsagePercent:F1}%";
    public string DisplayRamPercent => $"{RamUsagePercent:F1}%";
    public string DisplayDiskUsagePercent => $"{DiskUsagePercent:F1}%";
    public string DisplayDiskReadLatency => DiskReadLatencyMs > 0 ? $"{DiskReadLatencyMs:F2} ms" : "0.00 ms";
    public string DisplayDiskWriteLatency => DiskWriteLatencyMs > 0 ? $"{DiskWriteLatencyMs:F2} ms" : "0.00 ms";

    public string DisplayGpuPercent => $"{GpuUsagePercent:F1}%";
    public string DisplayGpuMemory => GpuMemoryTotalMb > 0 ? $"已用: {GpuMemoryUsedMb:N0} MB / 總計: {GpuMemoryTotalMb:N0} MB" : $"已用: {GpuMemoryUsedMb:N0} MB";
    public string DisplayGpuShortVram => GpuMemoryTotalMb > 0
        ? $"{GpuMemoryUsedMb / 1024.0:F1}/{GpuMemoryTotalMb / 1024.0:F0}G"
        : $"{GpuMemoryUsedMb}M";
    public double GpuMemoryPercent => GpuMemoryTotalMb > 0 ? Math.Clamp((double)GpuMemoryUsedMb * 100.0 / GpuMemoryTotalMb, 0, 100) : 0;

    public long UsedRamMb => Math.Max(0, TotalRamMb - AvailableRamMb);
    public string DisplayRamDetail => TotalRamMb > 0 ? $"已用: {UsedRamMb:N0} MB / 總計: {TotalRamMb:N0} MB" : "讀取中...";
    public string DisplayRamSpeed => RamSpeedMhz > 0 ? $"{RamSpeedMhz} MHz" : "Auto MHz";
    public string DisplayRamTypeAndSpeed => RamSpeedMhz > 0 ? $"{RamMemoryType} · {RamSpeedMhz} MHz" : RamMemoryType;

    public string DiskLatencyBadge => (DiskReadLatencyMs + DiskWriteLatencyMs) switch
    {
        <= 1.0 => "⚡ 極速回應 (< 1ms)",
        <= 10.0 => "🟢 存取正常 (< 10ms)",
        <= 25.0 => "🟡 負載較高 (< 25ms)",
        _ => "🔴 延遲過高 (> 25ms)"
    };

    partial void OnCpuUsagePercentChanged(double value) => OnPropertyChanged(nameof(DisplayCpuPercent));
    partial void OnRamUsagePercentChanged(double value) => OnPropertyChanged(nameof(DisplayRamPercent));
    partial void OnDiskUsagePercentChanged(double value) => OnPropertyChanged(nameof(DisplayDiskUsagePercent));
    partial void OnDiskReadLatencyMsChanged(double value)
    {
        OnPropertyChanged(nameof(DisplayDiskReadLatency));
        OnPropertyChanged(nameof(DiskLatencyBadge));
    }
    partial void OnDiskWriteLatencyMsChanged(double value)
    {
        OnPropertyChanged(nameof(DisplayDiskWriteLatency));
        OnPropertyChanged(nameof(DiskLatencyBadge));
    }
    partial void OnAvailableRamMbChanged(long value) => OnPropertyChanged(nameof(DisplayRamDetail));
    partial void OnTotalRamMbChanged(long value) => OnPropertyChanged(nameof(DisplayRamDetail));
    partial void OnRamMemoryTypeChanged(string value) => OnPropertyChanged(nameof(DisplayRamTypeAndSpeed));
    partial void OnRamSpeedMhzChanged(int value)
    {
        OnPropertyChanged(nameof(DisplayRamSpeed));
        OnPropertyChanged(nameof(DisplayRamTypeAndSpeed));
    }
    partial void OnGpuUsagePercentChanged(double value) => OnPropertyChanged(nameof(DisplayGpuPercent));
    partial void OnGpuMemoryUsedMbChanged(long value)
    {
        OnPropertyChanged(nameof(DisplayGpuMemory));
        OnPropertyChanged(nameof(DisplayGpuShortVram));
        OnPropertyChanged(nameof(GpuMemoryPercent));
    }
    partial void OnGpuMemoryTotalMbChanged(long value)
    {
        OnPropertyChanged(nameof(DisplayGpuMemory));
        OnPropertyChanged(nameof(DisplayGpuShortVram));
        OnPropertyChanged(nameof(GpuMemoryPercent));
    }
}

public partial class DiskPerformanceItem : ObservableObject
{
    [ObservableProperty] private int _diskIndex; // 0, 1, 2... or -1 for _Total
    [ObservableProperty] private string _instanceName = ""; // e.g. "0 C:", "1 F:", "_Total"
    [ObservableProperty] private string _displayName = ""; // e.g. "磁碟 0 (C:)"
    [ObservableProperty] private string _modelName = ""; // e.g. "WD_BLACK SN850X 2000GB"
    [ObservableProperty] private string _mediaType = "SSD"; // "NVMe SSD", "SSD", "HDD", "外接磁碟 (USB)"
    [ObservableProperty] private string _capacityDisplay = ""; // "2,000 GB", "500 GB"
    [ObservableProperty] private double _activeTimePercent;
    [ObservableProperty] private double _readLatencyMs;
    [ObservableProperty] private double _writeLatencyMs;
    [ObservableProperty] private string _readRateDisplay = "0 KB/s";
    [ObservableProperty] private string _writeRateDisplay = "0 KB/s";
    [ObservableProperty] private bool _isSelected;

    public string DisplayActivePercent => $"{ActiveTimePercent:F1}%";
    public string DisplayReadLatency => ReadLatencyMs > 0 ? $"{ReadLatencyMs:F2} ms" : "0.00 ms";
    public string DisplayWriteLatency => WriteLatencyMs > 0 ? $"{WriteLatencyMs:F2} ms" : "0.00 ms";

    public string ResponseTimeBadge => (ReadLatencyMs + WriteLatencyMs) switch
    {
        <= 1.0 => "⚡ 極速回應 (< 1ms)",
        <= 10.0 => "🟢 存取正常 (< 10ms)",
        <= 25.0 => "🟡 負載較高 (< 25ms)",
        _ => "🔴 延遲過高 (> 25ms)"
    };

    public string TypeIconGlyph => MediaType.Contains("外接") || MediaType.Contains("USB")
        ? "\uE88E"
        : MediaType.Contains("NVMe") || MediaType.Contains("SSD")
            ? "\uEDA2"
            : "\uEDA3";

    // Rolling 60-second usage history queue for this specific disk
    public Queue<double> UsageHistory { get; } = new(Enumerable.Repeat(0.0, 60));

    partial void OnActiveTimePercentChanged(double value) => OnPropertyChanged(nameof(DisplayActivePercent));
    partial void OnReadLatencyMsChanged(double value)
    {
        OnPropertyChanged(nameof(DisplayReadLatency));
        OnPropertyChanged(nameof(ResponseTimeBadge));
    }
    partial void OnWriteLatencyMsChanged(double value)
    {
        OnPropertyChanged(nameof(DisplayWriteLatency));
        OnPropertyChanged(nameof(ResponseTimeBadge));
    }
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
