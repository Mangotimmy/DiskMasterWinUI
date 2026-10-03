using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service for inspecting and optimizing Windows TCP/IP network protocol stack parameters.
/// Supports TCP AutoTuning, BBR / CUBIC congestion providers, ECN, RSS, RSC, and 1-click max throughput profiles.
/// </summary>
public class TcpOptimizerService
{
    /// <summary>
    /// Queries the current Windows TCP global parameters.
    /// </summary>
    public async Task<TcpSettingsItem> GetTcpSettingsAsync()
    {
        var item = new TcpSettingsItem();
        try
        {
            var (output, _, _) = await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp show global");
            item.RawGlobalOutput = output;

            if (!string.IsNullOrWhiteSpace(output))
            {
                var autoTuningMatch = Regex.Match(output, @"Receive Window Auto-Tuning Level\s*:\s*(\w+)", RegexOptions.IgnoreCase);
                if (autoTuningMatch.Success) item.AutoTuningLevel = autoTuningMatch.Groups[1].Value.Trim();

                var congestionMatch = Regex.Match(output, @"Add-On Congestion Control Provider\s*:\s*(\w+)", RegexOptions.IgnoreCase);
                if (congestionMatch.Success) item.CongestionProvider = congestionMatch.Groups[1].Value.Trim();

                var ecnMatch = Regex.Match(output, @"ECN Capability\s*:\s*(\w+)", RegexOptions.IgnoreCase);
                if (ecnMatch.Success) item.EcnCapability = ecnMatch.Groups[1].Value.Trim();

                var rssMatch = Regex.Match(output, @"Receive-Side Scaling State\s*:\s*(\w+)", RegexOptions.IgnoreCase);
                if (rssMatch.Success) item.Rss = rssMatch.Groups[1].Value.Trim();

                var rscMatch = Regex.Match(output, @"Receive Segment Coalescing State\s*:\s*(\w+)", RegexOptions.IgnoreCase);
                if (rscMatch.Success) item.Rsc = rscMatch.Groups[1].Value.Trim();

                var timestampsMatch = Regex.Match(output, @"Timestamps\s*:\s*(\w+)", RegexOptions.IgnoreCase);
                if (timestampsMatch.Success) item.Timestamps = timestampsMatch.Groups[1].Value.Trim();

                var rtoMatch = Regex.Match(output, @"Initial RTO\s*:\s*(\d+)", RegexOptions.IgnoreCase);
                if (rtoMatch.Success) item.InitialRto = rtoMatch.Groups[1].Value.Trim();
            }

            // Also check NetTCPSetting for precise CongestionProvider (BBR / CUBIC / CTCP)
            try
            {
                var (psOut, _, psCode) = await ProcessHelper.RunProcessAsync(
                    "powershell.exe",
                    "-NoProfile -Command \"(Get-NetTCPSetting -SettingName Internet -ErrorAction SilentlyContinue).CongestionProvider\"");
                if (psCode == 0 && !string.IsNullOrWhiteSpace(psOut))
                {
                    item.CongestionProvider = psOut.Trim();
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            item.RawGlobalOutput = $"Error querying TCP settings: {ex.Message}";
        }
        return item;
    }

    /// <summary>
    /// Configures the TCP Receive Window Auto-Tuning Level.
    /// (normal, disabled, highlyrestricted, restricted, experimental)
    /// </summary>
    public async Task<(bool Success, string Message)> SetAutoTuningLevelAsync(string level)
    {
        var lvl = level.Trim().ToLowerInvariant();
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("netsh.exe", $"int tcp set global autotuninglevel={lvl}");
        if (exitCode == 0)
        {
            return (true, $"已成功將 TCP 視窗自動微調等級設定為: {level}");
        }
        return (false, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    /// <summary>
    /// Configures the TCP Congestion Control Provider (bbr, cubic, ctcp, newreno, default).
    /// </summary>
    public async Task<(bool Success, string Message)> SetCongestionProviderAsync(string provider)
    {
        var prov = provider.Trim();
        // Method 1: Netsh supplemental template
        var (stdout1, stderr1, exitCode1) = await ProcessHelper.RunProcessAsync(
            "netsh.exe", $"int tcp set supplemental template=internet congestionprovider={prov.ToLowerInvariant()}");

        // Method 2: PowerShell Set-NetTCPSetting
        var psCmd = $"Set-NetTCPSetting -SettingName Internet -CongestionProvider {prov} -ErrorAction SilentlyContinue; Set-NetTCPSetting -SettingName InternetCustom -CongestionProvider {prov} -ErrorAction SilentlyContinue";
        var (_, _, exitCode2) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"{psCmd}\"");

        if (exitCode1 == 0 || exitCode2 == 0)
        {
            return (true, $"已成功將 TCP 擁塞控制演算法切換為: {prov}");
        }
        return (false, string.IsNullOrWhiteSpace(stderr1) ? stdout1 : stderr1);
    }

    /// <summary>
    /// Enables or disables Explicit Congestion Notification (ECN).
    /// </summary>
    public async Task<(bool Success, string Message)> SetEcnAsync(bool enable)
    {
        var state = enable ? "enabled" : "disabled";
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("netsh.exe", $"int tcp set global ecncapability={state}");
        if (exitCode == 0)
        {
            return (true, $"ECN 顯式擁塞通知: {(enable ? "已啟用" : "已停用")}");
        }
        return (false, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    /// <summary>
    /// Enables or disables Receive-Side Scaling (RSS) for multi-core packet distribution.
    /// </summary>
    public async Task<(bool Success, string Message)> SetRssAsync(bool enable)
    {
        var state = enable ? "enabled" : "disabled";
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("netsh.exe", $"int tcp set global rss={state}");
        if (exitCode == 0)
        {
            return (true, $"RSS 網卡多核心佇列分流: {(enable ? "已啟用" : "已停用")}");
        }
        return (false, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    /// <summary>
    /// Enables or disables Receive Segment Coalescing (RSC) hardware offloading.
    /// </summary>
    public async Task<(bool Success, string Message)> SetRscAsync(bool enable)
    {
        var state = enable ? "enabled" : "disabled";
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("netsh.exe", $"int tcp set global rsc={state}");
        if (exitCode == 0)
        {
            return (true, $"RSC 封包硬體分段合併: {(enable ? "已啟用" : "已停用")}");
        }
        return (false, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    /// <summary>
    /// Enables or disables RFC 1323 Timestamps.
    /// </summary>
    public async Task<(bool Success, string Message)> SetTimestampsAsync(bool enable)
    {
        var state = enable ? "allowed" : "disabled";
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("netsh.exe", $"int tcp set global timestamps={state}");
        if (exitCode == 0)
        {
            return (true, $"TCP 時間戳 (Timestamps): {(enable ? "已允許" : "已關閉 (降低封包負擔)")}");
        }
        return (false, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    /// <summary>
    /// 1-Click Extreme Throughput & Low-Latency Gaming Preset:
    /// Applies AutoTuning=Normal (or Experimental for fiber), Congestion=BBR/CUBIC, ECN=enabled, RSS=enabled, RSC=enabled, Timestamps=disabled.
    /// </summary>
    public async Task<(bool Success, string Message)> ApplyExtremeThroughputPresetAsync(bool experimentalWindow = false)
    {
        try
        {
            var tuning = experimentalWindow ? "experimental" : "normal";
            await ProcessHelper.RunProcessAsync("netsh.exe", $"int tcp set global autotuninglevel={tuning}");
            await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp set global ecncapability=enabled");
            await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp set global rss=enabled");
            await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp set global rsc=enabled");
            await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp set global timestamps=disabled");
            await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp set global fastopen=enabled");
            await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp set global hystart=enabled");
            await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp set global prr=enabled");

            // Try BBR first, if not supported fallback to CUBIC
            var (bbrOut, _, bbrCode) = await ProcessHelper.RunProcessAsync("powershell.exe", "-NoProfile -Command \"Set-NetTCPSetting -SettingName Internet -CongestionProvider BBR -ErrorAction SilentlyContinue\"");
            if (bbrCode != 0)
            {
                await ProcessHelper.RunProcessAsync("powershell.exe", "-NoProfile -Command \"Set-NetTCPSetting -SettingName Internet -CongestionProvider CUBIC -ErrorAction SilentlyContinue\"");
            }

            return (true, "已成功套用【⚡ 極速大頻寬與電競低延遲 TCP 配置】！(AutoTuning/ECN/RSS/RSC/FastOpen 最佳化完成)");
        }
        catch (Exception ex)
        {
            return (false, $"套用失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores Windows default TCP/IP stack configuration.
    /// </summary>
    public async Task<(bool Success, string Message)> RestoreWindowsDefaultsAsync()
    {
        try
        {
            await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp set global autotuninglevel=normal");
            await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp set global ecncapability=disabled");
            await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp set global rss=enabled");
            await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp set global rsc=enabled");
            await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp set global timestamps=allowed");
            await ProcessHelper.RunProcessAsync("powershell.exe", "-NoProfile -Command \"Set-NetTCPSetting -SettingName Internet -CongestionProvider CUBIC -ErrorAction SilentlyContinue\"");

            return (true, "已成功還原【Windows 官方預設 TCP 網路配置】。");
        }
        catch (Exception ex)
        {
            return (false, $"還原失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// Flushes the Windows DNS resolver cache.
    /// </summary>
    public async Task<string> FlushDnsAsync()
    {
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("ipconfig.exe", "/flushdns");
        return exitCode == 0 ? "已成功清除 Windows DNS 解析快取！" : stderr;
    }
}
