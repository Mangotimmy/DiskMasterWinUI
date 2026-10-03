namespace DiskMasterWinUI.Models;

/// <summary>
/// Represents an ATA / SATA S.M.A.R.T. attribute entry.
/// </summary>
public class SmartAttributeItem
{
    public byte Id { get; set; }
    public string IdHex => $"0x{Id:X2}";
    public string Name { get; set; } = "";
    public int Current { get; set; }
    public int Worst { get; set; }
    public int Threshold { get; set; }
    public ulong RawValue { get; set; }

    /// <summary>
    /// Formatted display of raw value, togglable between human-readable decimal and hex.
    /// </summary>
    public string RawValueDisplay { get; set; } = "";
    public string RawValueHex => $"0x{RawValue:X12}";

    public string Status { get; set; } = "OK";
    public string StatusIcon => Status switch
    {
        "OK" or "良好" => "✅",
        "Warning" or "警告" => "⚠️",
        "Critical" or "危險" => "❌",
        _ => "ℹ️"
    };

    public bool IsCritical { get; set; }
}
