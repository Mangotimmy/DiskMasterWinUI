namespace DiskMasterWinUI.Models;

/// <summary>
/// Represents a UPnP NAT port mapping entry on the local network router/gateway.
/// </summary>
public class UpnpPortMappingItem
{
    /// <summary>
    /// Gets or sets the external port on the WAN side.
    /// </summary>
    public int ExternalPort { get; set; }

    /// <summary>
    /// Gets or sets the target internal port on the LAN side.
    /// </summary>
    public int InternalPort { get; set; }

    /// <summary>
    /// Gets or sets the protocol: "TCP" or "UDP".
    /// </summary>
    public string Protocol { get; set; } = "TCP";

    /// <summary>
    /// Gets or sets the internal LAN client IP address (e.g., 192.168.1.100).
    /// </summary>
    public string InternalClient { get; set; } = "";

    /// <summary>
    /// Gets or sets the human-readable description or label for the port mapping.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Gets or sets whether this port mapping rule is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets a formatted display summary for the port mapping.
    /// </summary>
    public string DisplayTitle => $"{Description} ({Protocol}: {ExternalPort})";
}
