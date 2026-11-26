using System.IO;
using DiskMasterWinUI.Models;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Enumerates Windows startup entries (HKCU/HKLM/WOW6432Node Run keys and Startup folders)
/// and toggles them cleanly &amp; reversibly using Windows native StartupApproved binary flags
/// (identical to Task Manager -> Startup Apps, never deleting the original Run entry).
/// </summary>
public class StartupManagerService
{
    private const string RunSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunWow64SubKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRunSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedFolderSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    private static readonly string[] HeavyBackgroundMarkers =
    [
        "discord", "teams", "ms-teams", "whatsapp", "spotify", "copilot",
        "open-video-downloader", "steam", "epicgames", "xbox", "googledrivefs",
        "onedrive", "mspcmanager", "figma", "wallpaper", "artcraft", "localsend",
        "cloudflare warp", "telegram", "slack"
    ];

    private static readonly string[] BrowserAutoLaunchMarkers =
    [
        "msedge.exe", "chrome.exe", "brave.exe", "opera.exe",
        "--no-startup-window", "--startup-foreground-launch", "--win-session-start"
    ];

    /// <summary>
    /// Decodes a Windows StartupApproved REG_BINARY value (12 bytes).
    /// Byte 0 even (0x02 / 0x06 / 0x00) = Enabled; odd (0x03 / 0x07 / 0x01) = Disabled.
    /// </summary>
    public static bool DecodeStartupApprovedEnabled(byte[]? raw)
    {
        if (raw == null || raw.Length == 0) return true;
        return (raw[0] & 0x01) == 0;
    }

    /// <summary>
    /// Encodes a 12-byte Windows StartupApproved REG_BINARY payload.
    /// </summary>
    public static byte[] EncodeStartupApprovedFlag(bool enable, DateTime? timestampUtc = null)
    {
        var data = new byte[12];
        if (enable)
        {
            data[0] = 0x02;
            // Bytes 4..11 remain 0 when enabled (Windows Task Manager convention)
        }
        else
        {
            data[0] = 0x03;
            long ft = (timestampUtc ?? DateTime.UtcNow).ToFileTimeUtc();
            BitConverter.GetBytes(ft).CopyTo(data, 4);
        }
        return data;
    }

    /// <summary>
    /// Scans all standard user &amp; machine startup entries, annotating duplicates,
    /// browser background auto-launchers, and heavy Electron/WebView2 apps.
    /// </summary>
    public Task<List<StartupEntryItem>> ScanStartupEntriesAsync()
    {
        return Task.Run(() =>
        {
            var items = new List<StartupEntryItem>();

            ReadRegistryRunHive(Registry.CurrentUser, RunSubKey, ApprovedRunSubKey, StartupSourceKind.HkcuRun, "使用者登錄檔 (HKCU\\Run)", items);
            ReadRegistryRunHive(Registry.LocalMachine, RunSubKey, ApprovedRunSubKey, StartupSourceKind.HklmRun, "系統登錄檔 (HKLM\\Run)", items);
            ReadRegistryRunHive(Registry.LocalMachine, RunWow64SubKey, ApprovedRun32SubKey, StartupSourceKind.HklmWow64Run, "系統 32-bit (HKLM\\WOW6432)", items);

            var userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            ReadStartupFolder(userStartup, Registry.CurrentUser, StartupSourceKind.UserStartupFolder, "使用者啟動資料夾", items);
            ReadStartupFolder(commonStartup, Registry.LocalMachine, StartupSourceKind.CommonStartupFolder, "共用啟動資料夾", items);

            AnnotateDiagnostics(items);

            return items
                .OrderByDescending(x => x.IsEnabled)
                .ThenByDescending(x => x.IsDuplicate || x.IsBrowserAutoLaunch || x.IsHeavyBackground)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        });
    }

    /// <summary>
    /// Enables or disables a startup entry reversibly via Windows StartupApproved.
    /// </summary>
    public Task<(bool Success, string Message)> SetStartupEntryEnabledAsync(StartupEntryItem item, bool enable)
    {
        return Task.Run(() =>
        {
            try
            {
                var (root, approvedSubKey) = GetApprovedTarget(item.SourceKind);
                using var key = root.CreateSubKey(approvedSubKey, writable: true);
                if (key == null)
                {
                    return (false, $"無法開啟 {approvedSubKey}（可能需要系統管理員權限）。");
                }

                var payload = EncodeStartupApprovedFlag(enable);
                key.SetValue(item.Name, payload, RegistryValueKind.Binary);
                item.IsEnabled = enable;

                string state = enable ? "已恢復開機啟動" : "已安全停用（保留原鍵值，可隨時恢復）";
                return (true, $"✅ {item.Name}: {state}");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, $"❌ 修改系統級啟動項 [{item.Name}] 需要系統管理員權限。");
            }
            catch (Exception ex)
            {
                return (false, $"❌ 切換啟動項 [{item.Name}] 失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Safely disables duplicate startup entries (keeping one instance) and browser background pre-launchers.
    /// </summary>
    public async Task<(int DisabledCount, string Summary)> DisableDuplicatesAndBrowserAutoLaunchAsync(
        IEnumerable<StartupEntryItem> entries,
        Action<string>? onLog = null)
    {
        int disabled = 0;
        var seenFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries.Where(e => e.IsEnabled))
        {
            bool shouldDisable = false;
            string reason = "";

            if (entry.IsBrowserAutoLaunch)
            {
                shouldDisable = true;
                reason = "瀏覽器背景預啟動";
            }
            else if (entry.IsDuplicate)
            {
                var family = NormalizeFamilyKey(entry);
                if (!seenFamilies.Add(family))
                {
                    shouldDisable = true;
                    reason = "重複常駐啟動項";
                }
            }

            if (shouldDisable)
            {
                var (ok, msg) = await SetStartupEntryEnabledAsync(entry, enable: false);
                if (ok)
                {
                    disabled++;
                    onLog?.Invoke($"▸ 已停用 [{entry.Name}] ({reason})");
                }
                else
                {
                    onLog?.Invoke($"▸ 略過 [{entry.Name}]: {msg}");
                }
            }
        }

        string summary = disabled > 0
            ? $"✅ 已安全停用 {disabled} 項重複／瀏覽器預啟動項目（全部皆可隨時在清單中一鍵還原）。"
            : "ℹ️ 目前沒有需要自動停用的重複或瀏覽器預啟動項目。";
        return (disabled, summary);
    }

    private static (RegistryKey Root, string ApprovedSubKey) GetApprovedTarget(StartupSourceKind kind) => kind switch
    {
        StartupSourceKind.HkcuRun => (Registry.CurrentUser, ApprovedRunSubKey),
        StartupSourceKind.HklmRun => (Registry.LocalMachine, ApprovedRunSubKey),
        StartupSourceKind.HklmWow64Run => (Registry.LocalMachine, ApprovedRun32SubKey),
        StartupSourceKind.UserStartupFolder => (Registry.CurrentUser, ApprovedFolderSubKey),
        StartupSourceKind.CommonStartupFolder => (Registry.LocalMachine, ApprovedFolderSubKey),
        _ => (Registry.CurrentUser, ApprovedRunSubKey)
    };

    private static void ReadRegistryRunHive(
        RegistryKey root,
        string runPath,
        string approvedPath,
        StartupSourceKind sourceKind,
        string sourceDisplay,
        List<StartupEntryItem> target)
    {
        try
        {
            using var runKey = root.OpenSubKey(runPath);
            if (runKey == null) return;

            using var approvedKey = root.OpenSubKey(approvedPath);

            foreach (var valueName in runKey.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(valueName)) continue;
                var cmd = runKey.GetValue(valueName)?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(cmd)) continue;

                var approvedBytes = approvedKey?.GetValue(valueName) as byte[];
                bool isEnabled = DecodeStartupApprovedEnabled(approvedBytes);

                target.Add(new StartupEntryItem
                {
                    Id = $"{sourceKind}:{valueName}",
                    Name = valueName,
                    Command = cmd,
                    ExecutablePath = ExtractExecutablePath(cmd),
                    SourceKind = sourceKind,
                    SourceDisplay = sourceDisplay,
                    IsEnabled = isEnabled
                });
            }
        }
        catch { }
    }

    private static void ReadStartupFolder(
        string folderPath,
        RegistryKey approvedRoot,
        StartupSourceKind sourceKind,
        string sourceDisplay,
        List<StartupEntryItem> target)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;
            using var approvedKey = approvedRoot.OpenSubKey(ApprovedFolderSubKey);

            foreach (var file in Directory.EnumerateFiles(folderPath))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                var approvedBytes = approvedKey?.GetValue(fileName) as byte[];
                bool isEnabled = DecodeStartupApprovedEnabled(approvedBytes);

                target.Add(new StartupEntryItem
                {
                    Id = $"{sourceKind}:{fileName}",
                    Name = fileName,
                    Command = file,
                    ExecutablePath = file,
                    SourceKind = sourceKind,
                    SourceDisplay = sourceDisplay,
                    IsEnabled = isEnabled
                });
            }
        }
        catch { }
    }

    private static void AnnotateDiagnostics(List<StartupEntryItem> items)
    {
        var familyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var fam = NormalizeFamilyKey(item);
            familyCounts[fam] = familyCounts.GetValueOrDefault(fam) + 1;
        }

        foreach (var item in items)
        {
            var combined = $"{item.Name} {item.Command}";
            var fam = NormalizeFamilyKey(item);

            bool isDup = familyCounts.GetValueOrDefault(fam) > 1;
            bool isBrowser = BrowserAutoLaunchMarkers.Any(m => combined.Contains(m, StringComparison.OrdinalIgnoreCase));
            bool isHeavy = HeavyBackgroundMarkers.Any(m => combined.Contains(m, StringComparison.OrdinalIgnoreCase));

            item.IsDuplicate = isDup;
            item.IsBrowserAutoLaunch = isBrowser;
            item.IsHeavyBackground = isHeavy;

            var badges = new List<string>();
            if (isDup) badges.Add("⚠ 重複常駐");
            if (isBrowser) badges.Add("🌐 瀏覽器預啟動");
            if (isHeavy) badges.Add("⚡ 高耗 RAM / WebView2");

            item.WarningBadge = string.Join(" · ", badges);
            item.ImpactLevel = (isDup || isBrowser || isHeavy) ? "高負擔" : "低負擔";
        }
    }

    private static string NormalizeFamilyKey(StartupEntryItem item)
    {
        var n = $"{item.Name} {item.ExecutablePath}".ToLowerInvariant();
        if (n.Contains("discord")) return "discord";
        if (n.Contains("googledrivefs")) return "googledrivefs";
        if (n.Contains("onedrive")) return "onedrive";
        if (n.Contains("msedge")) return "msedge";
        if (n.Contains("chrome")) return "chrome";
        if (n.Contains("teams")) return "teams";
        return Path.GetFileNameWithoutExtension(item.ExecutablePath).ToLowerInvariant() is { Length: > 0 } exe
            ? exe
            : item.Name.ToLowerInvariant();
    }

    private static string ExtractExecutablePath(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            int end = trimmed.IndexOf('"', 1);
            if (end > 1) return trimmed[1..end];
        }

        int space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }
}
