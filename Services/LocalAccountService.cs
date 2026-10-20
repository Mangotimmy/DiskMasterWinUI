using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Reads local Windows users/groups for the shared account picker. Results are cached briefly
/// because the UI may switch between Administrator and User scopes frequently.
/// </summary>
public class LocalAccountService
{
    private sealed record CacheEntry(DateTime LoadedUtc, List<LocalAccountItem> Items);
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    public async Task<List<LocalAccountItem>> GetGroupMembersAsync(
        string groupName,
        string? search = null,
        CancellationToken ct = default)
    {
        string? canonicalGroup = NormalizeGroup(groupName);
        if (canonicalGroup == null) return new List<LocalAccountItem>();

        List<LocalAccountItem> items;
        lock (CacheLock)
        {
            if (Cache.TryGetValue(canonicalGroup, out var entry) &&
                DateTime.UtcNow - entry.LoadedUtc < CacheLifetime)
            {
                items = entry.Items.Select(Clone).ToList();
                return Filter(items, search);
            }
        }

        items = await LoadGroupMembersAsync(canonicalGroup, ct).ConfigureAwait(false);
        lock (CacheLock)
        {
            Cache[canonicalGroup] = new CacheEntry(DateTime.UtcNow, items.Select(Clone).ToList());
        }
        return Filter(items, search);
    }

    public async Task<List<LocalAccountItem>> GetAccountsAsync(
        string? search = null,
        CancellationToken ct = default)
    {
        const string key = "__all_local_users__";
        List<LocalAccountItem> items;
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var entry) &&
                DateTime.UtcNow - entry.LoadedUtc < CacheLifetime)
            {
                return Filter(entry.Items.Select(Clone).ToList(), search);
            }
        }

        items = await LoadAllUsersAsync(ct).ConfigureAwait(false);
        lock (CacheLock)
        {
            Cache[key] = new CacheEntry(DateTime.UtcNow, items.Select(Clone).ToList());
        }
        return Filter(items, search);
    }

    public async Task<LocalAccountItem?> GetAccountAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var all = await GetAccountsAsync(null, ct).ConfigureAwait(false);
        var match = all.FirstOrDefault(a =>
            a.AccountName.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) ||
            a.AccountName.EndsWith("\\" + name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        foreach (var group in new[] { "Administrators", "Users" })
        {
            var members = await GetGroupMembersAsync(group, name.Trim(), ct).ConfigureAwait(false);
            match = members.FirstOrDefault(a => a.AccountName.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return null;
    }

    public void InvalidateCache()
    {
        lock (CacheLock) { Cache.Clear(); }
    }

    private static string? NormalizeGroup(string group)
    {
        if (group.Equals("Administrators", StringComparison.OrdinalIgnoreCase)) return "Administrators";
        if (group.Equals("Users", StringComparison.OrdinalIgnoreCase)) return "Users";
        return null;
    }

    private static List<LocalAccountItem> Filter(List<LocalAccountItem> items, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return items;
        string q = search.Trim();
        return items.Where(a =>
            a.AccountName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            a.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            a.Sid.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            a.AccountType.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static LocalAccountItem Clone(LocalAccountItem source) => new()
    {
        AccountName = source.AccountName,
        AccountType = source.AccountType,
        IsEnabled = source.IsEnabled,
        Description = source.Description,
        Sid = source.Sid,
        LastLogonDisplay = source.LastLogonDisplay,
        IsAdministrator = source.IsAdministrator,
        Source = source.Source
    };

    private async Task<List<LocalAccountItem>> LoadGroupMembersAsync(string group, CancellationToken ct)
    {
        string script = @"
$ErrorActionPreference = 'Stop'
$group = '__GROUP__'
$members = @(Get-LocalGroupMember -Group $group)
$users = @(Get-LocalUser -ErrorAction SilentlyContinue)
$rows = @()
foreach ($m in $members) {
    $sid = ''
    if ($m.SID) { $sid = $m.SID.Value }
    $u = $null
    if ($sid) { $u = $users | Where-Object { $_.SID -and $_.SID.Value -eq $sid } | Select-Object -First 1 }
    $enabled = $null
    if ($u) { $enabled = [bool]$u.Enabled }
    $desc = ''
    if ($u -and $u.Description) { $desc = [string]$u.Description }
    $last = ''
    if ($u -and $u.LastLogon) { $last = $u.LastLogon.ToString('yyyy-MM-dd HH:mm') }
    $type = 'User'
    if ($m.ObjectClass) { $type = [string]$m.ObjectClass }
    $rows += [PSCustomObject]@{
        AccountName = [string]$m.Name
        AccountType = $type
        IsEnabled = $enabled
        Description = $desc
        Sid = $sid
        LastLogonDisplay = $last
        IsAdministrator = ($group -eq 'Administrators')
        Source = 'Local'
    }
}
$rows | ConvertTo-Json -Compress -Depth 4 | Set-Content -LiteralPath '__TEMP__' -Encoding UTF8
";
        script = script.Replace("__GROUP__", group, StringComparison.Ordinal);
        var parsed = await RunScriptAsync(script, ct).ConfigureAwait(false);
        if (parsed != null) return parsed;

        return await LoadGroupFallbackAsync(group, ct).ConfigureAwait(false);
    }

    private async Task<List<LocalAccountItem>> LoadAllUsersAsync(CancellationToken ct)
    {
        string script = @"
$ErrorActionPreference = 'Stop'
$users = @(Get-LocalUser -ErrorAction SilentlyContinue)
$rows = @()
foreach ($u in $users) {
    $sid = ''
    if ($u.SID) { $sid = $u.SID.Value }
    $last = ''
    if ($u.LastLogon) { $last = $u.LastLogon.ToString('yyyy-MM-dd HH:mm') }
    $desc = ''
    if ($u.Description) { $desc = [string]$u.Description }
    $rows += [PSCustomObject]@{
        AccountName = [string]$u.Name
        AccountType = 'User'
        IsEnabled = [bool]$u.Enabled
        Description = $desc
        Sid = $sid
        LastLogonDisplay = $last
        IsAdministrator = $false
        Source = 'Local'
    }
}
$rows | ConvertTo-Json -Compress -Depth 4 | Set-Content -LiteralPath '__TEMP__' -Encoding UTF8
";
        var parsed = await RunScriptAsync(script, ct).ConfigureAwait(false);
        return parsed ?? new List<LocalAccountItem>();
    }

    private async Task<List<LocalAccountItem>?> RunScriptAsync(string script, CancellationToken ct)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"dm_accounts_{Guid.NewGuid():N}.json");
        string escaped = temp.Replace("'", "''");
        script = script.Replace("__TEMP__", escaped, StringComparison.Ordinal);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            char quote = (char)34;
            string backslash = ((char)92).ToString();
            string escapedScript = script.Replace(quote.ToString(), backslash + quote);
            string args = "-NoProfile -ExecutionPolicy Bypass -Command " + quote + escapedScript + quote;
            var (_, err, code) = await ProcessHelper.RunProcessAsync(
                "powershell.exe", args, cancellationToken: cts.Token).ConfigureAwait(false);
            if (code != 0 || !File.Exists(temp)) return null;

            string json = await File.ReadAllTextAsync(temp, Encoding.UTF8, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null") return new List<LocalAccountItem>();
            if (json.TrimStart().StartsWith("{", StringComparison.Ordinal)) json = "[" + json + "]";
            var items = JsonSerializer.Deserialize<List<LocalAccountItem>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return items ?? new List<LocalAccountItem>();
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Warning($"Local account query failed: {ex.Message}", "LocalAccount");
            return null;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private async Task<List<LocalAccountItem>> LoadGroupFallbackAsync(string group, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            var (output, err, code) = await ProcessHelper.RunProcessAsync(
                "net.exe", $"localgroup \"{group}\"", cancellationToken: cts.Token).ConfigureAwait(false);
            if (code != 0 || string.IsNullOrWhiteSpace(output)) return new List<LocalAccountItem>();

            var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            bool membersStarted = false;
            var result = new List<LocalAccountItem>();
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("===", StringComparison.Ordinal))
                {
                    membersStarted = true;
                    continue;
                }
                if (!membersStarted) continue;
                if (line.Contains("command completed", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("命令完成", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("コマンド", StringComparison.OrdinalIgnoreCase)) break;
                if (line.Length == 0) continue;
                string name = line.TrimStart('*').Trim();
                if (name.Length == 0) continue;
                result.Add(new LocalAccountItem
                {
                    AccountName = name,
                    AccountType = "Group/User",
                    IsEnabled = null,
                    Description = "",
                    Sid = "",
                    LastLogonDisplay = "",
                    IsAdministrator = group.Equals("Administrators", StringComparison.OrdinalIgnoreCase),
                    Source = "Fallback"
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Warning($"Local group fallback failed: {ex.Message}", "LocalAccount");
            return new List<LocalAccountItem>();
        }
    }
}
