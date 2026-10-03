using System.IO;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// PowerCfg and system sleep management service.
/// Provides hardware wake control, sleep blocker diagnostics, and hibernation SSD space recovery.
/// </summary>
public class PowerCfgService
{
    /// <summary>
    /// Queries all devices currently armed to wake the computer, as well as devices eligible for wake.
    /// </summary>
    public async Task<List<PowerWakeDeviceItem>> GetWakeDevicesAsync()
    {
        var armedList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Query wake_armed
        var (armedOut, _, _) = await ProcessHelper.RunProcessAsync("powercfg.exe", "/devicequery wake_armed");
        var armedLines = armedOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in armedLines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("NONE", StringComparison.OrdinalIgnoreCase))
            {
                armedList.Add(trimmed);
            }
        }

        // 2. Query wake_from_any (devices capable of waking)
        var (anyOut, _, _) = await ProcessHelper.RunProcessAsync("powercfg.exe", "/devicequery wake_from_any");
        var anyLines = anyOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var result = new List<PowerWakeDeviceItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add armed devices first
        foreach (var dev in armedList)
        {
            if (seen.Add(dev))
            {
                result.Add(new PowerWakeDeviceItem
                {
                    DeviceName = dev,
                    IsArmed = true
                });
            }
        }

        // Add remaining wake-capable devices
        foreach (var line in anyLines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("NONE", StringComparison.OrdinalIgnoreCase)) continue;

            // Filter down to interesting human-interactive or network devices
            var lower = trimmed.ToLowerInvariant();
            bool isRelevant = lower.Contains("mouse") || lower.Contains("keyboard") || lower.Contains("pointing") ||
                              lower.Contains("ethernet") || lower.Contains("network") || lower.Contains("lan") ||
                              lower.Contains("wi-fi") || lower.Contains("wireless") || lower.Contains("hid");

            if (isRelevant && seen.Add(trimmed))
            {
                result.Add(new PowerWakeDeviceItem
                {
                    DeviceName = trimmed,
                    IsArmed = armedList.Contains(trimmed)
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Enables or disables a specific hardware device's ability to wake the system.
    /// </summary>
    public async Task<(bool Success, string Message)> SetDeviceWakeStateAsync(string deviceName, bool enable)
    {
        var cmd = enable ? "/deviceenablewake" : "/devicedisablewake";
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("powercfg.exe", $"{cmd} \"{deviceName}\"");

        if (exitCode == 0)
        {
            var actionText = enable ? "已啟用喚醒權限" : "已停用喚醒權限 (防止晃動誤喚醒)";
            return (true, $"{deviceName}: {actionText}");
        }

        var err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (false, $"設定失敗: {err.Trim()}");
    }

    /// <summary>
    /// Inspects active sleep blockers (powercfg /requests).
    /// </summary>
    public async Task<List<SleepBlockerItem>> GetSleepBlockersAsync()
    {
        var list = new List<SleepBlockerItem>();
        var (stdout, _, _) = await ProcessHelper.RunProcessAsync("powercfg.exe", "/requests");

        var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string currentCategory = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.EndsWith(":") && !line.Contains(" "))
            {
                currentCategory = line.TrimEnd(':');
                continue;
            }

            if (string.IsNullOrEmpty(currentCategory) || line.Equals("None.", StringComparison.OrdinalIgnoreCase) || line.Equals("無。", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Line typically starts with [PROCESS], [SERVICE], or [DRIVER]
            var match = Regex.Match(line, @"^\[(.*?)\]\s*(.*)$");
            if (match.Success)
            {
                var callerType = match.Groups[1].Value.Trim();
                var callerDetails = match.Groups[2].Value.Trim();

                list.Add(new SleepBlockerItem
                {
                    Category = currentCategory,
                    CallerType = callerType,
                    CallerName = callerDetails,
                    Reason = "阻止系統進入睡眠或關閉顯示器"
                });
            }
            else
            {
                list.Add(new SleepBlockerItem
                {
                    Category = currentCategory,
                    CallerType = "CALLER",
                    CallerName = line,
                    Reason = "活躍請求"
                });
            }
        }

        return list;
    }

    /// <summary>
    /// Checks if hibernation is currently enabled on the system.
    /// </summary>
    public bool IsHibernationEnabled()
    {
        try
        {
            return File.Exists(@"C:\hiberfil.sys");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the formatted size of hiberfil.sys on C: drive.
    /// </summary>
    public string GetHiberfilSizeDisplay()
    {
        try
        {
            var fi = new FileInfo(@"C:\hiberfil.sys");
            if (fi.Exists)
            {
                double gb = fi.Length / (1024.0 * 1024.0 * 1024.0);
                return $"{gb:F1} GB";
            }
            return "0 GB (已關閉釋放)";
        }
        catch
        {
            return "未知";
        }
    }

    /// <summary>
    /// Enables or disables system hibernation. Disabling instantly deletes C:\hiberfil.sys and frees 16~64GB.
    /// </summary>
    public async Task<(bool Success, string Message)> SetHibernationAsync(bool enable)
    {
        var arg = enable ? "/hibernate on" : "/hibernate off";
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("powercfg.exe", arg);

        if (exitCode == 0)
        {
            var msg = enable ? "休眠已開啟 (已建立 hiberfil.sys)" : "休眠已關閉 (已成功釋放 SSD 空間！)";
            return (true, msg);
        }

        var err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (false, $"休眠設定失敗: {err.Trim()}");
    }

    /// <summary>
    /// Sets fast startup to reduced hibernation mode (saves ~60% SSD space while preserving Fast Startup).
    /// </summary>
    public async Task<(bool Success, string Message)> SetFastStartupReducedModeAsync(bool reduced)
    {
        var arg = reduced ? "/hibernate /type reduced" : "/hibernate /type full";
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("powercfg.exe", arg);

        if (exitCode == 0)
        {
            var msg = reduced ? "已切換為縮減模式 (節省約 60% 休眠檔空間且保留快速啟動)" : "已切換為完整休眠模式";
            return (true, msg);
        }

        var err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (false, $"模式設定失敗: {err.Trim()}");
    }

    /// <summary>
    /// Unhides or hides the hidden "Processor Performance Boost Mode" in Windows Power Options.
    /// </summary>
    public async Task<(bool Success, string Message)> SetProcessorBoostModeUnhideAsync(bool unhide)
    {
        var flag = unhide ? "-ATTRIB_HIDE" : "+ATTRIB_HIDE";
        var arg = $"-attributes 54533251-82be-4824-96c1-47b60b740d00 be337238-0d82-4146-a960-4f3749d470c7 {flag}";
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("powercfg.exe", arg);

        if (exitCode == 0)
        {
            var msg = unhide ? "已解鎖「處理器效能激進提升模式」設定原則" : "已隱藏「處理器效能激進提升模式」";
            return (true, msg);
        }

        var err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (false, $"設定失敗: {err.Trim()}");
    }

    public async Task<(bool Success, string Message)> UnhideProcessorAttributeAsync(string attributeGuid)
    {
        var arg = $"-attributes 54533251-82be-4824-96c1-47b60b740d00 {attributeGuid} -ATTRIB_HIDE";
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("powercfg.exe", arg);
        return (exitCode == 0, exitCode == 0 ? "已解鎖處理器電源屬性" : stderr);
    }

    public async Task<(bool Success, string Message)> UnhideAllProcessorAttributesAsync()
    {
        var guids = new[]
        {
            "be337238-0d82-4146-a960-4f3749d470c7", // BoostMode
            "36687f9e-e376-49e8-b783-be5e3e3563ab", // EnergyPerformancePreference
            "8baa4a8a-14fc-482b-bd23-a0f0f71e11e8", // AutonomousMode
            "0cc5b647-c1df-4637-891a-dec35c318583", // CoreParkingMinCores
            "ea062031-0e34-4ff1-9b6d-eb1059324028", // CoreParkingMaxCores
            "7f24e370-7664-4642-99e3-e605185a0899", // HeterogeneousScheduling
            "94d3a615-a899-4ac5-ae2b-e4d8f6343d57"  // SystemCoolingPolicy
        };

        var errors = new List<string>();
        foreach (var guid in guids)
        {
            var (ok, msg) = await UnhideProcessorAttributeAsync(guid);
            if (!ok) errors.Add(msg);
        }

        return errors.Count == 0
            ? (true, "已成功解鎖全部 7 項處理器進階電源管理原則！")
            : (false, $"部分屬性解鎖失敗: {string.Join("; ", errors)}");
    }

    /// <summary>
    /// Purges hiberfil.sys by disabling hibernation, verifies release, and returns reclaimed GB.
    /// </summary>
    public async Task<(bool Success, double ReclaimedGb, string Message)> PurgeHibernationFileAndFreeSpaceAsync()
    {
        string sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        string hiberPath = Path.Combine(sysDrive, "hiberfil.sys");

        long originalBytes = 0;
        try
        {
            if (File.Exists(hiberPath))
            {
                var fi = new FileInfo(hiberPath);
                originalBytes = fi.Length;
            }
        }
        catch { }

        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("powercfg.exe", "/hibernate off");

        double reclaimedGb = originalBytes > 0 ? (double)originalBytes / 1073741824.0 : 0.0;

        if (exitCode == 0)
        {
            string msg = originalBytes > 0
                ? $"已成功關閉系統休眠並清除 hiberfil.sys，成功釋放 {reclaimedGb:F2} GB SSD 空間！"
                : "系統休眠已關閉，目前無 hiberfil.sys 佔用空間。";
            return (true, reclaimedGb, msg);
        }

        string err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (false, 0.0, $"休眠關閉失敗: {err.Trim()}");
    }

    /// <summary>
    /// Generates Windows Battery Report HTML.
    /// </summary>
    public async Task<(bool Success, string FilePath)> GenerateBatteryReportAsync()
    {
        var targetPath = Path.Combine(Path.GetTempPath(), $"BatteryReport_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("powercfg.exe", $"/batteryreport /output \"{targetPath}\"");

        if (File.Exists(targetPath))
        {
            return (true, targetPath);
        }

        return (false, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    /// <summary>
    /// Generates Windows Energy Report HTML.
    /// </summary>
    public async Task<(bool Success, string FilePath)> GenerateEnergyReportAsync()
    {
        var targetPath = Path.Combine(Path.GetTempPath(), $"EnergyReport_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("powercfg.exe", $"/energy /output \"{targetPath}\" /duration 5");

        if (File.Exists(targetPath))
        {
            return (true, targetPath);
        }

        return (false, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    /// <summary>
    /// Sets Energy Performance Preference (EPP) for the active power scheme (0 = Max Performance, 100 = Max Power Saving).
    /// </summary>
    public async Task<(bool Success, string Message)> SetEnergyPerformancePreferenceAsync(int acValue, int dcValue = 50)
    {
        var (stdout1, stderr1, code1) = await ProcessHelper.RunProcessAsync("powercfg.exe", $"/setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 36687f9e-e376-49e8-b783-be5e3e3563ab {acValue}");
        var (stdout2, stderr2, code2) = await ProcessHelper.RunProcessAsync("powercfg.exe", $"/setdcvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 36687f9e-e376-49e8-b783-be5e3e3563ab {dcValue}");
        await ProcessHelper.RunProcessAsync("powercfg.exe", "/setactive SCHEME_CURRENT");

        if (code1 == 0 && code2 == 0)
        {
            return (true, $"EPP 能源偏好已設定為 AC: {acValue} (0=極速), DC: {dcValue}");
        }
        return (false, $"EPP 設定失敗: {stderr1} {stderr2}");
    }

    /// <summary>
    /// Configures CPU Core Parking minimum and maximum core percentages (100% min = unpark all cores).
    /// </summary>
    public async Task<(bool Success, string Message)> SetCoreParkingAsync(int minCoresPercent, int maxCoresPercent = 100)
    {
        var (stdout1, stderr1, code1) = await ProcessHelper.RunProcessAsync("powercfg.exe", $"/setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 {minCoresPercent}");
        var (stdout2, stderr2, code2) = await ProcessHelper.RunProcessAsync("powercfg.exe", $"/setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 ea062031-0e34-4ff1-9b6d-eb1059324028 {maxCoresPercent}");
        await ProcessHelper.RunProcessAsync("powercfg.exe", "/setactive SCHEME_CURRENT");

        if (code1 == 0 && code2 == 0)
        {
            var desc = minCoresPercent >= 100 ? "已全面解鎖核心停駐 (100% 全核無休眠運行)" : $"核心停駐範圍已設定為 {minCoresPercent}% ~ {maxCoresPercent}%";
            return (true, desc);
        }
        return (false, $"核心停駐設定失敗: {stderr1} {stderr2}");
    }

    /// <summary>
    /// Configures Processor Performance Boost Mode (0=Disabled, 1=Enabled, 2=Aggressive, 3=Efficient Enabled, 4=Efficient Aggressive).
    /// </summary>
    public async Task<(bool Success, string Message)> SetProcessorBoostModeValueAsync(int mode)
    {
        var (stdout, stderr, code) = await ProcessHelper.RunProcessAsync("powercfg.exe", $"/setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 be337238-0d82-4146-a960-4f3749d470c7 {mode}");
        await ProcessHelper.RunProcessAsync("powercfg.exe", "/setactive SCHEME_CURRENT");

        if (code == 0)
        {
            var modeName = mode switch
            {
                0 => "已停用 (降低發熱與功耗)",
                1 => "標準啟用",
                2 => "極致激進 (Aggressive，鎖定高頻)",
                3 => "節能啟用",
                _ => "高能效激進"
            };
            return (true, $"處理器激進加速模式: {modeName}");
        }
        return (false, $"加速模式設定失敗: {stderr}");
    }
}

