using System.Diagnostics;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

/// <summary>
/// One-Click NetShare Wizard & Windows LAN File Sharing Service.
/// Solves Microsoft Account NTLM authentication failures, Private network configuration,
/// Firewall File Sharing rules, and UNC path resolution.
/// </summary>
public class NetShareService
{
    public async Task<List<NetShareItem>> GetActiveSharesAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<NetShareItem>();
            try
            {
                var (outStr, _, _) = ProcessHelper.RunProcessAsync("net.exe", "share", cancellationToken: ct).GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(outStr)) return list;

                var (primaryIp, hostName) = GetPrimaryLanEndpoints();

                var lines = outStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                bool startParsing = false;

                foreach (var line in lines)
                {
                    if (line.StartsWith("---") || line.StartsWith("==="))
                    {
                        startParsing = true;
                        continue;
                    }

                    if (!startParsing) continue;
                    if (line.Contains("The command completed successfully", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("命令已經成功完成", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("命令成功完成", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    // Format: Share name   Resource   Remark
                    // Usually: ShareName   C:\Folder   Remark
                    var match = Regex.Match(line, @"^(\S+)\s+([A-Za-z]:\\[^\s]*|\\\\.*|[A-Za-z]:)\s*(.*)$");
                    if (match.Success)
                    {
                        var name = match.Groups[1].Value.Trim();
                        var resource = match.Groups[2].Value.Trim();
                        var remark = match.Groups[3].Value.Trim();

                        bool isAdmin = name.EndsWith('$') || name.Equals("IPC$", StringComparison.OrdinalIgnoreCase);

                        list.Add(new NetShareItem
                        {
                            ShareName = name,
                            ResourcePath = resource,
                            Remark = remark,
                            IsAdminShare = isAdmin,
                            UncIpPath = !string.IsNullOrEmpty(primaryIp) ? $@"\\{primaryIp}\{name}" : "",
                            UncHostPath = $@"\\{hostName}\{name}"
                        });
                    }
                    else
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var name = parts[0];
                            var resource = parts[1];
                            bool isAdmin = name.EndsWith('$');
                            list.Add(new NetShareItem
                            {
                                ShareName = name,
                                ResourcePath = resource,
                                Remark = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "",
                                IsAdminShare = isAdmin,
                                UncIpPath = !string.IsNullOrEmpty(primaryIp) ? $@"\\{primaryIp}\{name}" : "",
                                UncHostPath = $@"\\{hostName}\{name}"
                            });
                        }
                    }
                }
            }
            catch { }
            return list;
        }, ct);
    }

    public static (string PrimaryIp, string HostName) GetPrimaryLanEndpoints()
    {
        string hostName = Environment.MachineName;
        string primaryIp = "";

        try
        {
            var primaryNic = NetworkDiagnosticService.GetPrimaryPhysicalInternetAdapter();
            if (primaryNic != null)
            {
                var ip = primaryNic.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString();
                if (!string.IsNullOrEmpty(ip)) primaryIp = ip;
            }

            if (string.IsNullOrEmpty(primaryIp))
            {
                var adapters = NetworkDiagnosticService.GetAvailableNetworkAdapters();
                var first = adapters.FirstOrDefault(a => !string.IsNullOrEmpty(a.IPv4Address));
                if (first != null) primaryIp = first.IPv4Address;
            }
        }
        catch { }

        return (primaryIp, hostName);
    }

    public async Task<(bool Success, string Message, string UncIpPath, string UncHostPath)> CreateShareAsync(
        string shareName,
        string folderPath,
        string permission = "FULL",
        string? remark = null)
    {
        if (string.IsNullOrWhiteSpace(shareName)) return (false, "Share name cannot be empty.", "", "");
        if (!Directory.Exists(folderPath)) return (false, $"Directory does not exist: {folderPath}", "", "");

        var remarkArg = string.IsNullOrWhiteSpace(remark) ? "" : $"/remark:\"{remark}\"";
        var cmd = $"share \"{shareName}\"=\"{folderPath}\" /grant:Everyone,{permission} /users:unlimited {remarkArg}";

        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("net.exe", cmd);
        if (code != 0)
        {
            return (false, string.IsNullOrWhiteSpace(errStr) ? outStr : errStr, "", "");
        }

        var (primaryIp, hostName) = GetPrimaryLanEndpoints();
        string uncIp = !string.IsNullOrEmpty(primaryIp) ? $@"\\{primaryIp}\{shareName}" : "";
        string uncHost = $@"\\{hostName}\{shareName}";

        return (true, $"Share '{shareName}' created successfully!", uncIp, uncHost);
    }

    public async Task<(bool Success, string Message)> DeleteShareAsync(string shareName)
    {
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("net.exe", $"share \"{shareName}\" /delete /y");
        return (code == 0, code == 0 ? $"Share '{shareName}' deleted successfully." : errStr);
    }

    /// <summary>
    /// Solves the classic Windows Microsoft Account LAN access failure by setting LocalAccountTokenFilterPolicy = 1
    /// </summary>
    public (bool Success, string Message) FixMicrosoftAccountSharingPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true);
            if (key != null)
            {
                key.SetValue("LocalAccountTokenFilterPolicy", 1, RegistryValueKind.DWord);
                return (true, "LocalAccountTokenFilterPolicy=1 set successfully. Local/Microsoft account remote tokens are now allowed over SMB.");
            }
            return (false, "Failed to open registry key: HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System");
        }
        catch (Exception ex)
        {
            return (false, $"Registry write error: {ex.Message}");
        }
    }

    public bool IsMicrosoftAccountSharingPolicyFixed()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            var val = key?.GetValue("LocalAccountTokenFilterPolicy");
            return val is int i && i == 1;
        }
        catch { return false; }
    }

    public async Task<(bool Success, string Message)> SetNetworkProfilePrivateAsync()
    {
        var ps = "Get-NetConnectionProfile | Set-NetConnectionProfile -NetworkCategory Private";
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"{ps}\"");
        return (code == 0, code == 0 ? "Active network profile set to Private." : errStr);
    }

    public async Task<(bool Success, string Message)> EnableFileSharingFirewallAsync()
    {
        // Try English, Traditional Chinese, and Simplified Chinese rule group names
        string[] groups = ["File and Printer Sharing", "檔案及印表機共用", "文件和打印机共享"];
        var results = new List<string>();

        foreach (var grp in groups)
        {
            var (outStr, _, code) = await ProcessHelper.RunProcessAsync("netsh.exe", $"advfirewall firewall set rule group=\"{grp}\" new enable=Yes");
            if (code == 0)
            {
                return (true, $"Firewall rule group '{grp}' enabled successfully.");
            }
            results.Add(outStr);
        }

        return (false, "Failed to enable firewall rule group. Details: " + string.Join("; ", results));
    }

    public async Task<(bool Success, string Message)> CreateDedicatedShareUserAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "Username and password cannot be empty.");
        }

        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("net.exe", $"user \"{username}\" \"{password}\" /add /passwordchg:no");
        return (code == 0, code == 0 ? $"Local sharing user '{username}' created successfully!" : errStr);
    }
}
