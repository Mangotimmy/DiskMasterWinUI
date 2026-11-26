using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Granular user-configurable options for the Windows Update &amp; CBS (0x800F0831) repair pipeline.
/// </summary>
public partial class WindowsUpdateRepairOptions : ObservableObject
{
    [ObservableProperty] private bool _runDismScanHealth = true;
    [ObservableProperty] private bool _runDismCheckHealth = true;
    [ObservableProperty] private bool _runDismRestoreHealth = true;
    [ObservableProperty] private bool _useLocalRepairSource;
    [ObservableProperty] private string _localRepairSourcePath = "";
    [ObservableProperty] private bool _runSfcScanNow = true;

    [ObservableProperty] private bool _resetWuServicesAndBits = true;
    [ObservableProperty] private bool _reRegisterCoreDlls;
    [ObservableProperty] private bool _resetSoftwareDistribution;
    [ObservableProperty] private bool _backupSoftwareDistribution = true;
    [ObservableProperty] private bool _resetCatroot2WithCryptSvc;
    [ObservableProperty] private bool _resetServiceSecurityDescriptors;
    [ObservableProperty] private bool _resetNetworkStack;
    [ObservableProperty] private bool _fixTrustedInstallerStartupType = true;
    [ObservableProperty] private bool _triggerUpdateScanAfter = true;
    [ObservableProperty] private bool _promptRebootAfterRepair = true;
}

/// <summary>
/// Predefined Windows Update error code presets for instant 1-click configuration.
/// </summary>
public class WindowsUpdateErrorPreset
{
    public string Id { get; init; } = "";
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string RecommendedAction { get; init; } = "";
    public string DocsUrl { get; init; } = "";

    public override string ToString() => $"{Code} — {Title}";
}

/// <summary>
/// A parsed diagnostic finding extracted from C:\Windows\Logs\CBS\CBS.log.
/// </summary>
public class CbsDiagnosticFinding
{
    public string Timestamp { get; init; } = "";
    public string ErrorCode { get; init; } = "";
    public string KbNumber { get; init; } = "";
    public string PackageId { get; init; } = "";
    public string Category { get; init; } = "";
    public string RawSummary { get; init; } = "";
    public string SearchQuery { get; init; } = "";
    public string CatalogSearchUrl { get; init; } = "";

    public string DisplayTitle => !string.IsNullOrWhiteSpace(KbNumber)
        ? $"{KbNumber} ({Category})"
        : !string.IsNullOrWhiteSpace(PackageId)
            ? $"{PackageId} ({Category})"
            : $"{ErrorCode} ({Category})";

    public string DisplaySubtitle => string.IsNullOrWhiteSpace(Timestamp)
        ? RawSummary
        : $"[{Timestamp}] {RawSummary}";

    public bool HasCatalogQuery => !string.IsNullOrWhiteSpace(SearchQuery);
}

/// <summary>
/// A Windows Update history record returned from Microsoft.Update.Session.
/// </summary>
public class WindowsUpdateHistoryItem
{
    public string DateDisplay { get; init; } = "";
    public string Title { get; init; } = "";
    public string KbNumber { get; init; } = "";
    public string ResultText { get; init; } = "";
    public bool IsFailed { get; init; }
    public string HResultHex { get; init; } = "";
    public string SearchQuery { get; init; } = "";
    public string CatalogSearchUrl { get; init; } = "";

    public string StatusBadge => IsFailed ? $"❌ 失敗 ({HResultHex})" : $"⚠️ {ResultText} ({HResultHex})";
    public bool HasCatalogQuery => !string.IsNullOrWhiteSpace(SearchQuery);
}

/// <summary>
/// Result of running the DISM / SFC component store repair sequence.
/// </summary>
public class DismSfcRepairResult
{
    public bool Success { get; init; }
    public int LastExitCode { get; init; }
    public bool RebootRecommended { get; init; }
    public string? DetectedErrorCode { get; init; }
    public string Summary { get; init; } = "";
}
