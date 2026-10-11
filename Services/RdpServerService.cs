using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
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
    public bool IsListening { get; set; }
    public int Port { get; set; } = 3389;
    public bool SingleSessionPerUser { get; set; } = true;
    public bool NetworkLevelAuthRequired { get; set; }
    public int ShadowMode { get; set; } = 1;
    public string TermsrvVersion { get; set; } = "";
    public bool IsWrapperInstalled { get; set; }
    public string WrapperVersion { get; set; } = "";
    public bool HasWrapperIni { get; set; }
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
/// Remote Desktop (RDP Server) & RDP Wrapper Management Service.
/// Features: Dynamic port detection & modification, concurrent multi-sessions (fSingleSessionPerUser),
/// Network Level Authentication (NLA), Session Shadowing, rdpwrap.ini community auto-updater,
/// service control, firewall rule synchronization, and active sessions manager.
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

        // 2. Check fDenyTSConnections & fSingleSessionPerUser
        try
        {
            using var tsKey = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Control\Terminal Server");
            if (tsKey != null)
            {
                var fDeny = tsKey.GetValue("fDenyTSConnections");
                info.IsRdpEnabled = (fDeny is int val && val == 0);

                var fSingle = tsKey.GetValue("fSingleSessionPerUser");
                info.SingleSessionPerUser = !(fSingle is int sVal && sVal == 0);
            }
        }
        catch { }

        // 3. Check PortNumber, UserAuthentication (NLA), and Shadow
        int port = 3389;
        try
        {
            using var rdpTcpKey = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp");
            if (rdpTcpKey != null)
            {
                if (rdpTcpKey.GetValue("PortNumber") is int pVal && pVal > 0 && pVal <= 65535)
                {
                    port = pVal;
                }

                if (rdpTcpKey.GetValue("UserAuthentication") is int nlaVal)
                {
                    info.NetworkLevelAuthRequired = (nlaVal == 1);
                }

                if (rdpTcpKey.GetValue("Shadow") is int shadowVal)
                {
                    info.ShadowMode = shadowVal;
                }
            }
        }
        catch { }
        info.Port = port;

        // 4. Check TermService status
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
            var (outStr, _, _) = await ProcessHelper.RunProcessAsync("sc.exe", "query TermService", cancellationToken: timeoutCts.Token);
            info.IsServiceRunning = outStr.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch { }

        // 5. Check if listener is active on Port
        info.IsListening = CheckIfPortListening(port);

        // 6. Termsrv.dll version
        info.TermsrvVersion = GetTermsrvVersion();

        // 7. RDP Wrapper Status
        var (wrapInstalled, wrapVer, hasIni) = CheckRdpWrapper();
        info.IsWrapperInstalled = wrapInstalled;
        info.WrapperVersion = wrapVer;
        info.HasWrapperIni = hasIni;

        // 8. IP & Hostname endpoints with actual port
        var (ip, host) = NetShareService.GetPrimaryLanEndpoints();
        info.ConnectAddressIp = !string.IsNullOrEmpty(ip) ? $"{ip}:{port}" : $"{port}";
        info.ConnectAddressHost = $"{host}:{port}";

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
            if (tsKey != null)
            {
                var fDeny = tsKey.GetValue("fDenyTSConnections");
                info.IsRdpEnabled = (fDeny is int val && val == 0);

                var fSingle = tsKey.GetValue("fSingleSessionPerUser");
                info.SingleSessionPerUser = !(fSingle is int sVal && sVal == 0);
            }
        }
        catch { }

        int port = 3389;
        try
        {
            using var rdpTcpKey = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp");
            if (rdpTcpKey != null)
            {
                if (rdpTcpKey.GetValue("PortNumber") is int pVal && pVal > 0 && pVal <= 65535)
                {
                    port = pVal;
                }
                if (rdpTcpKey.GetValue("UserAuthentication") is int nlaVal)
                {
                    info.NetworkLevelAuthRequired = (nlaVal == 1);
                }
                if (rdpTcpKey.GetValue("Shadow") is int shadowVal)
                {
                    info.ShadowMode = shadowVal;
                }
            }
        }
        catch { }
        info.Port = port;

        try
        {
            var proc = Process.GetProcessesByName("svchost");
            info.IsServiceRunning = proc.Length > 0;
        }
        catch { }

        info.IsListening = CheckIfPortListening(port);
        info.TermsrvVersion = GetTermsrvVersion();

        var (wrapInstalled, wrapVer, hasIni) = CheckRdpWrapper();
        info.IsWrapperInstalled = wrapInstalled;
        info.WrapperVersion = wrapVer;
        info.HasWrapperIni = hasIni;

        var (ip, host) = NetShareService.GetPrimaryLanEndpoints();
        info.ConnectAddressIp = !string.IsNullOrEmpty(ip) ? $"{ip}:{port}" : $"{port}";
        info.ConnectAddressHost = $"{host}:{port}";
        return info;
    }

    public async Task<(bool Success, string Message)> EnableRdpServerAsync(bool isHomeEdition, int port = 3389)
    {
        var results = new List<string>();

        // 1. Registry: fDenyTSConnections = 0
        try
        {
            using var tsKey = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Control\Terminal Server", true);
            if (tsKey != null)
            {
                tsKey.SetValue("fDenyTSConnections", 0, RegistryValueKind.DWord);
                results.Add("註冊表已啟用遠端桌面 (fDenyTSConnections = 0)。");
            }

            if (isHomeEdition)
            {
                using var licKey = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Control\Terminal Server\Licensing Core", true);
                licKey?.SetValue("EnableConcurrentSessions", 1, RegistryValueKind.DWord);

                using var ntKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Terminal Server", true);
                ntKey?.SetValue("TSAppCompat", 1, RegistryValueKind.DWord);
                results.Add("已套用家用版並發原則旗標 (Home Edition policy flags)。");
            }
        }
        catch (Exception ex)
        {
            return (false, $"註冊表設定失敗: {ex.Message}");
        }

        // 2. Open Firewall for port (TCP & UDP)
        try
        {
            await ProcessHelper.RunProcessAsync("netsh.exe", $"advfirewall firewall add rule name=\"Remote Desktop (TCP {port})\" dir=in action=allow protocol=TCP localport={port}");
            await ProcessHelper.RunProcessAsync("netsh.exe", $"advfirewall firewall add rule name=\"Remote Desktop (UDP {port})\" dir=in action=allow protocol=UDP localport={port}");
            results.Add($"已在防火牆放行連接埠 {port} (TCP/UDP)。");
        }
        catch { }

        // 3. Configure and start TermService
        try
        {
            await ProcessHelper.RunProcessAsync("sc.exe", "config TermService start= auto");
            await ProcessHelper.RunProcessAsync("net.exe", "start TermService");
            results.Add("已將 TermService 設為自動啟動並成功啟動服務。");
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
                return (true, "已成功停用遠端桌面連線 (fDenyTSConnections = 1)。");
            }
            catch (Exception ex)
            {
                return (false, $"停用 RDP 失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Updates the RDP listening port in registry, configures Windows Firewall, and restarts TermService.
    /// </summary>
    public async Task<(bool Success, string Message)> SetRdpPortAsync(int newPort)
    {
        if (newPort < 1 || newPort > 65535)
        {
            return (false, "無效的連接埠號 (範圍 1-65535)。");
        }

        try
        {
            // 1. Update Registry
            using (var key = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp", true))
            {
                key?.SetValue("PortNumber", newPort, RegistryValueKind.DWord);
            }

            // 2. Open Firewall rules for new port
            await ProcessHelper.RunProcessAsync("netsh.exe", $"advfirewall firewall add rule name=\"Remote Desktop (TCP {newPort})\" dir=in action=allow protocol=TCP localport={newPort}");
            await ProcessHelper.RunProcessAsync("netsh.exe", $"advfirewall firewall add rule name=\"Remote Desktop (UDP {newPort})\" dir=in action=allow protocol=UDP localport={newPort}");

            // 3. Restart TermService to bind new port
            var (restartOk, restartMsg) = await RestartTermServiceAsync();
            if (!restartOk)
            {
                return (true, $"連接埠已成功寫入為 {newPort}，防火牆已新增。提示: {restartMsg}");
            }

            return (true, $"已成功將遠端桌面連接埠切換為 {newPort}，防火牆已放行，服務已重啟！");
        }
        catch (Exception ex)
        {
            return (false, $"變更連接埠失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// Toggles single session per user vs concurrent multi-sessions (RDPWrap multi-user feature).
    /// </summary>
    public Task<(bool Success, string Message)> SetSingleSessionPerUserAsync(bool single)
    {
        return Task.Run(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Control\Terminal Server", true);
                key?.SetValue("fSingleSessionPerUser", single ? 1 : 0, RegistryValueKind.DWord);
                return (true, single 
                    ? "已設為單一工作階段模式 (相同帳號登入將踢掉舊階段)。" 
                    : "已啟用多使用者並發 / 多階段模式 (允許多個使用者同時連線不互踢)！");
            }
            catch (Exception ex)
            {
                return (false, $"修改多階段連線設定失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Toggles Network Level Authentication (NLA).
    /// </summary>
    public Task<(bool Success, string Message)> SetNetworkLevelAuthAsync(bool requireNla)
    {
        return Task.Run(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp", true);
                key?.SetValue("UserAuthentication", requireNla ? 1 : 0, RegistryValueKind.DWord);
                return (true, requireNla 
                    ? "已啟用網路層級驗證 (NLA，安全性較高)。" 
                    : "已關閉網路層級驗證 (允許所有終端、手機與相容客戶端連線)。");
            }
            catch (Exception ex)
            {
                return (false, $"修改 NLA 設定失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Sets Session Shadowing permissions (View / Control with or without consent).
    /// </summary>
    public Task<(bool Success, string Message)> SetShadowModeAsync(int shadowMode)
    {
        return Task.Run(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp", true);
                key?.SetValue("Shadow", shadowMode, RegistryValueKind.DWord);
                string desc = shadowMode switch
                {
                    0 => "停用畫面監視",
                    1 => "徵得使用者同意後完全控制",
                    2 => "無需同意直接完全控制",
                    3 => "徵得使用者同意僅檢視畫面",
                    4 => "無需同意直接檢視畫面",
                    _ => "自訂監視模式"
                };
                return (true, $"已更新遠端桌面畫面監視與控制模式: {desc}。");
            }
            catch (Exception ex)
            {
                return (false, $"修改監視模式失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Smoothly restarts TermService without rebooting Windows.
    /// </summary>
    public async Task<(bool Success, string Message)> RestartTermServiceAsync()
    {
        try
        {
            await ProcessHelper.RunProcessAsync("net.exe", "stop TermService /y");
            await Task.Delay(800);
            await ProcessHelper.RunProcessAsync("net.exe", "start TermService");
            await Task.Delay(500);
            return (true, "TermService 服務重啟成功。");
        }
        catch (Exception ex)
        {
            return (false, $"重啟服務失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// Auto-updates rdpwrap.ini from latest community GitHub/CDN repository.
    /// </summary>
    public async Task<(bool Success, string Message)> UpdateRdpWrapIniAsync()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string rdpWrapDir = Path.Combine(programFiles, "RDP Wrapper");
        if (!Directory.Exists(rdpWrapDir))
        {
            try { Directory.CreateDirectory(rdpWrapDir); } catch { }
        }

        string iniPath = Path.Combine(rdpWrapDir, "rdpwrap.ini");
        string[] mirrorUrls =
        {
            "https://cdn.jsdelivr.net/gh/sebaxakerhtc/rdpwrap.ini@master/rdpwrap.ini",
            "https://raw.githubusercontent.com/sebaxakerhtc/rdpwrap.ini/master/rdpwrap.ini",
            "https://raw.githubusercontent.com/affinityv/INI-RDP-Wrapper/master/rdpwrap.ini"
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Add("User-Agent", "DiskMasterPro-App/1.5.1");

        foreach (var url in mirrorUrls)
        {
            try
            {
                var content = await client.GetStringAsync(url);
                if (content.Contains("[Main]") || content.Contains("[10.0.") || content.Contains("[6.1."))
                {
                    if (File.Exists(iniPath))
                    {
                        try { File.Copy(iniPath, iniPath + ".bak", overwrite: true); } catch { }
                    }
                    await File.WriteAllTextAsync(iniPath, content);
                    await RestartTermServiceAsync();
                    return (true, $"已成功從社群鏡像更新 rdpwrap.ini 並重啟 TermService！(檔案大小: {content.Length / 1024} KB)");
                }
            }
            catch
            {
                // Try next mirror
            }
        }

        return (false, "下載最新 rdpwrap.ini 失敗，請確認網路連線或稍後再試。");
    }

    /// <summary>
    /// Enumerates active remote desktop sessions using qwinsta.
    /// </summary>
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

    public async Task<(bool Success, string Message)> DisconnectSessionAsync(int sessionId)
    {
        try
        {
            await ProcessHelper.RunProcessAsync("rwinsta.exe", $"{sessionId}");
            return (true, $"已中斷工作階段 #{sessionId}。");
        }
        catch (Exception ex)
        {
            return (false, $"中斷工作階段失敗: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> LogoffSessionAsync(int sessionId)
    {
        try
        {
            await ProcessHelper.RunProcessAsync("logoff.exe", $"{sessionId}");
            return (true, $"已強制登出工作階段 #{sessionId}。");
        }
        catch (Exception ex)
        {
            return (false, $"登出工作階段失敗: {ex.Message}");
        }
    }

    public void LaunchLoopbackTest(int port = 3389)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"/v:127.0.0.1:{port}",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static bool CheckIfPortListening(int port)
    {
        try
        {
            var ipProps = IPGlobalProperties.GetIPGlobalProperties();
            var tcpEndpoints = ipProps.GetActiveTcpListeners();
            return tcpEndpoints.Any(e => e.Port == port);
        }
        catch
        {
            return false;
        }
    }

    private static string GetTermsrvVersion()
    {
        try
        {
            string path = Path.Combine(Environment.SystemDirectory, "termsrv.dll");
            if (File.Exists(path))
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                return vi.FileVersion ?? vi.ProductVersion ?? "10.0";
            }
        }
        catch { }
        return "未知";
    }

    private static (bool Installed, string Version, bool HasIni) CheckRdpWrapper()
    {
        try
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string rdpWrapDir = Path.Combine(programFiles, "RDP Wrapper");
            string dllPath = Path.Combine(rdpWrapDir, "rdpwrap.dll");
            string iniPath = Path.Combine(rdpWrapDir, "rdpwrap.ini");

            bool hasDll = File.Exists(dllPath);
            bool hasIni = File.Exists(iniPath);

            string ver = "未安裝";
            if (hasDll)
            {
                var vi = FileVersionInfo.GetVersionInfo(dllPath);
                ver = vi.FileVersion ?? "已安裝";
            }

            return (hasDll, ver, hasIni);
        }
        catch
        {
            return (false, "未安裝", false);
        }
    }
}
