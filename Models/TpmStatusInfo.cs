namespace DiskMasterWinUI.Models;

/// <summary>
/// Encapsulates TPM (Trusted Platform Module) hardware status, specification, and security flags.
/// </summary>
public class TpmStatusInfo
{
    public bool IsPresent { get; set; }
    public string SpecVersion { get; set; } = "Unknown";
    public string ManufacturerName { get; set; } = "Unknown";
    public string ManufacturerVersion { get; set; } = "Unknown";
    public bool IsEnabled { get; set; }
    public bool IsActivated { get; set; }
    public bool IsOwned { get; set; }
    public bool IsWin11BypassActive { get; set; }
    public string StatusSummary { get; set; } = "";
    public string BitLockerSummary { get; set; } = "";
    public string DetailsLog { get; set; } = "";
}
