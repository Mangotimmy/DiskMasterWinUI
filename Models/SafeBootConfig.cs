namespace DiskMasterWinUI.Models;

/// <summary>
/// Represents MSConfig-compatible boot configuration parameters and safe boot modes.
/// </summary>
public class SafeBootConfig
{
    /// <summary>
    /// Safe boot mode: "Normal", "Minimal", "AlternateShell", "Network", "DsRepair".
    /// </summary>
    public string SafeBootMode { get; set; } = "Normal";

    /// <summary>
    /// bcdedit /set {current} noguiboot yes / deletevalue
    /// </summary>
    public bool NoGuiBoot { get; set; }

    /// <summary>
    /// bcdedit /set {current} bootlog yes / deletevalue
    /// </summary>
    public bool BootLog { get; set; }

    /// <summary>
    /// bcdedit /set {current} basevideo yes / deletevalue
    /// </summary>
    public bool BaseVideo { get; set; }

    /// <summary>
    /// bcdedit /set {current} sos yes / deletevalue
    /// </summary>
    public bool Sos { get; set; }

    /// <summary>
    /// bcdedit /set {current} testsigning on / off
    /// </summary>
    public bool TestSigning { get; set; }

    /// <summary>
    /// bcdedit /set {current} nointegritychecks on / off
    /// </summary>
    public bool NoIntegrityChecks { get; set; }

    /// <summary>
    /// bcdedit /set {current} hypervisorlaunchtype auto / off
    /// </summary>
    public string HypervisorLaunchType { get; set; } = "auto";
}
