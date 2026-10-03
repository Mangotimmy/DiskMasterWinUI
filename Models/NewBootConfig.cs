namespace DiskMasterWinUI.Models;

public class NewBootConfig
{
    public string SourceWindowsPath { get; set; } = @"C:\Windows";
    public int TargetDiskNumber { get; set; } = 0;
    public bool CreateNewPartition { get; set; } = true;
    public int PartitionSizeMB { get; set; } = 260;
    public string TargetPartitionLetter { get; set; } = "S:";
    public string TargetFileSystem { get; set; } = "FAT32";
    public string FirmwareType { get; set; } = "UEFI"; // UEFI, BIOS, ALL
    public bool ForceMbrActive { get; set; } = false;
    public bool RemoveLetterAfterBuild { get; set; } = false;
}
