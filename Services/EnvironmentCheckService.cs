using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskMasterWinUI.Helpers;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DiskMasterWinUI.Services;

public partial class ToolEnvironmentItem : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _executableName = "";
    [ObservableProperty] private string _category = "System";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private string _pathFound = "";
    [ObservableProperty] private string _wingetPackageId = "";

    public bool CanInstallViaWinget => !string.IsNullOrEmpty(WingetPackageId) && !IsAvailable;
    public bool CanOpenFolder => IsAvailable && !string.IsNullOrWhiteSpace(PathFound) && File.Exists(PathFound);
    public string StatusGlyph => IsAvailable ? "\uE73E" : "\uE7BA"; // Checkmark vs Warning
    public string StatusText => IsAvailable ? "已就緒 (Ready)" : "未安裝 (Missing)";
    public SolidColorBrush StatusBrush => IsAvailable
        ? new SolidColorBrush(Color.FromArgb(255, 46, 160, 67))   // Semantic Success Green
        : new SolidColorBrush(Color.FromArgb(255, 229, 83, 75));  // Semantic Danger Red
}

public partial class EnvironmentVariableEntry : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _expandedValue = "";
    [ObservableProperty] private string _scope = "User"; // User | Machine | Process

    public string ScopeDisplay => Scope switch
    {
        "Machine" => "🛡️ 系統 (Machine)",
        "User" => "👤 使用者 (User)",
        _ => "⚙️ 處理程序 (Process)"
    };

    public EnvironmentVariableTarget TargetEnum => Scope switch
    {
        "Machine" => EnvironmentVariableTarget.Machine,
        "User" => EnvironmentVariableTarget.User,
        _ => EnvironmentVariableTarget.Process
    };

    public int CharacterCount => Value?.Length ?? 0;
    public string LengthDisplay => $"{CharacterCount:N0} 字元";

    public bool HasUnexpandedTokens => !string.IsNullOrEmpty(Value) && Value.Contains('%');

    public bool IsPathStatusValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExpandedValue)) return false;
            if (Name.Equals("Path", StringComparison.OrdinalIgnoreCase)) return true;
            if (ExpandedValue.Length >= 3 && ExpandedValue[1] == ':' && (ExpandedValue[2] == '\\' || ExpandedValue[2] == '/') && !ExpandedValue.Contains(';'))
            {
                return Directory.Exists(ExpandedValue) || File.Exists(ExpandedValue);
            }
            return true;
        }
    }

    public string StatusGlyph => IsPathStatusValid ? "\uE73E" : "\uE7BA";
    public string StatusBadgeText => IsPathStatusValid ? "有效 (Valid)" : "路徑異常 (Invalid)";
    public SolidColorBrush StatusBrush => IsPathStatusValid
        ? new SolidColorBrush(Color.FromArgb(255, 46, 160, 67))
        : new SolidColorBrush(Color.FromArgb(255, 210, 153, 34));
}

public partial class PathDirectoryEntry : ObservableObject
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private string _rawPath = "";
    [ObservableProperty] private string _expandedPath = "";
    [ObservableProperty] private string _scope = "Machine"; // Machine | User
    [ObservableProperty] private bool _exists;
    [ObservableProperty] private bool _isDuplicate;
    [ObservableProperty] private int _executableCount;

    public string IndexDisplay => $"#{Index:D2}";

    public string ScopeDisplay => Scope == "Machine" ? "🛡️ 系統 PATH" : "👤 使用者 PATH";

    public EnvironmentVariableTarget TargetEnum => Scope == "Machine"
        ? EnvironmentVariableTarget.Machine
        : EnvironmentVariableTarget.User;

    public string StatusGlyph => !Exists ? "\uE711" : (IsDuplicate ? "\uE7BA" : "\uE73E");

    public string StatusText => !Exists
        ? "❌ 目錄不存在"
        : (IsDuplicate ? "⚠️ 重複項目" : $"✅ 有效 ({ExecutableCount} 個執行檔)");

    public SolidColorBrush StatusBrush => !Exists
        ? new SolidColorBrush(Color.FromArgb(255, 229, 83, 75))
        : (IsDuplicate
            ? new SolidColorBrush(Color.FromArgb(255, 210, 153, 34))
            : new SolidColorBrush(Color.FromArgb(255, 46, 160, 67)));
}

public class EnvironmentCheckService
{
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HWND_BROADCAST = new(0xffff);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    public static void BroadcastEnvironmentChange()
    {
        try
        {
            SendMessageTimeoutW(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 1500, out _);
        }
        catch { }
    }

    public async Task<List<ToolEnvironmentItem>> CheckAllToolsAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<ToolEnvironmentItem>
            {
                CheckTool("DiskPart CLI", "diskpart.exe", "磁碟分割", "Windows 核心磁碟與磁碟區分割管理引擎", ""),
                CheckTool("Defrag / SSD TRIM", "defrag.exe", "磁碟最佳化", "磁碟區重組與 SSD TRIM 指令優化工具", ""),
                CheckTool("DISM 映像部署工具", "dism.exe", "系統映像", "WIM/ESD 掛載、驅動注入與系統映像健康修復", ""),
                CheckTool("SFC 系統檔案檢查", "sfc.exe", "系統修復", "掃描並修復受損的 Windows 系統核心保護檔", ""),
                CheckTool("BCD 啟動組態編輯", "bcdedit.exe", "開機管理", "UEFI / BCD 開機項目與選單資料庫編輯器", ""),
                CheckTool("BCDBoot 開機重建", "bcdboot.exe", "開機管理", "重建 EFI/MBR 啟動分割區檔案與 BCD 資料庫", ""),
                CheckTool("Robocopy 高速備份", "robocopy.exe", "備份同步", "多執行緒容錯目錄對拷與鏡像備份引擎", ""),
                CheckTool("iCACLS 權限工具", "icacls.exe", "安全權限", "NTFS 檔案系統存取控制清單 (ACL) 備份與重設", ""),
                CheckTool("Netsh 網路組態", "netsh.exe", "網路調校", "TCP 全域自動調校 (autotuninglevel) 與介面設定", ""),
                CheckTool("WSL2 (EXT4 支援)", "wsl.exe", "跨平台掛載", "Windows Subsystem for Linux (掛載 EXT4 實體磁區)", "Microsoft.WSL"),
                CheckTool("Windows File Recovery", "winfr.exe", "資料救援", "微軟官方 NTFS/FAT/exFAT 已刪除檔案深層救援 CLI", "9N26S50LN705")
            };
            return list;
        });
    }

    private static ToolEnvironmentItem CheckTool(string name, string exeName, string category, string desc, string wingetId)
    {
        var item = new ToolEnvironmentItem
        {
            Name = name,
            ExecutableName = exeName,
            Category = category,
            Description = desc,
            WingetPackageId = wingetId
        };

        var sysPath = Path.Combine(Environment.SystemDirectory, exeName);
        if (File.Exists(sysPath))
        {
            item.IsAvailable = true;
            item.PathFound = sysPath;
            return item;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = p.Trim().Trim('"');
                // A disconnected UNC entry makes File.Exists wait for SMB timeouts.
                if (candidate.StartsWith(@"\\", StringComparison.Ordinal)) continue;
                var full = Path.Combine(candidate, exeName);
                if (File.Exists(full))
                {
                    item.IsAvailable = true;
                    item.PathFound = full;
                    return item;
                }
            }
            catch { }
        }

        var localApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", exeName);
        if (File.Exists(localApps))
        {
            item.IsAvailable = true;
            item.PathFound = localApps;
            return item;
        }

        item.IsAvailable = false;
        item.PathFound = "未在系統 PATH 中找到 (Not found in PATH)";
        return item;
    }

    public async Task<string> InstallViaWingetAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var (stdout, stderr, _) = await ProcessHelper.RunProcessAsync(
            "winget.exe",
            $"install --id \"{packageId}\" --accept-source-agreements --accept-package-agreements --silent",
            cancellationToken: cancellationToken);

        return $"{stdout}\n{stderr}";
    }

    // ── Environment Variables Table Operations ──

    public Task<List<EnvironmentVariableEntry>> GetEnvironmentVariablesAsync(string scopeFilter = "All")
    {
        return Task.Run(() =>
        {
            var results = new List<EnvironmentVariableEntry>();

            void CollectFromTarget(EnvironmentVariableTarget target, string scopeName)
            {
                try
                {
                    var dict = Environment.GetEnvironmentVariables(target);
                    foreach (System.Collections.DictionaryEntry de in dict)
                    {
                        var key = de.Key?.ToString() ?? "";
                        var val = de.Value?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(key)) continue;

                        results.Add(new EnvironmentVariableEntry
                        {
                            Name = key,
                            Value = val,
                            ExpandedValue = Environment.ExpandEnvironmentVariables(val),
                            Scope = scopeName
                        });
                    }
                }
                catch { }
            }

            if (scopeFilter.Equals("Machine", StringComparison.OrdinalIgnoreCase))
            {
                CollectFromTarget(EnvironmentVariableTarget.Machine, "Machine");
            }
            else if (scopeFilter.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                CollectFromTarget(EnvironmentVariableTarget.User, "User");
            }
            else if (scopeFilter.Equals("Process", StringComparison.OrdinalIgnoreCase))
            {
                CollectFromTarget(EnvironmentVariableTarget.Process, "Process");
            }
            else
            {
                CollectFromTarget(EnvironmentVariableTarget.Machine, "Machine");
                CollectFromTarget(EnvironmentVariableTarget.User, "User");
            }

            return results
                .OrderBy(e => e.Scope == "Machine" ? 0 : (e.Scope == "User" ? 1 : 2))
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        });
    }

    public Task<(bool Success, string Message)> SetEnvironmentVariableAsync(
        string name,
        string value,
        EnvironmentVariableTarget target)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return (false, "變數名稱不可為空白。");

            var cleanName = name.Trim();
            if (cleanName.Contains('='))
                return (false, "變數名稱不可包含 '=' 字元。");

            if (target == EnvironmentVariableTarget.Machine && !AdminHelper.IsRunningAsAdmin())
                return (false, "修改「系統 (Machine)」環境變數需要系統管理員權限。請先以系統管理員身分重新啟動，或改選「使用者 (User)」範圍。");

            try
            {
                Environment.SetEnvironmentVariable(cleanName, value ?? "", target);
                // Also sync current process so tools inside DiskMaster immediately see the updated value
                Environment.SetEnvironmentVariable(cleanName, value ?? "", EnvironmentVariableTarget.Process);
                BroadcastEnvironmentChange();
                return (true, $"已成功儲存環境變數 [{cleanName}] ({target}) 並廣播 WM_SETTINGCHANGE。");
            }
            catch (Exception ex)
            {
                return (false, $"寫入環境變數失敗: {ex.Message}");
            }
        });
    }

    public Task<(bool Success, string Message)> DeleteEnvironmentVariableAsync(
        string name,
        EnvironmentVariableTarget target)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return (false, "請先選擇要刪除的環境變數。");

            if (target == EnvironmentVariableTarget.Machine && !AdminHelper.IsRunningAsAdmin())
                return (false, "刪除「系統 (Machine)」環境變數需要系統管理員權限。");

            try
            {
                Environment.SetEnvironmentVariable(name.Trim(), null, target);
                BroadcastEnvironmentChange();
                return (true, $"已刪除環境變數 [{name.Trim()}] ({target})。");
            }
            catch (Exception ex)
            {
                return (false, $"刪除環境變數失敗: {ex.Message}");
            }
        });
    }

    // ── PATH Directory Table Operations ──

    public Task<List<PathDirectoryEntry>> GetPathDirectoriesAsync(string scopeFilter = "All")
    {
        return Task.Run(() =>
        {
            var list = new List<PathDirectoryEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int idx = 1;

            void CollectPath(EnvironmentVariableTarget target, string scopeName)
            {
                try
                {
                    var rawPathVar = Environment.GetEnvironmentVariable("Path", target) ?? "";
                    var segments = rawPathVar.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var seg in segments)
                    {
                        var trimmed = seg.Trim().Trim('"');
                        if (string.IsNullOrWhiteSpace(trimmed)) continue;

                        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
                        // Never probe disconnected SMB shares while building the PATH table.
                        if (expanded.StartsWith(@"\\", StringComparison.Ordinal)) continue;
                        var normalized = expanded.TrimEnd('\\', '/');
                        bool isDup = !seen.Add($"{scopeName}:{normalized}");
                        bool exists = Directory.Exists(expanded);
                        int exeCount = 0;

                        if (exists)
                        {
                            try
                            {
                                exeCount = Directory.EnumerateFiles(expanded, "*.*", SearchOption.TopDirectoryOnly)
                                    .Count(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                             || f.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                                             || f.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                                             || f.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase));
                            }
                            catch { }
                        }

                        list.Add(new PathDirectoryEntry
                        {
                            Index = idx++,
                            RawPath = trimmed,
                            ExpandedPath = expanded,
                            Scope = scopeName,
                            Exists = exists,
                            IsDuplicate = isDup,
                            ExecutableCount = exeCount
                        });
                    }
                }
                catch { }
            }

            if (scopeFilter.Equals("Machine", StringComparison.OrdinalIgnoreCase))
            {
                CollectPath(EnvironmentVariableTarget.Machine, "Machine");
            }
            else if (scopeFilter.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                CollectPath(EnvironmentVariableTarget.User, "User");
            }
            else
            {
                CollectPath(EnvironmentVariableTarget.Machine, "Machine");
                CollectPath(EnvironmentVariableTarget.User, "User");
            }

            return list;
        });
    }

    public static (bool Success, string Message) AddPathDirectoryDirect(
        string directoryPath,
        EnvironmentVariableTarget target)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return (false, "請輸入或瀏覽要加入 PATH 的資料夾路徑。");

        var cleanDir = directoryPath.Trim().Trim('"');
        if (File.Exists(cleanDir) || (!Directory.Exists(cleanDir) && Path.HasExtension(cleanDir)))
        {
            var parent = Path.GetDirectoryName(cleanDir);
            if (!string.IsNullOrEmpty(parent)) cleanDir = parent;
        }

        if (cleanDir.Length == 2 && char.IsLetter(cleanDir[0]) && cleanDir[1] == ':')
        {
            cleanDir += "\\";
        }
        else if (cleanDir.Length > 3)
        {
            cleanDir = cleanDir.TrimEnd('\\', '/');
        }

        if (string.IsNullOrWhiteSpace(cleanDir))
            return (false, "無效的資料夾路徑。");

        if (target == EnvironmentVariableTarget.Machine && !AdminHelper.IsRunningAsAdmin())
            return (false, "修改「系統 PATH」需要系統管理員權限，請改選「使用者 PATH」或以管理員身分執行。");

        try
        {
            var current = Environment.GetEnvironmentVariable("Path", target) ?? "";
            var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (parts.Any(p => string.Equals(p.TrimEnd('\\', '/'), cleanDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            {
                var scopeText = target == EnvironmentVariableTarget.Machine ? "系統" : "使用者";
                return (false, $"路徑 [{cleanDir}] 已存在於 {scopeText} PATH 中，無需重複加入。");
            }

            parts.Add(cleanDir);
            var updated = string.Join(";", parts);
            Environment.SetEnvironmentVariable("Path", updated, target);

            // Also append to current process PATH
            var procPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process) ?? "";
            Environment.SetEnvironmentVariable("Path", procPath.TrimEnd(';') + ";" + cleanDir, EnvironmentVariableTarget.Process);

            BroadcastEnvironmentChange();
            var targetScopeName = target == EnvironmentVariableTarget.Machine ? "系統 (System)" : "使用者 (User)";
            return (true, $"已將 [{cleanDir}] 成功加入 {targetScopeName} PATH 並立即生效。");
        }
        catch (Exception ex)
        {
            return (false, $"加入 PATH 失敗: {ex.Message}");
        }
    }

    public Task<(bool Success, string Message)> AddPathDirectoryAsync(
        string directoryPath,
        EnvironmentVariableTarget target)
    {
        return Task.Run(() => AddPathDirectoryDirect(directoryPath, target));
    }

    public Task<(bool Success, string Message)> RemovePathDirectoryAsync(
        string rawPath,
        EnvironmentVariableTarget target)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return (false, "請選擇要移除的 PATH 目錄項目。");

            if (target == EnvironmentVariableTarget.Machine && !AdminHelper.IsRunningAsAdmin())
                return (false, "修改「系統 PATH」需要系統管理員權限。");

            try
            {
                var current = Environment.GetEnvironmentVariable("Path", target) ?? "";
                var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                int removed = parts.RemoveAll(p => string.Equals(p.Trim().Trim('"'), rawPath.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                    return (false, $"在 {target} PATH 中找不到 [{rawPath}]。");

                Environment.SetEnvironmentVariable("Path", string.Join(";", parts), target);
                BroadcastEnvironmentChange();
                return (true, $"已從 {target} PATH 移除 [{rawPath}]。");
            }
            catch (Exception ex)
            {
                return (false, $"移除 PATH 項目失敗: {ex.Message}");
            }
        });
    }

    public Task<(bool Success, int RemovedCount, string Message)> CleanInvalidPathDirectoriesAsync(EnvironmentVariableTarget target)
    {
        return Task.Run(() =>
        {
            if (target == EnvironmentVariableTarget.Machine && !AdminHelper.IsRunningAsAdmin())
                return (false, 0, "清理「系統 PATH」需要系統管理員權限。");

            try
            {
                var current = Environment.GetEnvironmentVariable("Path", target) ?? "";
                var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var validList = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int removed = 0;

                foreach (var p in parts)
                {
                    var trimmed = p.Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    var expanded = Environment.ExpandEnvironmentVariables(trimmed);
                    var normalized = expanded.TrimEnd('\\', '/');
                    if (!Directory.Exists(expanded) || !seen.Add(normalized))
                    {
                        removed++;
                        continue;
                    }
                    validList.Add(trimmed);
                }

                if (removed > 0)
                {
                    Environment.SetEnvironmentVariable("Path", string.Join(";", validList), target);
                    BroadcastEnvironmentChange();
                }

                return (true, removed, removed > 0
                    ? $"已從 {target} PATH 清理 {removed} 個失效或重複的目錄項目。"
                    : $"{target} PATH 中的所有目錄皆有效且無重複項目。");
            }
            catch (Exception ex)
            {
                return (false, 0, $"清理 PATH 失敗: {ex.Message}");
            }
        });
    }

    public async Task<(bool Success, string Message)> ExportEnvironmentBackupAsync(string filePath)
    {
        try
        {
            var machineVars = await GetEnvironmentVariablesAsync("Machine");
            var userVars = await GetEnvironmentVariablesAsync("User");
            var pathDirs = await GetPathDirectoriesAsync("All");

            var payload = new
            {
                ExportedAtUtc = DateTime.UtcNow.ToString("O"),
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                MachineVariables = machineVars.ToDictionary(k => k.Name, v => v.Value),
                UserVariables = userVars.ToDictionary(k => k.Name, v => v.Value),
                PathBreakdown = pathDirs.Select(p => new
                {
                    p.Index,
                    p.Scope,
                    p.RawPath,
                    p.ExpandedPath,
                    p.Exists,
                    p.IsDuplicate,
                    p.ExecutableCount
                })
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
            return (true, $"環境變數與 PATH 表格備份已匯出至: {filePath}");
        }
        catch (Exception ex)
        {
            return (false, $"匯出環境變數備份失敗: {ex.Message}");
        }
    }

    public void OpenWindowsEnvironmentDialog()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = "sysdm.cpl,EditEnvironmentVariables",
                UseShellExecute = true
            });
        }
        catch { }
    }
}
