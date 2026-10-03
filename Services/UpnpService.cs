using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service providing UPnP NAT-T / IGD router port forwarding using Windows native COM (HNetCfg.NATUPnP).
/// Supports router capability querying, listing, adding, and removing port mapping rules.
/// </summary>
public class UpnpService
{
    /// <summary>
    /// Gets the primary local IPv4 address connected to the default gateway.
    /// </summary>
    public string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch
        {
            // Fallback: search operational network interfaces
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    var props = ni.GetIPProperties();
                    foreach (var ip in props.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(ip.Address))
                        {
                            return ip.Address.ToString();
                        }
                    }
                }
            }
            catch { }
        }

        return "127.0.0.1";
    }

    /// <summary>
    /// Tests whether the current local gateway/router supports and enables UPnP IGD.
    /// </summary>
    public async Task<bool> IsUpnpSupportedAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var natType = Type.GetTypeFromProgID("HNetCfg.NATUPnP");
                if (natType == null) return false;

                dynamic? nat = Activator.CreateInstance(natType);
                if (nat == null) return false;

                dynamic? mappings = nat.StaticPortMappingCollection;
                return mappings != null;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Retrieves all active UPnP port mappings from the router.
    /// </summary>
    public async Task<(bool Success, List<UpnpPortMappingItem> Mappings, string ErrorMessage)> GetPortMappingsAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<UpnpPortMappingItem>();
            try
            {
                var natType = Type.GetTypeFromProgID("HNetCfg.NATUPnP");
                if (natType == null)
                {
                    return (false, list, "HNetCfg.NATUPnP COM component not available on this Windows edition.");
                }

                dynamic? nat = Activator.CreateInstance(natType);
                if (nat == null)
                {
                    return (false, list, "Unable to instantiate NATUPnP COM object.");
                }

                dynamic? mappings = nat.StaticPortMappingCollection;
                if (mappings == null)
                {
                    return (false, list, "Router/Gateway did not respond to UPnP IGD queries (UPnP may be disabled on router).");
                }

                foreach (dynamic item in mappings)
                {
                    try
                    {
                        list.Add(new UpnpPortMappingItem
                        {
                            ExternalPort = (int)item.ExternalPort,
                            InternalPort = (int)item.InternalPort,
                            Protocol = (string)(item.Protocol ?? "TCP"),
                            InternalClient = (string)(item.InternalClient ?? ""),
                            Description = (string)(item.Description ?? ""),
                            Enabled = (bool)(item.Enabled ?? true)
                        });
                    }
                    catch { }
                }

                return (true, list, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, list, ex.Message);
            }
        });
    }

    /// <summary>
    /// Adds or updates a UPnP port forwarding rule on the router.
    /// </summary>
    public async Task<(bool Success, string Message)> AddPortMappingAsync(UpnpPortMappingItem item)
    {
        return await Task.Run(() =>
        {
            try
            {
                var natType = Type.GetTypeFromProgID("HNetCfg.NATUPnP");
                if (natType == null)
                {
                    return (false, "HNetCfg.NATUPnP COM component not available.");
                }

                dynamic? nat = Activator.CreateInstance(natType);
                if (nat == null)
                {
                    return (false, "Failed to instantiate NATUPnP COM object.");
                }

                dynamic? mappings = nat.StaticPortMappingCollection;
                if (mappings == null)
                {
                    return (false, "Router did not respond. Check if UPnP is enabled in router admin settings.");
                }

                string clientIp = string.IsNullOrWhiteSpace(item.InternalClient)
                    ? GetLocalIpAddress()
                    : item.InternalClient;

                string protocol = string.IsNullOrWhiteSpace(item.Protocol) ? "TCP" : item.Protocol.ToUpperInvariant();
                string desc = string.IsNullOrWhiteSpace(item.Description)
                    ? $"DiskMaster_{item.ExternalPort}"
                    : item.Description;

                mappings.Add(
                    item.ExternalPort,
                    protocol,
                    item.InternalPort,
                    clientIp,
                    item.Enabled,
                    desc
                );

                return (true, $"Port {item.ExternalPort} ({protocol}) mapped successfully to {clientIp}:{item.InternalPort}");
            }
            catch (COMException cex)
            {
                return (false, $"Router rejected UPnP port mapping: {cex.Message} (Error code: 0x{cex.ErrorCode:X8})");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to add UPnP port mapping: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Deletes an existing UPnP port forwarding rule.
    /// </summary>
    public async Task<(bool Success, string Message)> DeletePortMappingAsync(int externalPort, string protocol)
    {
        return await Task.Run(() =>
        {
            try
            {
                var natType = Type.GetTypeFromProgID("HNetCfg.NATUPnP");
                if (natType == null)
                {
                    return (false, "HNetCfg.NATUPnP COM component not available.");
                }

                dynamic? nat = Activator.CreateInstance(natType);
                if (nat == null)
                {
                    return (false, "Failed to instantiate NATUPnP COM object.");
                }

                dynamic? mappings = nat.StaticPortMappingCollection;
                if (mappings == null)
                {
                    return (false, "Router UPnP collection not available.");
                }

                mappings.Remove(externalPort, protocol.ToUpperInvariant());
                return (true, $"Port {externalPort} ({protocol}) removed successfully.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to delete UPnP port mapping: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Returns predefined port forwarding templates for popular multiplayer games and server utilities.
    /// </summary>
    public static List<UpnpPortMappingItem> GetGamePresets()
    {
        return new List<UpnpPortMappingItem>
        {
            new() { ExternalPort = 25565, InternalPort = 25565, Protocol = "TCP", Description = "Minecraft Java Server" },
            new() { ExternalPort = 19132, InternalPort = 19132, Protocol = "UDP", Description = "Minecraft Bedrock Server" },
            new() { ExternalPort = 27015, InternalPort = 27015, Protocol = "UDP", Description = "Steam Server / CS2 / TF2" },
            new() { ExternalPort = 7777,  InternalPort = 7777,  Protocol = "TCP", Description = "Terraria Multiplayer Server" },
            new() { ExternalPort = 2456,  InternalPort = 2456,  Protocol = "UDP", Description = "Valheim Dedicated Server" },
            new() { ExternalPort = 8211,  InternalPort = 8211,  Protocol = "UDP", Description = "Palworld Dedicated Server" },
            new() { ExternalPort = 3389,  InternalPort = 3389,  Protocol = "TCP", Description = "Windows Remote Desktop (RDP)" },
            new() { ExternalPort = 32400, InternalPort = 32400, Protocol = "TCP", Description = "Plex Media Server" },
            new() { ExternalPort = 8080,  InternalPort = 8080,  Protocol = "TCP", Description = "Custom Web Server / HTTP" },
            new() { ExternalPort = 22,    InternalPort = 22,    Protocol = "TCP", Description = "SSH Secure Shell" }
        };
    }
}
