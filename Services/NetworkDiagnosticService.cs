using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Result of an individual network diagnostic stage.
/// </summary>
public class DiagnosticStepResult
{
    public string Name { get; set; } = "";
    public bool Success { get; set; }
    public long LatencyMs { get; set; }
    public string LatencyDisplay => LatencyMs > 0 ? $"{LatencyMs} ms" : "--";
    public string Details { get; set; } = "";
    public string Recommendation { get; set; } = "";
}

/// <summary>
/// Predefined DNS configuration profile.
/// </summary>
public class DnsPreset
{
    public string Name { get; set; } = "";
    public string PrimaryDns { get; set; } = "";
    public string SecondaryDns { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// Service providing 5-stage automated network bottleneck diagnosis and DNS configuration.
/// </summary>
public class NetworkDiagnosticService
{
    /// <summary>
    /// Gets recommended DNS server profiles.
    /// </summary>
    public static List<DnsPreset> GetRecommendedDnsPresets()
    {
        return new List<DnsPreset>
        {
            new() { Name = "Cloudflare (1.1.1.1)", PrimaryDns = "1.1.1.1", SecondaryDns = "1.0.0.1", Description = "Fastest global response, privacy-focused, no logs" },
            new() { Name = "Google Public DNS", PrimaryDns = "8.8.8.8", SecondaryDns = "8.8.4.4", Description = "High stability, global Anycast, excellent CDN resolution" },
            new() { Name = "Quad9 Security", PrimaryDns = "9.9.9.9", SecondaryDns = "149.112.112.112", Description = "Built-in threat intelligence, malware & phishing blocking" },
            new() { Name = "AdGuard DNS", PrimaryDns = "94.140.14.14", SecondaryDns = "94.140.15.15", Description = "Blocks ads, trackers, and malicious domains automatically" },
            new() { Name = "HiNet (Taiwan Chunghwa)", PrimaryDns = "168.95.1.1", SecondaryDns = "168.95.192.1", Description = "Lowest latency for Taiwan and East Asia ISPs" },
            new() { Name = "TWNIC Quad 101", PrimaryDns = "101.101.101.101", SecondaryDns = "101.102.103.104", Description = "Taiwan Network Information Center public DNS" },
            new() { Name = "AliDNS (Alibaba)", PrimaryDns = "223.5.5.5", SecondaryDns = "223.6.6.6", Description = "Optimal routing and speed for Greater China regions" },
            new() { Name = "Automatic (DHCP)", PrimaryDns = "", SecondaryDns = "", Description = "Restore router ISP default automatic assignment" }
        };
    }

    /// <summary>
    /// Runs a 5-stage smart network diagnostic to find reasons for slow network or latency spikes.
    /// </summary>
    public async Task<List<DiagnosticStepResult>> RunSmartDiagnosticsAsync(Action<string>? progressCallback = null)
    {
        return await Task.Run(async () =>
        {
            var results = new List<DiagnosticStepResult>();

            // Stage 1: Local Gateway Ping (Wi-Fi / Ethernet quality)
            progressCallback?.Invoke("Diagnosing Local Gateway connection...");
            results.Add(await CheckLocalGatewayAsync());

            // Stage 2: DNS Resolution Speed
            progressCallback?.Invoke("Measuring DNS lookup latency...");
            results.Add(await CheckDnsResolutionAsync());

            // Stage 3: Internet Backbone Connectivity & Latency
            progressCallback?.Invoke("Pinging Internet backbone nodes (Cloudflare/Google)...");
            results.Add(await CheckBackbonePingAsync());

            // Stage 4: Critical Web Ports (HTTP 80 / HTTPS 443)
            progressCallback?.Invoke("Verifying outbound ports (80/443/53)...");
            results.Add(await CheckOutboundPortsAsync());

            // Stage 5: Network Adapter Packet Loss & Errors
            progressCallback?.Invoke("Checking network adapter statistics...");
            results.Add(CheckAdapterStatus());

            return results;
        });
    }

    private async Task<DiagnosticStepResult> CheckLocalGatewayAsync()
    {
        var step = new DiagnosticStepResult { Name = "1. Local Gateway (Router / Wi-Fi)" };
        try
        {
            string? gatewayIp = null;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var props = ni.GetIPProperties();
                var gw = props.GatewayAddresses.FirstOrDefault();
                if (gw != null && gw.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    gatewayIp = gw.Address.ToString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(gatewayIp))
            {
                step.Success = false;
                step.Details = "No default gateway found. Network might be disconnected.";
                step.Recommendation = "Check Wi-Fi connection or reconnect your network cable.";
                return step;
            }

            using var ping = new Ping();
            var reply = await ping.SendPingAsync(gatewayIp, 2000);
            if (reply.Status == IPStatus.Success)
            {
                step.Success = true;
                step.LatencyMs = reply.RoundtripTime;
                step.Details = $"Gateway ({gatewayIp}): {reply.RoundtripTime} ms";
                if (reply.RoundtripTime > 50)
                {
                    step.Recommendation = "Local latency is high (>50ms). Wi-Fi interference or bad Ethernet cable detected.";
                }
                else
                {
                    step.Recommendation = "Local router connection is healthy and fast.";
                }
            }
            else
            {
                step.Success = false;
                step.Details = $"Gateway ({gatewayIp}) ping failed: {reply.Status}";
                step.Recommendation = "Router is not responding. Reboot your router/modem.";
            }
        }
        catch (Exception ex)
        {
            step.Success = false;
            step.Details = ex.Message;
            step.Recommendation = "Verify local adapter settings.";
        }

        return step;
    }

    private async Task<DiagnosticStepResult> CheckDnsResolutionAsync()
    {
        var step = new DiagnosticStepResult { Name = "2. DNS Resolution Speed" };
        var sw = Stopwatch.StartNew();
        try
        {
            var entry = await Dns.GetHostEntryAsync("www.cloudflare.com");
            sw.Stop();
            step.LatencyMs = sw.ElapsedMilliseconds;
            step.Success = entry.AddressList.Length > 0;
            step.Details = $"Resolved cloudflare.com in {sw.ElapsedMilliseconds} ms ({entry.AddressList.Length} IPs)";

            if (sw.ElapsedMilliseconds > 200)
            {
                step.Recommendation = "DNS resolution is sluggish (>200ms). Switching to 1.1.1.1 or 8.8.8.8 recommended.";
            }
            else
            {
                step.Recommendation = "DNS response time is excellent.";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            step.Success = false;
            step.LatencyMs = sw.ElapsedMilliseconds;
            step.Details = $"DNS resolution failed: {ex.Message}";
            step.Recommendation = "Current DNS server is not responding or unreachable. Switch to a public DNS.";
        }

        return step;
    }

    private async Task<DiagnosticStepResult> CheckBackbonePingAsync()
    {
        var step = new DiagnosticStepResult { Name = "3. Internet Backbone (1.1.1.1 / 8.8.8.8)" };
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("1.1.1.1", 3000);
            if (reply.Status == IPStatus.Success)
            {
                step.Success = true;
                step.LatencyMs = reply.RoundtripTime;
                step.Details = $"Cloudflare Backbone Ping: {reply.RoundtripTime} ms";
                if (reply.RoundtripTime > 150)
                {
                    step.Recommendation = "High external latency (>150ms). ISP routing congestion or background downloads.";
                }
                else
                {
                    step.Recommendation = "Internet backbone connectivity is stable.";
                }
            }
            else
            {
                // Fallback to 8.8.8.8
                var reply2 = await ping.SendPingAsync("8.8.8.8", 3000);
                if (reply2.Status == IPStatus.Success)
                {
                    step.Success = true;
                    step.LatencyMs = reply2.RoundtripTime;
                    step.Details = $"Google Backbone Ping: {reply2.RoundtripTime} ms";
                    step.Recommendation = "Internet backbone reachable via Google DNS.";
                }
                else
                {
                    step.Success = false;
                    step.Details = "Internet backbone unreachable (ICMP blocked or disconnected).";
                    step.Recommendation = "Check WAN connection on modem or contact your ISP.";
                }
            }
        }
        catch (Exception ex)
        {
            step.Success = false;
            step.Details = ex.Message;
        }

        return step;
    }

    private async Task<DiagnosticStepResult> CheckOutboundPortsAsync()
    {
        var step = new DiagnosticStepResult { Name = "4. Outbound Ports (Web HTTPS: 443)" };
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync("1.1.1.1", 443);
            if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask)
            {
                await connectTask;
                sw.Stop();
                step.Success = true;
                step.LatencyMs = sw.ElapsedMilliseconds;
                step.Details = $"TCP 443 connected successfully in {sw.ElapsedMilliseconds} ms";
                step.Recommendation = "Outbound web browsing ports are open and operational.";
            }
            else
            {
                step.Success = false;
                step.Details = "TCP 443 connection timed out (3000 ms).";
                step.Recommendation = "Firewall, VPN, or ISP may be blocking outbound port 443.";
            }
        }
        catch (Exception ex)
        {
            step.Success = false;
            step.Details = $"Port 443 test failed: {ex.Message}";
            step.Recommendation = "Verify Windows Firewall rules.";
        }

        return step;
    }

    private static DiagnosticStepResult CheckAdapterStatus()
    {
        var step = new DiagnosticStepResult { Name = "5. Network Adapter Health" };
        try
        {
            var activeNic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                      ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            if (activeNic != null)
            {
                var ipStats = activeNic.GetIPStatistics();
                long errors = ipStats.IncomingPacketsWithErrors + ipStats.OutgoingPacketsWithErrors;
                long discarded = ipStats.IncomingPacketsDiscarded + ipStats.OutgoingPacketsDiscarded;

                step.Success = true;
                step.Details = $"{activeNic.Name} ({activeNic.Speed / 1_000_000} Mbps) | Errors: {errors}, Discarded: {discarded}";
                if (errors > 100 || discarded > 500)
                {
                    step.Recommendation = "High packet errors detected. Update network adapter drivers or replace cable.";
                }
                else
                {
                    step.Recommendation = "Network adapter operating normally without packet drops.";
                }
            }
            else
            {
                step.Success = false;
                step.Details = "No active network adapter found.";
                step.Recommendation = "Enable network adapter in Windows Device Manager.";
            }
        }
        catch (Exception ex)
        {
            step.Success = false;
            step.Details = ex.Message;
        }

        return step;
    }

    /// <summary>
    /// Applies DNS settings to the primary active network adapter using PowerShell or netsh.
    /// </summary>
    public async Task<(bool Success, string Message)> ApplyDnsAsync(string primaryDns, string secondaryDns)
    {
        return await Task.Run(() =>
        {
            try
            {
                var activeNic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                          ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                if (activeNic == null)
                {
                    return (false, "No active network adapter detected.");
                }

                string alias = activeNic.Name;

                if (string.IsNullOrWhiteSpace(primaryDns))
                {
                    // Reset to DHCP
                    var psi = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"interface ip set dns name=\"{alias}\" dhcp",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(5000);
                    HostsService.FlushDnsCache();
                    return (true, $"DNS reset to automatic DHCP for adapter '{alias}'.");
                }
                else
                {
                    // Set primary
                    var psi1 = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"interface ip set dns name=\"{alias}\" static {primaryDns} validate=no",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p1 = Process.Start(psi1);
                    p1?.WaitForExit(5000);

                    // Set secondary if provided
                    if (!string.IsNullOrWhiteSpace(secondaryDns))
                    {
                        var psi2 = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"interface ip add dns name=\"{alias}\" {secondaryDns} index=2 validate=no",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var p2 = Process.Start(psi2);
                        p2?.WaitForExit(5000);
                    }

                    HostsService.FlushDnsCache();
                    return (true, $"DNS successfully updated to {primaryDns}{(string.IsNullOrWhiteSpace(secondaryDns) ? "" : $", {secondaryDns}")} on '{alias}'.");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Failed to set DNS: {ex.Message}");
            }
        });
    }
}
