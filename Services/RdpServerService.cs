using System.Diagnostics;
using DiskMasterWinUI.Helpers;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

public class RdpStatusInfo
{
    public string EditionName { get; set; } = "";
    public string EditionId { get; set; } = "";
    public bool IsHomeEdition { get; set; }
    public bool IsRdpEnabled { get; set; }
    public bool IsServiceRunning { get; set; }
    public bool IsFirewallAllowed { get; set; }
    public string ConnectAddressIp { get; set; } = "";
    public string ConnectAddressHost { get; set; } = "";
}

public class RdpSessionItem
{
    public string SessionName { get; set; } = "";
    public string UserName { get; set; } = "";
    public int SessionId { get; set; }
    public string State { get; set; } = "";
    public string DeviceType { get; set; } = "";
}

/// <summary>
/// Remote Desktop (RDP Server) Service for Windows Home and Pro editions.
/// Detects edition, enables Terminal Server listeners, configures registry,
/// firewall port 3389 rules, and manages incoming sessions.
/// </summary>
public class RdpServerService
{
    public async Task<RdpStatusInfo> GetStatusAsync(CancellationToken ct = default)
    {
        var info = new RdpStatusInfo();

        // 1. Detect Edition
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            info.EditionId = key?.GetValue("EditionID")?.ToString() ?? "";
            info.EditionName = key?.GetValue("ProductName")?.ToString() ?? "";
            info.IsHomeEdition = info.EditionId.Contains("Home", StringComparison.OrdinalIgnoreCase) ||
                                 info.EditionName.Contains("Home", StringComparison.OrdinalIgnoreCase);
        }
        catch { }

        // 2. Check fDenyTSConnections
        try
        {
            using var tsKey = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Control\Terminal Server");
            var fDeny = tsKey?.GetValue("fDenyTSConnections");
            info.IsRdpEnabled = (fDeny is int val && val == 0);
        }
        catch { }

        // 3. Check TermService status
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
            var (outStr, _, _) = await ProcessHelper.RunProcessAsync("sc.exe", "query TermService", cancellationToken: timeoutCts.Token);
            info.IsServiceRunning = outStr.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch { }

        // 4. IP endpoints
        var (ip, host) = NetShareService.GetPrimaryLanEndpoints();
        info.ConnectAddressIp = !string.IsNullOrEmpty(ip) ? $"{ip}:3389" : "3389";
        info.ConnectAddressHost = $"{host}:3389";

        return info;
    }

    public RdpStatusInfo GetStatus()
    {
        var info = new RdpStatusInfo();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            info.EditionId = key?.GetValue("EditionID")?.ToString() ?? "";
            info.EditionName = key?.GetValue("ProductName")?.ToString() ?? "";
            info.IsHomeEdition = info.EditionId.Contains("Home", StringComparison.OrdinalIgnoreCase) ||
                                 info.EditionName.Contains("Home", StringComparison.OrdinalIgnoreCase);
        }
        catch { }
        try
        {
            using var tsKey = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Control\Terminal Server");
            var fDeny = tsKey?.GetValue("fDenyTSConnections");
            info.IsRdpEnabled = (fDeny is int val && val == 0);
        }
        catch { }
        try
        {
            var proc = Process.GetProcessesByName("svchost");
            info.IsServiceRunning = proc.Length > 0;
        }
        catch { }
        var (ip, host) = NetShareService.GetPrimaryLanEndpoints();
        info.ConnectAddressIp = !string.IsNullOrEmpty(ip) ? $"{ip}:3389" : "3389";
        info.ConnectAddressHost = $"{host}:3389";
        return info;
    }

    public async Task<(bool Success, string Message)> EnableRdpServerAsync(bool isHomeEdition)
    {
        var results = new List<string>();

        // 1. Registry: fDenyTSConnections = 0
        try
        {
            using var tsKey = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Control\Terminal Server", true);
            if (tsKey != null)
            {
                tsKey.SetValue("fDenyTSConnections", 0, RegistryValueKind.DWord);
                tsKey.SetValue("UserAuthentication", 0, RegistryValueKind.DWord); // Allow broader RDP clients
                results.Add("Registry fDenyTSConnections set to 0 (Allowed).");
            }

            if (isHomeEdition)
            {
                // Home Edition termsrv licensing overrides
                using var licKey = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Control\Terminal Server\Licensing Core", true);
                licKey?.SetValue("EnableConcurrentSessions", 1, RegistryValueKind.DWord);

                using var ntKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Terminal Server", true);
                ntKey?.SetValue("TSAppCompat", 1, RegistryValueKind.DWord);
                results.Add("Home Edition policy flags applied.");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Registry configuration error: {ex.Message}");
        }

        // 2. Open Firewall for port 3389 TCP & UDP
        try
        {
            await ProcessHelper.RunProcessAsync("netsh.exe", "advfirewall firewall add rule name=\"Remote Desktop (TCP 3389)\" dir=in action=allow protocol=TCP localport=3389");
            await ProcessHelper.RunProcessAsync("netsh.exe", "advfirewall firewall add rule name=\"Remote Desktop (UDP 3389)\" dir=in action=allow protocol=UDP localport=3389");
            results.Add("Firewall rules for port 3389 configured.");
        }
        catch { }

        // 3. Configure and start TermService
        try
        {
            await ProcessHelper.RunProcessAsync("sc.exe", "config TermService start= auto");
            await ProcessHelper.RunProcessAsync("net.exe", "start TermService");
            results.Add("TermService configured to automatic startup and started.");
        }
        catch { }

        return (true, string.Join("\r\n", results));
    }

    public async Task<(bool Success, string Message)> DisableRdpServerAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var tsKey = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Control\Terminal Server", true);
                tsKey?.SetValue("fDenyTSConnections", 1, RegistryValueKind.DWord);
                return (true, "Remote Desktop has been disabled (fDenyTSConnections = 1).");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to disable RDP: {ex.Message}");
            }
        });
    }

    public async Task<List<RdpSessionItem>> GetActiveSessionsAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var list = new List<RdpSessionItem>();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2.5));

            var (outStr, _, _) = await ProcessHelper.RunProcessAsync("qwinsta.exe", "", cancellationToken: cts.Token);
            if (string.IsNullOrWhiteSpace(outStr)) return list;

            var lines = outStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines.Skip(1)) // Skip header
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    var sessionName = parts[0];
                    var userName = "";
                    int sessionId = 0;
                    var state = "";

                    if (int.TryParse(parts[1], out int id1))
                    {
                        sessionId = id1;
                        state = parts[2];
                    }
                    else if (parts.Length >= 4 && int.TryParse(parts[2], out int id2))
                    {
                        userName = parts[1];
                        sessionId = id2;
                        state = parts[3];
                    }

                    list.Add(new RdpSessionItem
                    {
                        SessionName = sessionName,
                        UserName = userName,
                        SessionId = sessionId,
                        State = state
                    });
                }
            }

            DebugLogService.Instance.Debug($"RdpServer enumerated {list.Count} active sessions in {sw.ElapsedMilliseconds}ms", "RdpServerService");
            return list;
        }
        catch (OperationCanceledException)
        {
            DebugLogService.Instance.Warning($"RdpServer session query timed out after {sw.ElapsedMilliseconds}ms", "RdpServerService");
            return list;
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Warning($"RdpServer GetActiveSessionsAsync failed: {ex.Message}", "RdpServerService");
            return list;
        }
    }

    public void LaunchLoopbackTest()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = "/v:127.0.0.1",
                UseShellExecute = true
            });
        }
        catch { }
    }
}
