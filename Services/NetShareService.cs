using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
    public string LastShareQueryError { get; private set; } = "";

    public async Task<List<NetShareItem>> GetActiveSharesAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var advanced = await GetActiveSharesViaSmbAsync(ct).ConfigureAwait(false);
        if (advanced.Count > 0)
        {
            DebugLogService.Instance.Debug($"NetShare enumerated {advanced.Count} SMB shares in {sw.ElapsedMilliseconds}ms", "NetShareService");
            return advanced;
        }

        var list = new List<NetShareItem>();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var (outStr, _, _) = await ProcessHelper.RunProcessAsync("net.exe", "share", cancellationToken: cts.Token);
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

            DebugLogService.Instance.Debug($"NetShare enumerated {list.Count} active shares in {sw.ElapsedMilliseconds}ms", "NetShareService");
            return list;
        }
        catch (OperationCanceledException)
        {
            DebugLogService.Instance.Warning($"NetShare query timed out after {sw.ElapsedMilliseconds}ms", "NetShareService");
            return list;
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Warning($"NetShare GetActiveSharesAsync failed: {ex.Message}", "NetShareService");
            return list;
        }
    }


    private async Task<List<NetShareItem>> GetActiveSharesViaSmbAsync(CancellationToken ct)
    {
        string script = @"
$shares = @(Get-SmbShare -ErrorAction Stop)
$rows = @()
foreach ($s in $shares) {
    $acl = @(Get-SmbShareAccess -Name $s.Name -ErrorAction SilentlyContinue)
    $summary = (@($acl | ForEach-Object { '{0}:{1}' -f $_.AccountName, $_.AccessRight }) -join '; ')
    $rows += [PSCustomObject]@{
        ShareName = [string]$s.Name
        ResourcePath = [string]$s.Path
        Remark = [string]$s.Description
        IsAdminShare = ([string]$s.Name).EndsWith('$')
        UncIpPath = ''
        UncHostPath = ''
        AccessSummary = $summary
        ConcurrentUserLimit = [int]$s.ConcurrentUserLimit
        CachingMode = [string]$s.CachingMode
        EncryptData = [bool]$s.EncryptData
        FolderEnumerationMode = [string]$s.FolderEnumerationMode
        PermissionCount = $acl.Count
    }
}
$rows | ConvertTo-Json -Compress -Depth 4 | Set-Content -LiteralPath '__TEMP__' -Encoding UTF8
";
        var rows = await RunJsonListAsync<NetShareItem>(script, ct).ConfigureAwait(false);
        if (rows.Count == 0) return rows;
        var (primaryIp, hostName) = GetPrimaryLanEndpoints();
        foreach (var row in rows)
        {
            row.UncIpPath = !string.IsNullOrEmpty(primaryIp) ? $@"\\{primaryIp}\{row.ShareName}" : "";
            row.UncHostPath = $@"\\{hostName}\{row.ShareName}";
        }
        return rows;
    }

    public Task<bool> IsMicrosoftAccountSharingPolicyFixedAsync() => Task.Run(IsMicrosoftAccountSharingPolicyFixed);

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
        // Phase 2 security default: grant Authenticated Users instead of Everyone.
        return await CreateShareWithPermissionAsync(
            shareName, folderPath, "Authenticated Users", permission, false, remark).ConfigureAwait(false);
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


    public async Task<ShareDetailsItem?> GetShareDetailsAsync(string shareName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shareName)) return null;
        string safe = shareName.Replace("'", "''");
        string script = @"
$s = Get-SmbShare -Name '__SHARE__' -ErrorAction Stop
@($s) | ForEach-Object {
    [PSCustomObject]@{
        ShareName = [string]$_.Name
        Path = [string]$_.Path
        Description = [string]$_.Description
        ConcurrentUserLimit = [int]$_.ConcurrentUserLimit
        CachingMode = [string]$_.CachingMode
        EncryptData = [bool]$_.EncryptData
        FolderEnumerationMode = [string]$_.FolderEnumerationMode
        AvailabilityType = [string]$_.AvailabilityType
        ScopeName = [string]$_.ScopeName
    }
} | ConvertTo-Json -Compress -Depth 4 | Set-Content -LiteralPath '__TEMP__' -Encoding UTF8
";
        script = script.Replace("__SHARE__", safe, StringComparison.Ordinal);
        var rows = await RunJsonListAsync<ShareDetailsItem>(script, ct).ConfigureAwait(false);
        var details = rows.FirstOrDefault();
        if (details == null) return null;
        details.AclEntries = await GetShareAclAsync(shareName, ct).ConfigureAwait(false);
        return details;
    }

    public async Task<List<ShareAclItem>> GetShareAclAsync(string shareName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shareName)) return new List<ShareAclItem>();
        string safe = shareName.Replace("'", "''");
        string script = @"
$acl = @(Get-SmbShareAccess -Name '__SHARE__' -ErrorAction Stop)
$rows = @()
foreach ($a in $acl) {
    $rows += [PSCustomObject]@{
        Principal = [string]$a.AccountName
        AccessRight = [string]$a.AccessRight
        AccessType = [string]$a.AccessControlType
        ScopeName = [string]$a.ScopeName
    }
}
$rows | ConvertTo-Json -Compress -Depth 4 | Set-Content -LiteralPath '__TEMP__' -Encoding UTF8
";
        script = script.Replace("__SHARE__", safe, StringComparison.Ordinal);
        return await RunJsonListAsync<ShareAclItem>(script, ct).ConfigureAwait(false);
    }

    public async Task<(bool Success, string Message)> GrantSharePermissionAsync(
        string shareName, string accountName, string accessRight, bool applyNtfs,
        string ntfsPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shareName) || string.IsNullOrWhiteSpace(accountName))
            return (false, "Share name and account are required.");
        if (!AdminHelper.IsRunningAsAdmin())
            return (false, "Administrator privileges are required to change share permissions.");

        string right = NormalizeShareRight(accessRight);
        string safeShare = shareName.Replace("'", "''");
        string safeAccount = accountName.Replace("'", "''");
        string script = @"
Grant-SmbShareAccess -Name '__SHARE__' -AccountName '__ACCOUNT__' -AccessRight '__RIGHT__' -Force -Confirm:$false -ErrorAction Stop
";
        script = script.Replace("__SHARE__", safeShare, StringComparison.Ordinal)
                       .Replace("__ACCOUNT__", safeAccount, StringComparison.Ordinal)
                       .Replace("__RIGHT__", right, StringComparison.Ordinal);
        var (output, error, code) = await RunPowerShellScriptAsync(script, ct).ConfigureAwait(false);
        if (code != 0) return (false, string.IsNullOrWhiteSpace(error) ? output : error);

        string message = $"Granted {right} share access to '{accountName}' on '{shareName}'.";
        if (applyNtfs && !string.IsNullOrWhiteSpace(ntfsPath))
        {
            var ntfs = new NtfsPermissionService();
            string rights = right.Equals("Full", StringComparison.OrdinalIgnoreCase) ? "F"
                : right.Equals("Change", StringComparison.OrdinalIgnoreCase) ? "M" : "R";
            int ntfsCode = await ntfs.GrantPermissionAsync(
                ntfsPath, accountName, rights, "(OI)(CI)", true, true,
                _ => { }, ct).ConfigureAwait(false);
            message += ntfsCode == 0 ? " NTFS permission applied." : " NTFS permission returned an error.";
        }
        return (true, message);
    }

    public async Task<(bool Success, string Message)> RevokeSharePermissionAsync(
        string shareName, string accountName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shareName) || string.IsNullOrWhiteSpace(accountName))
            return (false, "Share name and account are required.");
        if (!AdminHelper.IsRunningAsAdmin())
            return (false, "Administrator privileges are required to change share permissions.");

        string safeShare = shareName.Replace("'", "''");
        string safeAccount = accountName.Replace("'", "''");
        string script = @"
Revoke-SmbShareAccess -Name '__SHARE__' -AccountName '__ACCOUNT__' -Force -Confirm:$false -ErrorAction Stop
";
        script = script.Replace("__SHARE__", safeShare, StringComparison.Ordinal)
                       .Replace("__ACCOUNT__", safeAccount, StringComparison.Ordinal);
        var (output, error, code) = await RunPowerShellScriptAsync(script, ct).ConfigureAwait(false);
        return code == 0
            ? (true, $"Revoked share access for '{accountName}' on '{shareName}'.")
            : (false, string.IsNullOrWhiteSpace(error) ? output : error);
    }

    public async Task<(bool Success, string Message, string UncIpPath, string UncHostPath)> CreateShareAdvancedAsync(
        string shareName,
        string folderPath,
        string? description,
        int concurrentUserLimit,
        string cachingMode,
        bool encryptData,
        string folderEnumerationMode,
        IReadOnlyList<ShareAclGrantItem>? grants,
        bool applyNtfs,
        CancellationToken ct = default)
    {
        string name = shareName?.Trim() ?? "";
        if (!IsValidShareName(name)) return (false, "Share name contains invalid characters or is empty.", "", "");
        if (!Directory.Exists(folderPath)) return (false, $"Directory does not exist: {folderPath}", "", "");
        if (!AdminHelper.IsRunningAsAdmin()) return (false, "Administrator privileges are required to create a share.", "", "");

        var desired = (grants ?? Array.Empty<ShareAclGrantItem>())
            .Where(g => !string.IsNullOrWhiteSpace(g.AccountName))
            .GroupBy(g => g.AccountName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ShareAclGrantItem { AccountName = g.First().AccountName, AccessRight = NormalizeShareRight(g.First().AccessRight) })
            .ToList();
        if (desired.Count == 0) return (false, "Add at least one person with access before creating the share.", "", "");

        int limit = Math.Clamp(concurrentUserLimit, 0, 999999);
        string caching = NormalizeCachingMode(cachingMode);
        string enumeration = NormalizeEnumerationMode(folderEnumerationMode);
        string desc = description?.Trim() ?? "";
        string ps = "$ErrorActionPreference = 'Stop'" + Environment.NewLine;
        ps += "New-SmbShare -Name " + Ps(name) + " -Path " + Ps(folderPath) + " -Description " + Ps(desc) +
              " -ConcurrentUserLimit " + limit + " -CachingMode " + Ps(caching) +
              " -EncryptData $" + (encryptData ? "true" : "false") +
              " -FolderEnumerationMode " + Ps(enumeration) + Environment.NewLine;
        foreach (var grant in desired)
        {
            ps += "Grant-SmbShareAccess -Name " + Ps(name) + " -AccountName " + Ps(grant.AccountName) +
                  " -AccessRight " + Ps(grant.AccessRight) + " -Force -Confirm:$false" + Environment.NewLine;
        }
        if (!desired.Any(g => g.AccountName.Equals("Everyone", StringComparison.OrdinalIgnoreCase)))
        {
            // New-SmbShare may create a default Everyone/Read entry. Remove it so the editor's list is authoritative.
            ps += "Revoke-SmbShareAccess -Name " + Ps(name) + " -AccountName 'Everyone' -Force -Confirm:$false -ErrorAction SilentlyContinue" + Environment.NewLine;
        }

        var (output, error, code) = await RunPowerShellScriptAsync(ps, ct).ConfigureAwait(false);
        if (code != 0)
        {
            await TryRemoveShareAsync(name, ct).ConfigureAwait(false);
            if (IsMissingSmbCmdlet(error))
            {
                return await CreateBasicShareFallbackAsync(name, folderPath, desc, limit, desired, ct).ConfigureAwait(false);
            }
            return (false, string.IsNullOrWhiteSpace(error) ? output : error, "", "");
        }

        if (applyNtfs)
        {
            foreach (var grant in desired)
            {
                string rights = ShareRightToNtfs(grant.AccessRight);
                int ntfsCode = await new NtfsPermissionService().GrantPermissionAsync(
                    folderPath, grant.AccountName, rights, "(OI)(CI)", true, true, _ => { }, ct).ConfigureAwait(false);
                if (ntfsCode != 0)
                {
                    await TryRemoveShareAsync(name, ct).ConfigureAwait(false);
                    return (false, "Share was rolled back because NTFS permission application failed.", "", "");
                }
            }
        }

        var (primaryIp, hostName) = GetPrimaryLanEndpoints();
        string uncIp = !string.IsNullOrEmpty(primaryIp) ? $@"\\{primaryIp}\{name}" : "";
        string uncHost = $@"\\{hostName}\{name}";
        return (true, $"Share '{name}' created with {desired.Count} permission row(s).", uncIp, uncHost);
    }

    public async Task<(bool Success, string Message)> UpdateShareAdvancedAsync(
        string shareName,
        string? description,
        int concurrentUserLimit,
        string cachingMode,
        bool encryptData,
        string folderEnumerationMode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shareName)) return (false, "Share name is required.");
        if (!AdminHelper.IsRunningAsAdmin()) return (false, "Administrator privileges are required to change share settings.");

        int limit = Math.Clamp(concurrentUserLimit, 0, 999999);
        string ps = "$ErrorActionPreference = 'Stop'" + Environment.NewLine +
                    "Set-SmbShare -Name " + Ps(shareName) + " -Description " + Ps(description?.Trim() ?? "") +
                    " -ConcurrentUserLimit " + limit +
                    " -CachingMode " + Ps(NormalizeCachingMode(cachingMode)) +
                    " -EncryptData $" + (encryptData ? "true" : "false") +
                    " -FolderEnumerationMode " + Ps(NormalizeEnumerationMode(folderEnumerationMode)) +
                    " -Force -Confirm:$false";
        var (output, error, code) = await RunPowerShellScriptAsync(ps, ct).ConfigureAwait(false);
        return code == 0
            ? (true, "Share settings updated.")
            : (false, IsMissingSmbCmdlet(error) ? "Advanced share settings are not supported by this Windows build." : (string.IsNullOrWhiteSpace(error) ? output : error));
    }

    public async Task<(bool Success, string Message)> ApplyShareAclAsync(
        string shareName,
        IReadOnlyList<ShareAclGrantItem> desiredGrants,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shareName)) return (false, "Share name is required.");
        if (!AdminHelper.IsRunningAsAdmin()) return (false, "Administrator privileges are required to change share permissions.");

        var desired = (desiredGrants ?? Array.Empty<ShareAclGrantItem>())
            .Where(g => !string.IsNullOrWhiteSpace(g.AccountName))
            .GroupBy(g => g.AccountName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ShareAclGrantItem { AccountName = g.First().AccountName, AccessRight = NormalizeShareRight(g.First().AccessRight) })
            .ToList();
        if (desired.Count == 0) return (false, "At least one permission row is required.");

        var current = await GetShareAclAsync(shareName, ct).ConfigureAwait(false);
        string ps = "$ErrorActionPreference = 'Stop'" + Environment.NewLine;
        foreach (var grant in desired)
        {
            ps += "Grant-SmbShareAccess -Name " + Ps(shareName) + " -AccountName " + Ps(grant.AccountName) +
                  " -AccessRight " + Ps(grant.AccessRight) + " -Force -Confirm:$false" + Environment.NewLine;
        }
        foreach (var existing in current)
        {
            if (!desired.Any(d => d.AccountName.Equals(existing.Principal, StringComparison.OrdinalIgnoreCase)))
            {
                ps += "Revoke-SmbShareAccess -Name " + Ps(shareName) + " -AccountName " + Ps(existing.Principal) +
                      " -Force -Confirm:$false" + Environment.NewLine;
            }
        }

        var (output, error, code) = await RunPowerShellScriptAsync(ps, ct).ConfigureAwait(false);
        return code == 0
            ? (true, "Share permissions updated.")
            : (false, string.IsNullOrWhiteSpace(error) ? output : error);
    }

    private static bool IsValidShareName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.EndsWith('$')) return false;
        return !name.Any(c => c < 32 || "\\/:*?\"<>|".Contains(c));
    }

    private static string Ps(string value) => "'" + (value ?? "").Replace("'", "''") + "'";

    private static string NormalizeCachingMode(string? value)
    {
        string v = (value ?? "").Trim();
        if (v.Equals("No offline files", StringComparison.OrdinalIgnoreCase) || v.Equals("None", StringComparison.OrdinalIgnoreCase)) return "None";
        if (v.Equals("Documents", StringComparison.OrdinalIgnoreCase)) return "Documents";
        if (v.Equals("Programs", StringComparison.OrdinalIgnoreCase)) return "Programs";
        if (v.Equals("BranchCache", StringComparison.OrdinalIgnoreCase)) return "BranchCache";
        return "Manual";
    }

    private static string NormalizeEnumerationMode(string? value)
    {
        string v = (value ?? "").Trim();
        if (v.Equals("Access-based", StringComparison.OrdinalIgnoreCase) || v.Equals("AccessBased", StringComparison.OrdinalIgnoreCase)) return "AccessBased";
        if (v.Equals("Hide unspecified", StringComparison.OrdinalIgnoreCase) || v.Equals("HideUnspecified", StringComparison.OrdinalIgnoreCase)) return "HideUnspecified";
        if (v.Equals("Unrestricted", StringComparison.OrdinalIgnoreCase)) return "Unrestricted";
        return "AccessBased";
    }

    private static string ShareRightToNtfs(string right)
    {
        if (right.Equals("Full", StringComparison.OrdinalIgnoreCase)) return "F";
        if (right.Equals("Change", StringComparison.OrdinalIgnoreCase)) return "M";
        return "R";
    }

    private static bool IsMissingSmbCmdlet(string? error) =>
        !string.IsNullOrEmpty(error) &&
        (error.Contains("New-SmbShare", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("Set-SmbShare", StringComparison.OrdinalIgnoreCase)) &&
        (error.Contains("not recognized", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("CommandNotFound", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("無法辨識", StringComparison.OrdinalIgnoreCase));

    private async Task TryRemoveShareAsync(string shareName, CancellationToken ct)
    {
        try
        {
            await RunPowerShellScriptAsync("Remove-SmbShare -Name " + Ps(shareName) + " -Force -Confirm:$false", ct).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task<(bool Success, string Message, string UncIpPath, string UncHostPath)> CreateBasicShareFallbackAsync(
        string name, string folderPath, string description, int limit,
        IReadOnlyList<ShareAclGrantItem> grants, CancellationToken ct)
    {
        var first = grants[0];
        string right = first.AccessRight.ToLowerInvariant();
        string cmd = $"share \"{name}\"=\"{folderPath}\" /grant:\"{first.AccountName}\",{right} /users:{(limit == 0 ? "unlimited" : limit.ToString())}";
        if (!string.IsNullOrWhiteSpace(description)) cmd += $" /remark:\"{description}\"";
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("net.exe", cmd, cancellationToken: ct).ConfigureAwait(false);
        if (code != 0) return (false, string.IsNullOrWhiteSpace(errStr) ? outStr : errStr, "", "");
        var (primaryIp, hostName) = GetPrimaryLanEndpoints();
        string uncIp = !string.IsNullOrEmpty(primaryIp) ? $@"\\{primaryIp}\{name}" : "";
        string uncHost = $@"\\{hostName}\{name}";
        return (true, "Share created with basic permissions; advanced SMB options are unavailable on this Windows build.", uncIp, uncHost);
    }

    public async Task<(bool Success, string Message, string UncIpPath, string UncHostPath)> CreateShareWithPermissionAsync(
        string shareName, string folderPath, string accountName, string accessRight,
        bool applyNtfs, string? remark, CancellationToken ct = default)
    {
        string account = string.IsNullOrWhiteSpace(accountName) ? "Authenticated Users" : accountName.Trim();
        return await CreateShareAdvancedAsync(
            shareName, folderPath, remark, 0, "Manual", false, "AccessBased",
            new[] { new ShareAclGrantItem { AccountName = account, AccessRight = NormalizeShareRight(accessRight) } },
            applyNtfs, ct).ConfigureAwait(false);
    }

    public async Task<NtfsFeatureSummary> GetPathAclAsync(string path, CancellationToken ct = default)
    {
        return await new NtfsPermissionService().InspectNtfsFeaturesAsync(path, ct).ConfigureAwait(false);
    }

    private static string NormalizeShareRight(string accessRight)
    {
        if (accessRight.Equals("Full", StringComparison.OrdinalIgnoreCase) || accessRight.Equals("F", StringComparison.OrdinalIgnoreCase)) return "Full";
        if (accessRight.Equals("Change", StringComparison.OrdinalIgnoreCase) || accessRight.Equals("M", StringComparison.OrdinalIgnoreCase)) return "Change";
        return "Read";
    }

    private async Task<List<T>> RunJsonListAsync<T>(string script, CancellationToken ct)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"dm_share_{Guid.NewGuid():N}.json");
        string escaped = temp.Replace("'", "''");
        script = script.Replace("__TEMP__", escaped, StringComparison.Ordinal);
        var (_, error, code) = await RunPowerShellScriptAsync(script, ct).ConfigureAwait(false);
        try
        {
            if (code != 0)
            {
                LastShareQueryError = error;
                DebugLogService.Instance.Warning($"Share JSON query failed: {error}", "NetShareService");
                return new List<T>();
            }
            if (!File.Exists(temp))
            {
                LastShareQueryError = "JSON output file was not created.";
                return new List<T>();
            }
            string json = await File.ReadAllTextAsync(temp, Encoding.UTF8, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null") return new List<T>();
            if (json.TrimStart().StartsWith("{", StringComparison.Ordinal)) json = "[" + json + "]";
            return JsonSerializer.Deserialize<List<T>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private async Task<(string Output, string Error, int ExitCode)> RunPowerShellScriptAsync(
        string script, CancellationToken ct)
    {
        char quote = (char)34;
        string backslash = ((char)92).ToString();
        string escaped = script.Replace(quote.ToString(), backslash + quote);
        string args = "-NoProfile -ExecutionPolicy Bypass -Command " + quote + escaped + quote;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(12));
        return await ProcessHelper.RunProcessAsync("powershell.exe", args, cancellationToken: cts.Token).ConfigureAwait(false);
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
