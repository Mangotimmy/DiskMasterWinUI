namespace DiskMasterWinUI.Models;

/// <summary>
/// Represents an entry in the Windows Hosts file (C:\Windows\System32\drivers\etc\hosts).
/// </summary>
public class HostsEntryItem
{
    /// <summary>
    /// Gets or sets the target IP address (e.g. 127.0.0.1, 0.0.0.0, or custom IP).
    /// </summary>
    public string IpAddress { get; set; } = "127.0.0.1";

    /// <summary>
    /// Gets or sets the domain name or host name.
    /// </summary>
    public string HostName { get; set; } = "";

    /// <summary>
    /// Gets or sets any optional comment or description.
    /// </summary>
    public string Comment { get; set; } = "";

    /// <summary>
    /// Gets or sets whether this hosts entry is actively enabled (not commented out).
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Represents a timestamped historical backup snapshot of the Hosts file.
/// </summary>
public class HostsBackupItem
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public long FileSizeBytes { get; set; }
    public string Note { get; set; } = "";
    public int EntryCount { get; set; }
    public string DisplaySize => $"{FileSizeBytes:N0} 位元組";
    public string DisplayTime => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>
/// Detailed diagnostic analysis report for the Windows Hosts file.
/// </summary>
public class HostsAnalysisReport
{
    public int TotalLines { get; set; }
    public int ActiveEntriesCount { get; set; }
    public int CommentedEntriesCount { get; set; }
    public int BlocklistEntriesCount { get; set; }
    public List<string> ManagedSections { get; set; } = new();
    public List<string> DuplicateDomains { get; set; } = new();
    public List<string> ConflictingEntries { get; set; } = new();
    public List<string> SyntaxErrors { get; set; } = new();
    public List<string> HighRiskEntries { get; set; } = new();

    public bool HasIssues => DuplicateDomains.Count > 0 || ConflictingEntries.Count > 0 || SyntaxErrors.Count > 0 || HighRiskEntries.Count > 0;
    public string HealthBadge => HasIssues ? "⚠️ 發現異常或重複" : "🟢 格式良好健康";
    public string SummaryText => $"總計 {TotalLines} 行 | 啟用條目: {ActiveEntriesCount} | 封鎖項: {BlocklistEntriesCount} | 託管區塊: {ManagedSections.Count} 個 | 重複項: {DuplicateDomains.Count} | 語法問題: {SyntaxErrors.Count}";
}

