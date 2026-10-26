namespace DiskMasterWinUI.Models;

/// <summary>
/// Status of NTFS and ReFS TRIM (DisableDeleteNotify) on the system.
/// </summary>
public record TrimStatusInfo(
    bool IsNtfsTrimEnabled,
    bool IsReFsTrimEnabled,
    string StatusDisplay,
    string RawOutput
);

/// <summary>
/// Status of NVMe APST (Autonomous Power State Transition) anti-stutter tweak.
/// When disabled (EnableAPST = 0), NVMe SSD will not enter autonomous low-power sleep, eliminating wake-up lag.
/// </summary>
public record NvmeApstStatus(
    bool IsApstDisabled,
    int? RawValue,
    string StatusDisplay
);

/// <summary>
/// Status of Microsoft StorNVMe NumberOfRequests (queue depth concurrency limit).
/// </summary>
public record NvmeQueueStatus(
    int? NumberOfRequests,
    string StatusDisplay
);

/// <summary>
/// Combined telemetry of NVMe anti-stutter and storage optimizations.
/// </summary>
public record NvmeTweakSummary(
    TrimStatusInfo Trim,
    NvmeApstStatus Apst,
    NvmeQueueStatus Queue,
    bool RequiresReboot
);
