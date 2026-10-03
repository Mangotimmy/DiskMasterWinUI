namespace DiskMasterWinUI.Models;

/// <summary>
/// Encapsulates a network interface adapter with hardware description, IP, gateway, and primary status.
/// </summary>
public class NetworkAdapterItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string IPv4Address { get; set; } = "";
    public string GatewayAddress { get; set; } = "";
    public bool IsPrimary { get; set; }

    public string DisplayName =>
        $"{Name} ({(Description.Length > 32 ? Description.Substring(0, 30) + "..." : Description)})" +
        (string.IsNullOrWhiteSpace(IPv4Address) ? "" : $" [{IPv4Address}]") +
        (IsPrimary ? " ★ 主要連網" : "");
}
