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
        var arg = $"-attributes SUB_PROCESSOR 54533251-82be-4824-96c1-47b60b740d00 {flag}";
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("powercfg.exe", arg);

        if (exitCode == 0)
        {
            var msg = unhide ? "已解鎖「處理器效能激進提升模式」設定原則" : "已隱藏「處理器效能激進提升模式」";
            return (true, msg);
        }

        var err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (false, $"設定失敗: {err.Trim()}");
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
}
