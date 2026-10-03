namespace DiskMasterWinUI.Models;

public class SystemRestorePointItem
{
    public int SequenceNumber { get; set; }
    public string Description { get; set; } = "";
    public string CreationTime { get; set; } = "";
    public string RestorePointType { get; set; } = "";

    public string Summary => $"#{SequenceNumber} [{CreationTime}] — {Description} ({RestorePointType})";
}
