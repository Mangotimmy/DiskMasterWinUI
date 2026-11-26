using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public enum StartupSourceKind
{
    HkcuRun,
    HklmRun,
    HklmWow64Run,
    UserStartupFolder,
    CommonStartupFolder
}

/// <summary>
/// Represents a Windows startup entry with native StartupApproved reversible toggle state.
/// </summary>
public partial class StartupEntryItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
    public StartupSourceKind SourceKind { get; init; }
    public string SourceDisplay { get; init; } = "";

    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isDuplicate;
    [ObservableProperty] private bool _isHeavyBackground;
    [ObservableProperty] private bool _isBrowserAutoLaunch;
    [ObservableProperty] private string _warningBadge = "";
    [ObservableProperty] private string _impactLevel = "一般";

    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningBadge);
    public string StatusText => IsEnabled ? "已啟用 (開機啟動)" : "已停用 (可隨時還原)";

    partial void OnIsEnabledChanged(bool value) => OnPropertyChanged(nameof(StatusText));
}

/// <summary>
/// Grouped memory consumption summary for a process family (e.g., msedgewebview2, chrome, TiWorker).
/// </summary>
public partial class ProcessGroupMemoryItem : ObservableObject
{
    public string ProcessName { get; init; } = "";
    public int InstanceCount { get; init; }
    public double WorkingSetMb { get; init; }
    public double PrivateMemoryMb { get; init; }
    public int HandleCount { get; init; }
    public string CategoryBadge { get; init; } = "App";
    public bool IsSystemProtected { get; init; }
    public bool IsHighUsage { get; init; }
    [ObservableProperty] private bool _isEcoQosApplied;

    public string DisplayHeader => InstanceCount > 1
        ? $"{ProcessName} ({InstanceCount} 行程)"
        : ProcessName;

    public string DisplayMemory => PrivateMemoryMb >= 1024
        ? $"{PrivateMemoryMb / 1024.0:F2} GB 私有 (WS {WorkingSetMb:F0} MB)"
        : $"{PrivateMemoryMb:F0} MB 私有 (WS {WorkingSetMb:F0} MB)";

    public string EcoButtonLabel => IsEcoQosApplied ? "⚡ 恢復標準" : "🍃 節能節流";

    partial void OnIsEcoQosAppliedChanged(bool value) => OnPropertyChanged(nameof(EcoButtonLabel));
}

/// <summary>
/// Maps spawned msedgewebview2.exe child processes back to the parent application that launched them.
/// </summary>
public class WebView2OwnerItem
{
    public string OwnerProcessName { get; init; } = "";
    public int WebView2Count { get; init; }
    public double TotalPrivateMb { get; init; }

    public string DisplaySummary => $"{OwnerProcessName} → {WebView2Count} 個 WebView2 ({TotalPrivateMb:F0} MB)";
}

public enum RamPressureSeverity
{
    Healthy,
    Elevated,
    CriticalThrashing
}

/// <summary>
/// Comprehensive live diagnostic snapshot explaining why a Windows PC is lagging or exhausting RAM.
/// </summary>
public class RamPressureReport
{
    public DateTime CapturedAt { get; init; } = DateTime.Now;
    public long PhysicalUsedMb { get; init; }
    public long PhysicalTotalMb { get; init; }
    public double PhysicalUsagePercent { get; init; }

    public long CommitUsedMb { get; init; }
    public long CommitLimitMb { get; init; }
    public double CommitUsagePercent { get; init; }

    public long PagefileAllocatedMb { get; init; }
    public long PagefileCurrentMb { get; init; }
    public long PagefilePeakMb { get; init; }

    public long PagedPoolMb { get; init; }
    public long NonPagedPoolMb { get; init; }
    public double PagesPerSec { get; init; }

    public RamPressureSeverity Severity { get; init; }
    public string SeverityBadge { get; init; } = "🟢 記憶體充裕";
    public string SummaryText { get; init; } = "";

    public List<ProcessGroupMemoryItem> TopProcessGroups { get; init; } = new();
    public List<WebView2OwnerItem> WebView2Owners { get; init; } = new();
    public List<string> DiagnosticFindings { get; init; } = new();

    public string PhysicalDisplay => $"{PhysicalUsedMb / 1024.0:F2} / {PhysicalTotalMb / 1024.0:F2} GB ({PhysicalUsagePercent:F1}%)";
    public string CommitDisplay => CommitLimitMb > 0
        ? $"{CommitUsedMb / 1024.0:F2} / {CommitLimitMb / 1024.0:F2} GB ({CommitUsagePercent:F0}%)"
        : "讀取中";
    public string PagefileDisplay => PagefileAllocatedMb > 0
        ? $"使用中 {PagefileCurrentMb / 1024.0:F2} GB / 峰值 {PagefilePeakMb / 1024.0:F2} GB (配置 {PagefileAllocatedMb / 1024.0:F1} GB)"
        : "系統託管 / 未使用";
    public string KernelPoolDisplay => $"Paged {PagedPoolMb:N0} MB · Non-Paged {NonPagedPoolMb:N0} MB · 分頁率 {PagesPerSec:F0} /s";
}

/// <summary>
/// A reversible background service or browser/shell bloat reduction toggle.
/// </summary>
public partial class BackgroundBloatTweakItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string TargetBadge { get; init; } = "";
    public string SafeNote { get; init; } = "";
    public bool RequiresAdmin { get; init; } = true;

    [ObservableProperty] private bool _isOptimized;
    [ObservableProperty] private string _currentStateDisplay = "偵測中...";
}

public enum CleanRunPresetKind
{
    SafeDaily,
    Balanced16Gb,
    RestoreWindowsDefaults
}

/// <summary>
/// Result of a safe memory release run.
/// </summary>
public class MemoryReleaseResult
{
    public bool Success { get; init; }
    public int TrimmedProcessCount { get; init; }
    public int SkippedProtectedCount { get; init; }
    public string ForegroundExcludedApp { get; init; } = "";
    public bool StandbyListPurged { get; init; }
    public long AvailableBeforeMb { get; init; }
    public long AvailableAfterMb { get; init; }
    public long ReclaimedMb => Math.Max(0, AvailableAfterMb - AvailableBeforeMb);
    public string Message { get; init; } = "";
}
