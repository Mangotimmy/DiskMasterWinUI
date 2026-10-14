using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Honest recoverability rating. The service never reports High unless the bytes
/// are still reachable without guessing (resident MFT data or an untouched file copy).
/// </summary>
public enum RecoveryConfidence
{
    /// <summary>Data is still physically present and was located exactly (resident MFT data, Recycle Bin copy).</summary>
    High = 0,

    /// <summary>Data location was reconstructed and the clusters are not in use, but they can still be overwritten at any moment.</summary>
    Medium = 1,

    /// <summary>Only a guess (signature carve) or a partially overwritten / compressed / encrypted layout.</summary>
    Low = 2,

    /// <summary>Metadata only — the file content is gone and cannot be reconstructed.</summary>
    Unrecoverable = 3
}

/// <summary>How the bytes of a candidate would be read back during recovery.</summary>
public enum RecoverySourceKind
{
    /// <summary>Non-resident $DATA run list read from the raw volume / image.</summary>
    MftClusters = 0,

    /// <summary>Small file whose content still sits inside the (deleted) MFT record.</summary>
    MftResident = 1,

    /// <summary>Ordinary file-system copy (Recycle Bin $R payload, temp residue).</summary>
    DirectFileCopy = 2,

    /// <summary>Content rebuilt from an on-disk signature (no name / path metadata).</summary>
    CarvedSignature = 3,

    /// <summary>Deleted directory entry: only the folder itself can be recreated.</summary>
    DirectoryEntry = 4
}

/// <summary>A single NTFS data run (mapping pair). <see cref="Lcn"/> = -1 means a sparse run.</summary>
public sealed class RecoveryDataRun
{
    public long Lcn { get; set; } = -1;
    public long ClusterCount { get; set; }
    public bool IsSparse => Lcn < 0;
}

/// <summary>
/// One recovery candidate produced by <c>RecoveryService.ScanAsync</c>.
/// User-facing fields are read-only projections; the reconstruction fields are used by the service
/// when the item is written to the destination.
/// </summary>
public partial class RecoveredFileItem : ObservableObject
{
    // ── user-facing identity ──────────────────────────────────────────────
    public string Name { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long SizeBytes { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public RecoveryConfidence Confidence { get; set; } = RecoveryConfidence.Low;
    public string ConfidenceDetail { get; set; } = "";
    public RecoverySourceKind SourceKind { get; set; } = RecoverySourceKind.MftClusters;
    public string VolumeRoot { get; set; } = "";
    public string MethodText { get; set; } = "";
    public uint MftRecordNumber { get; set; }

    // ── reconstruction data (used by RecoveryService) ─────────────────────
    public string SourcePath { get; set; } = "";
    public bool SourceIsImage { get; set; }
    public int SourceClusterSize { get; set; } = 4096;
    public List<RecoveryDataRun> DataRuns { get; set; } = new();
    public byte[]? InlineData { get; set; }
    public long RawOffset { get; set; }
    public long RawLength { get; set; }

    // ── live UI state ────────────────────────────────────────────────────
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _statusText = "待復原";
    [ObservableProperty] private string _recoveredTo = "";

    // ── projections ──────────────────────────────────────────────────────
    public string SizeDisplay => IsDirectory
        ? "<目錄>"
        : SizeBytes >= 1024L * 1024 * 1024 ? $"{SizeBytes / 1024.0 / 1024 / 1024:F2} GB"
        : SizeBytes >= 1024L * 1024 ? $"{SizeBytes / 1024.0 / 1024:F1} MB"
        : SizeBytes >= 1024 ? $"{SizeBytes / 1024.0:F1} KB"
        : $"{SizeBytes} B";

    public string ConfidenceText => Confidence switch
    {
        RecoveryConfidence.High => "🟢 高",
        RecoveryConfidence.Medium => "🟡 中",
        RecoveryConfidence.Low => "🟠 低",
        _ => "🔴 無法復原"
    };

    public string ConfidencePercent => Confidence switch
    {
        RecoveryConfidence.High => "約 90%",
        RecoveryConfidence.Medium => "約 60%",
        RecoveryConfidence.Low => "約 25%",
        _ => "0%"
    };

    public string TypeText => IsDirectory ? "📁 目錄" : "📄 檔案";

    public string TimeDisplay
    {
        get
        {
            var parts = new List<string>(3);
            if (ModifiedAt.HasValue) parts.Add($"修改 {ModifiedAt.Value:yyyy-MM-dd HH:mm}");
            if (CreatedAt.HasValue) parts.Add($"建立 {CreatedAt.Value:yyyy-MM-dd HH:mm}");
            if (DeletedAt.HasValue) parts.Add($"刪除 {DeletedAt.Value:yyyy-MM-dd HH:mm}");
            return parts.Count == 0 ? "—" : string.Join("  ·  ", parts);
        }
    }

    public string PathDisplay => string.IsNullOrWhiteSpace(OriginalPath) ? "(原始路徑未知)" : OriginalPath;

    public bool IsRecoverable =>
        Confidence != RecoveryConfidence.Unrecoverable &&
        (SourceKind switch
        {
            RecoverySourceKind.MftResident => InlineData is { Length: > 0 },
            RecoverySourceKind.MftClusters => DataRuns.Count > 0,
            RecoverySourceKind.DirectFileCopy => !string.IsNullOrEmpty(SourcePath),
            RecoverySourceKind.CarvedSignature => RawLength > 0,
            RecoverySourceKind.DirectoryEntry => true,
            _ => false
        });

    public string RecoverableText => IsRecoverable ? "可復原" : "僅中繼資料";

    /// <summary>Searchable blob for the UI filter box.</summary>
    public string SearchText => $"{Name}\n{OriginalPath}\n{MethodText}\n{ConfidenceDetail}".ToLowerInvariant();
}
