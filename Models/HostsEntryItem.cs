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
