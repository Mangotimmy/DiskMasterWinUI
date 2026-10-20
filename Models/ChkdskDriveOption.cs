namespace DiskMasterWinUI.Models;

/// <summary>
/// A selectable drive for the chkdsk card dropdown: the raw letter used on the command
/// line plus a friendly label (volume name and free space) shown in the ComboBox.
/// </summary>
public sealed class ChkdskDriveOption
{
    public string Letter { get; init; } = "C:";
    public string Display { get; init; } = "C:";

    public static ChkdskDriveOption FromDrive(DriveInfo drive)
    {
        string letter = drive.Name.TrimEnd('\\');
        double freeGb = 0;
        string label = "";
        try
        {
            freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "" : drive.VolumeLabel.Trim();
        }
        catch { }

        string display = string.IsNullOrEmpty(label)
            ? (freeGb > 0 ? $"{letter}  (可用 {freeGb:N1} GB)" : letter)
            : (freeGb > 0 ? $"{letter}  {label} — 可用 {freeGb:N1} GB" : $"{letter}  {label}");

        return new ChkdskDriveOption { Letter = letter, Display = display };
    }
}
