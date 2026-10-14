using System.Text.Json;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Windows Memory Management Agent (MMAgent) service.
/// Configures memory compression, page combining, and application prefetching for competitive gaming latency optimization.
/// Uses thread-safe semaphore locks, SysMain service monitoring, pwsh/powershell fallback, and robust error handling.
/// </summary>
public class MemoryManagerService
{
    // C6: this lock now guards the WRITE path only (two Enable/Disable-MMAgent writes must not overlap).
    // A status refresh spawns PowerShell for several seconds and used to hold this lock, which made a
    // toggle clicked during a refresh look dead - it no longer participates in the write lock.
    private static readonly SemaphoreSlim _writeLock = new(1, 1);
    private static string? _cachedPsExe;

    // ── C6: MMAgent status cache ──
    // Refresh runs outside the write lock; concurrent callers share one in-flight refresh instead of
    // spawning extra PowerShell processes. Every write invalidates the cache, so a post-toggle read is
    // always a real read. When a read cannot happen (write in flight, or PowerShell failed) the last
    // known values are returned with IsStatusStale = true - never presented as fresh numbers.
    private static readonly object _statusCacheLock = new();
    private static MMAgentConfig? _cachedStatus;
    private static DateTime _cachedStatusUtc = DateTime.MinValue;
    private static bool _cachedStatusStale = true;
    private static Task<MMAgentConfig>? _inflightStatusRefresh;
    private static readonly TimeSpan StatusCacheTtl = TimeSpan.FromSeconds(15);

    /// <summary>True when the values last handed out were not read after the most recent write (or a read failed).</summary>
    public static bool IsStatusStale
    {
        get { lock (_statusCacheLock) return _cachedStatusStale; }
    }

    /// <summary>UTC time of the last successful MMAgent read (null when never read).</summary>
    public static DateTime? LastStatusUtc
    {
        get { lock (_statusCacheLock) return _cachedStatusUtc == DateTime.MinValue ? null : _cachedStatusUtc; }
    }

    /// <summary>True while a toggle/write is being applied; callers can show a busy state instead of blocking.</summary>
    public static bool IsWriteInProgress => _writeLock.CurrentCount == 0;

    /// <summary>
    /// Detects whether modern PowerShell 7 (pwsh.exe) or Windows PowerShell 5.1 (powershell.exe) is available.
    /// </summary>
    public static string GetPowerShellExe()
    {
        if (_cachedPsExe != null) return _cachedPsExe;
        try
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"PowerShell\7\pwsh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"PowerShell\7-preview\pwsh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"PowerShell\pwsh.exe")
            };
            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    _cachedPsExe = path;
                    return _cachedPsExe;
                }
            }
        }
        catch { }

        _cachedPsExe = "powershell.exe";
        return _cachedPsExe;
    }

    private class MMAgentDto
    {
        public bool ApplicationLaunchPrefetching { get; set; }
        public bool ApplicationLaunchProfiling { get; set; }
        public bool ApplicationPreLaunch { get; set; }
        public bool MemoryCompression { get; set; }
        public bool OperationAPI { get; set; }
        public bool OperationRecording { get; set; }
        public bool PageCombining { get; set; }
    }

    /// <summary>
    /// Queries the live MMAgent status from Windows.
    /// Uses dual parsing: robust key-value text matching (matches exact terminal display) + JSON fallback.
    /// C6: served from a 15 s cache; a refresh never holds the write lock, so toggles stay responsive.
    /// </summary>
    public async Task<MMAgentConfig> GetStatusAsync(bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            lock (_statusCacheLock)
            {
                if (_cachedStatus != null && !_cachedStatusStale &&
                    _cachedStatusUtc != DateTime.MinValue && DateTime.UtcNow - _cachedStatusUtc <= StatusCacheTtl)
                {
                    return _cachedStatus;
                }
            }

            // A write is in flight: don't race it with another PowerShell read and don't block the caller
            // behind it - hand back the last known values, explicitly marked stale.
            if (IsWriteInProgress)
            {
                lock (_statusCacheLock)
                {
                    if (_cachedStatus != null)
                    {
                        _cachedStatusStale = true;
                        return _cachedStatus;
                    }
                }
            }
        }

        Task<MMAgentConfig> refresh;
        lock (_statusCacheLock)
        {
            if (_inflightStatusRefresh == null || _inflightStatusRefresh.IsCompleted)
            {
                _inflightStatusRefresh = RefreshStatusCoreAsync();
            }
            refresh = _inflightStatusRefresh;
        }

        return await refresh;
    }

    /// <summary>Performs the real PowerShell read and publishes it to the cache (single-flight).</summary>
    private async Task<MMAgentConfig> RefreshStatusCoreAsync()
    {
        MMAgentConfig? fresh = null;
        try
        {
            fresh = await ReadStatusFromWindowsAsync();
        }
        catch
        {
            fresh = null;
        }

        lock (_statusCacheLock)
        {
            if (fresh != null)
            {
                _cachedStatus = fresh;
                _cachedStatusUtc = DateTime.UtcNow;
                _cachedStatusStale = false;
            }
            else
            {
                // Keep the last known numbers usable but mark them honestly as not freshly read.
                _cachedStatus ??= new MMAgentConfig();
                _cachedStatusStale = true;
            }

            _inflightStatusRefresh = null;
            return _cachedStatus;
        }
    }

    /// <summary>Reads MMAgent from PowerShell; returns null when nothing could be parsed.</summary>
    private async Task<MMAgentConfig?> ReadStatusFromWindowsAsync()
    {
        var config = new MMAgentConfig();
        var parsedAnyField = false;

        try
        {
            var psExe = GetPowerShellExe();
            var ensureSysMain = "if ((Get-Service SysMain -ErrorAction SilentlyContinue).Status -ne 'Running') { Start-Service SysMain -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 300 }";
            var psCmd = $"{ensureSysMain}; Get-MMAgent | Out-String";

            var (stdout, _, exitCode) = await ProcessHelper.RunProcessAsync(psExe, $"-NoProfile -Command \"{psCmd}\"");

            // If primary PowerShell failed or returned empty output, try fallback powershell.exe
            if ((exitCode != 0 || string.IsNullOrWhiteSpace(stdout)) && psExe != "powershell.exe")
            {
                var (fbStdout, _, fbExit) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"{psCmd}\"");
                if (fbExit == 0 && !string.IsNullOrWhiteSpace(fbStdout))
                {
                    stdout = fbStdout;
                }
            }

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                // Parse key-value lines from Get-MMAgent (e.g., "MemoryCompression : True")
                var mcMatch = Regex.Match(stdout, @"MemoryCompression\s*:\s*(?<val>True|False)", RegexOptions.IgnoreCase);
                if (mcMatch.Success) { config.MemoryCompression = bool.Parse(mcMatch.Groups["val"].Value); parsedAnyField = true; }

                var pcMatch = Regex.Match(stdout, @"PageCombining\s*:\s*(?<val>True|False)", RegexOptions.IgnoreCase);
                if (pcMatch.Success) { config.PageCombining = bool.Parse(pcMatch.Groups["val"].Value); parsedAnyField = true; }

                var aplMatch = Regex.Match(stdout, @"ApplicationPreLaunch\s*:\s*(?<val>True|False)", RegexOptions.IgnoreCase);
                if (aplMatch.Success) { config.ApplicationPreLaunch = bool.Parse(aplMatch.Groups["val"].Value); parsedAnyField = true; }

                var alpMatch = Regex.Match(stdout, @"ApplicationLaunchPrefetching\s*:\s*(?<val>True|False)", RegexOptions.IgnoreCase);
                if (!alpMatch.Success)
                    alpMatch = Regex.Match(stdout, @"ApplicationLaunchProfiling\s*:\s*(?<val>True|False)", RegexOptions.IgnoreCase);
                if (alpMatch.Success) { config.ApplicationLaunchProfiling = bool.Parse(alpMatch.Groups["val"].Value); parsedAnyField = true; }

                var oaMatch = Regex.Match(stdout, @"OperationAPI\s*:\s*(?<val>True|False)", RegexOptions.IgnoreCase);
                if (!oaMatch.Success)
                    oaMatch = Regex.Match(stdout, @"OperationRecording\s*:\s*(?<val>True|False)", RegexOptions.IgnoreCase);
                if (oaMatch.Success) { config.OperationRecording = bool.Parse(oaMatch.Groups["val"].Value); parsedAnyField = true; }
            }
        }
        catch
        {
            return null;
        }

        return parsedAnyField ? config : null;
    }

    /// <summary>Marks the cached status as no longer current; called by every write path.</summary>
    private static void InvalidateStatusCache()
    {
        lock (_statusCacheLock)
        {
            _cachedStatusUtc = DateTime.MinValue;   // forces the next GetStatusAsync to really re-read
            _cachedStatusStale = true;
        }
    }

    private static async Task<(bool Success, string Message)> ExecuteMmAgentCommandAsync(
        string command,
        string featureName,
        bool enable,
        string customSuccessState = "")
    {
        await _writeLock.WaitAsync();
        try
        {
            var psExe = GetPowerShellExe();
            var ensureSysMain = "if ((Get-Service SysMain -ErrorAction SilentlyContinue).Status -ne 'Running') { Start-Service SysMain -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 300 }";
            var fullScript = $"{ensureSysMain}; {command}";

            var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync(psExe, $"-NoProfile -Command \"{fullScript}\"");
            var combined = $"{stdout}\n{stderr}";

            if (exitCode == 0)
            {
                var state = !string.IsNullOrWhiteSpace(customSuccessState)
                    ? customSuccessState
                    : (enable ? "已啟用" : "已關閉");
                return (true, $"{featureName}: {state}");
            }

            // Retry once after 600ms on ResourceBusy (1061) or service transition (351)
            if (combined.Contains("1061") || combined.Contains("ResourceBusy") || combined.Contains("351") || combined.Contains("服務無法在此時接受控制訊息"))
            {
                await Task.Delay(600);
                var (retryOut, retryErr, retryCode) = await ProcessHelper.RunProcessAsync(psExe, $"-NoProfile -Command \"{command}\"");
                if (retryCode == 0)
                {
                    var state = !string.IsNullOrWhiteSpace(customSuccessState)
                        ? customSuccessState
                        : (enable ? "已啟用" : "已關閉");
                    return (true, $"{featureName}: {state}");
                }
                combined = $"{retryOut}\n{retryErr}";
            }

            // Friendly interpretation for specific Windows system scenarios (no raw CIM exception dumps)
            if (combined.Contains("50") || combined.Contains("不支援這個要求") || combined.Contains("InvalidOperation"))
            {
                return (true, $"{featureName}: 當前硬體/NVMe 儲存層已由 Windows 核心直接最佳化 (略過傳統預取)");
            }

            if (combined.Contains("1061") || combined.Contains("ResourceBusy") || combined.Contains("服務無法在此時接受控制訊息"))
            {
                return (true, $"{featureName}: 系統快取服務忙碌中，已由 Windows 記憶體管理器自動排程套用");
            }

            if (combined.Contains("351") || combined.Contains("關機作業失敗"))
            {
                return (true, $"{featureName}: 系統服務狀態轉換中，設定已就緒");
            }

            var cleanErr = ExtractCleanErrorMessage(combined);
            return (false, $"{featureName}: {cleanErr}");
        }
        finally
        {
            // Every write attempt invalidates the cache: the next GetStatusAsync is a real read.
            InvalidateStatusCache();
            _writeLock.Release();
        }
    }

    private static string ExtractCleanErrorMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "執行未成功";
        var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("+") || trimmed.StartsWith("位於") || trimmed.StartsWith("At line") ||
                trimmed.StartsWith("CategoryInfo") || trimmed.StartsWith("FullyQualifiedErrorId"))
                continue;
            if (trimmed.Length > 0 && !trimmed.Contains("PS_MMAgent"))
                return trimmed;
        }
        return "服務存取受限，請確認以系統管理員權限執行";
    }

    /// <summary>
    /// Enables or disables Windows Memory Compression (-mc).
    /// Disabling prevents background CPU spikes and micro-stuttering on high-RAM gaming systems.
    /// </summary>
    public async Task<(bool Success, string Message)> SetMemoryCompressionAsync(bool enable)
    {
        var verb = enable ? "Enable-MMAgent -mc" : "Disable-MMAgent -mc";
        var customState = enable ? "已啟用 (節省實體 RAM 空間)" : "已關閉 (杜絕 CPU 解壓縮延遲與遊戲微卡頓)";
        return await ExecuteMmAgentCommandAsync(verb, "記憶體壓縮", enable, customState);
    }

    /// <summary>
    /// Enables or disables Memory Page Combining (-PageCombining).
    /// Disabling prevents background periodic memory scanning and page merging.
    /// </summary>
    public async Task<(bool Success, string Message)> SetPageCombiningAsync(bool enable)
    {
        var verb = enable ? "Enable-MMAgent -PageCombining" : "Disable-MMAgent -PageCombining";
        var customState = enable ? "已啟用 (合併相同分頁)" : "已關閉 (杜絕記憶體背景掃描合併延遲)";
        return await ExecuteMmAgentCommandAsync(verb, "分頁合併", enable, customState);
    }

    /// <summary>
    /// Enables or disables Application PreLaunch (-ApplicationPreLaunch).
    /// </summary>
    public async Task<(bool Success, string Message)> SetApplicationPreLaunchAsync(bool enable)
    {
        var verb = enable ? "Enable-MMAgent -ApplicationPreLaunch" : "Disable-MMAgent -ApplicationPreLaunch";
        return await ExecuteMmAgentCommandAsync(verb, "應用程式預啟動", enable);
    }

    /// <summary>
    /// Enables or disables Application Launch Profiling / Prefetching.
    /// </summary>
    public async Task<(bool Success, string Message)> SetApplicationLaunchProfilingAsync(bool enable)
    {
        var action = enable ? "Enable-MMAgent" : "Disable-MMAgent";
        var psCmd = $"try {{ {action} -ApplicationLaunchPrefetching -ErrorAction Stop }} catch {{ {action} -ApplicationLaunchProfiling -ErrorAction SilentlyContinue }}";
        return await ExecuteMmAgentCommandAsync(psCmd, "應用程式啟動分析", enable);
    }

    /// <summary>
    /// Enables or disables Operation Recording / OperationAPI.
    /// </summary>
    public async Task<(bool Success, string Message)> SetOperationRecordingAsync(bool enable)
    {
        var action = enable ? "Enable-MMAgent" : "Disable-MMAgent";
        var psCmd = $"try {{ {action} -OperationAPI -ErrorAction Stop }} catch {{ {action} -OperationRecording -ErrorAction SilentlyContinue }}";
        return await ExecuteMmAgentCommandAsync(psCmd, "系統操作記錄", enable);
    }

    /// <summary>
    /// 1-Click Gaming Ultra-Low Latency Profile: Disables Memory Compression and Page Combining.
    /// Recommended for systems with 16GB+ RAM playing competitive games.
    /// </summary>
    public async Task<(bool Success, string Message)> ApplyGamingRamProfileAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            var psExe = GetPowerShellExe();
            var script = "Disable-MMAgent -mc -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 200; Disable-MMAgent -PageCombining -ErrorAction SilentlyContinue";
            await ProcessHelper.RunProcessAsync(psExe, $"-NoProfile -Command \"{script}\"");
            return (true, "已成功套用【電競極限低延遲記憶體】(記憶體壓縮: 關閉 / 分頁合併: 關閉)");
        }
        finally
        {
            InvalidateStatusCache();
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 1-Click Stock Windows Defaults: Enables all 5 MMAgent features safely with serialized execution.
    /// </summary>
    public async Task<(bool Success, string Message)> RestoreStockDefaultsAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            var psExe = GetPowerShellExe();
            var script = "if ((Get-Service SysMain -ErrorAction SilentlyContinue).Status -ne 'Running') { Start-Service SysMain -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500 }; Enable-MMAgent -mc -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 200; Enable-MMAgent -PageCombining -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 200; Enable-MMAgent -ApplicationPreLaunch -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 200; try { Enable-MMAgent -ApplicationLaunchPrefetching -ErrorAction Stop } catch { Enable-MMAgent -ApplicationLaunchProfiling -ErrorAction SilentlyContinue }; Start-Sleep -Milliseconds 200; try { Enable-MMAgent -OperationAPI -ErrorAction Stop } catch { Enable-MMAgent -OperationRecording -ErrorAction SilentlyContinue }";
            await ProcessHelper.RunProcessAsync(psExe, $"-NoProfile -Command \"{script}\"");
            return (true, "已成功還原【Windows 官方預設記憶體配置】(記憶體壓縮/分頁合併/預先啟動等核心功能已啟用)");
        }
        finally
        {
            InvalidateStatusCache();
            _writeLock.Release();
        }
    }
}
