namespace DiskMasterWinUI.Models;

public class RegistryBackupItem
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public System.DateTime CreatedAt { get; set; }
    public long FileSizeBytes { get; set; }
    public string Description { get; set; } = "";

    public string CreatedAtDisplay => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string SizeDisplay => $"{FileSizeBytes / 1024.0:F1} KB";
    public string Summary => $"{FileName} ({SizeDisplay}) — {CreatedAtDisplay}";
}
