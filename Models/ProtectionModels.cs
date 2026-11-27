using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Single file record stored inside a Quarantine Vault session manifest.
/// </summary>
public class QuarantineFileEntry
{
    public string OriginalPath { get; set; } = "";
    public string QuarantinedRelativePath { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public string CategoryId { get; set; } = "";
}

/// <summary>
/// Persisted JSON manifest describing one cleanup batch in the Quarantine Vault.
/// </summary>
public class QuarantineSessionManifest
{
    public string SessionId { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string CategorySummary { get; set; } = "";
    public int TotalFiles { get; set; }
    public long TotalBytes { get; set; }
    public bool IsRestored { get; set; }
    public List<QuarantineFileEntry> Entries { get; set; } = new();
}

/// <summary>
/// UI-bindable summary item representing a restorable Quarantine Vault cleanup session.
/// </summary>
public partial class QuarantineSessionItem : ObservableObject
{
    public string SessionId { get; init; } = "";
    public DateTime CreatedLocal { get; init; }
    public string CategorySummary { get; init; } = "";
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public string SizeDisplay { get; init; } = "0 B";

    [ObservableProperty] private bool _isRestored;

    public string DisplayTitle => $"{CategorySummary} ({FileCount:N0} 檔 · {SizeDisplay})";
    public string DisplayTime => CreatedLocal.ToString("yyyy-MM-dd HH:mm:ss");
    public string StatusBadge => IsRestored ? "✅ 已還原" : "🛡️ 可隨時還原";

    partial void OnIsRestoredChanged(bool value) => OnPropertyChanged(nameof(StatusBadge));
}
