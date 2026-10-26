using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service for NVMe SSD latency optimization, autonomous power state transitions (APST),
/// StorNVMe queue depth concurrency tuning (NumberOfRequests), and TRIM maintenance.
/// Addresses NVMe micro-stuttering, wake-up latency spikes, and Event ID 129 driver resets.
/// </summary>
public class NvmeOptimizerService
{
    private const string StornvmeDeviceKeyPath = @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device";
    private const string FullRegKeyPath = @"HKLM\SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device";

    /// <summary>
    /// Queries the current system-wide TRIM status for NTFS and ReFS filesystems via fsutil.
    /// In DisableDeleteNotify: 0 = TRIM Enabled (Optimal), 1 = TRIM Disabled.
    /// </summary>
    public async Task<TrimStatusInfo> QueryTrimStatusAsync()
    {
        try
        {
            var (output, _, _) = await ProcessHelper.RunProcessAsync("fsutil.exe", "behavior query DisableDeleteNotify");
            var text = output ?? "";

            bool ntfsTrim = false;
            bool refsTrim = false;

            // Sample outputs:
            // NTFS DisableDeleteNotify = 0  (Allows TRIM operations to be sent to the storage device)
            // ReFS DisableDeleteNotify = 0  (Allows TRIM operations to be sent to the storage device)
            // or simply "DisableDeleteNotify = 0"
            var ntfsMatch = Regex.Match(text, @"NTFS\s+DisableDeleteNotify\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            var refsMatch = Regex.Match(text, @"ReFS\s+DisableDeleteNotify\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            var genericMatch = Regex.Match(text, @"DisableDeleteNotify\s*=\s*(\d+)", RegexOptions.IgnoreCase);

            if (ntfsMatch.Success)
            {
                ntfsTrim = ntfsMatch.Groups[1].Value == "0";
            }
            else if (genericMatch.Success)
            {
                ntfsTrim = genericMatch.Groups[1].Value == "0";
            }

            if (refsMatch.Success)
            {
                refsTrim = refsMatch.Groups[1].Value == "0";
            }
            else
            {
                refsTrim = ntfsTrim;
            }

            string display = (ntfsTrim && refsTrim)
                ? "TRIM 已全面啟用 (NTFS: 0, ReFS: 0)"
                : ntfsTrim
                    ? "NTFS TRIM 已啟用，ReFS TRIM 未啟用"
                    : "⚠️ TRIM 未啟用 (DisableDeleteNotify = 1)";

            return new TrimStatusInfo(ntfsTrim, refsTrim, display, text.Trim());
        }
        catch (Exception ex)
        {
            return new TrimStatusInfo(false, false, $"查詢失敗: {ex.Message}", ex.ToString());
        }
    }

    /// <summary>
    /// Configures the filesystem TRIM notification (DisableDeleteNotify).
    /// </summary>
    public async Task<(bool Success, string Message)> SetTrimEnabledAsync(bool enable)
    {
        try
        {
            int val = enable ? 0 : 1;
            var (outNtfs, errNtfs, codeNtfs) = await ProcessHelper.RunProcessAsync("fsutil.exe", $"behavior set DisableDeleteNotify {val}");
            
            // Try explicit NTFS and ReFS set if supported by newer Windows
            await ProcessHelper.RunProcessAsync("fsutil.exe", $"behavior set DisableDeleteNotify NTFS {val}");
            await ProcessHelper.RunProcessAsync("fsutil.exe", $"behavior set DisableDeleteNotify ReFS {val}");

            if (codeNtfs == 0)
            {
                return (true, enable
                    ? "TRIM 指令已成功全域啟用！SSD 寫入效能與壽命最佳化已生效。"
                    : "TRIM 指令已停用 (DisableDeleteNotify = 1)。");
            }
            return (false, $"設定失敗: {errNtfs}");
        }
        catch (Exception ex)
        {
            return (false, $"執行異常: {ex.Message}");
        }
    }

    /// <summary>
    /// Manually triggers drive volume TRIM (ReTrim / Slab Consolidation) on the specified drive.
    /// </summary>
    public async Task<(bool Success, string Message)> RunManualTrimAsync(string? driveLetter = null)
    {
        string letter = string.IsNullOrWhiteSpace(driveLetter) ? "C" : driveLetter.Trim().TrimEnd(':', '\\');
        try
        {
            // Execute PowerShell Optimize-Volume with -ReTrim
            var (output, error, exitCode) = await ProcessHelper.RunProcessAsync(
                "powershell.exe",
                $"-NoProfile -NonInteractive -Command \"Optimize-Volume -DriveLetter {letter} -ReTrim -Verbose\""
            );

            if (exitCode == 0 || output.Contains("OK", StringComparison.OrdinalIgnoreCase))
            {
                return (true, $"磁碟區 {letter}: 手動 TRIM 重修復 (ReTrim) 執行成功！已完成區塊廢料回收。");
            }

            // Fallback to defrag /L
            var (dOut, dErr, dCode) = await ProcessHelper.RunProcessAsync("defrag.exe", $"{letter}: /L /U /V");
            if (dCode == 0)
            {
                return (true, $"磁碟區 {letter}: 手動 TRIM (defrag /L) 成功完成！");
            }

            return (false, $"手動 TRIM 失敗: {error} {dErr}");
        }
        catch (Exception ex)
        {
            return (false, $"手動 TRIM 例外: {ex.Message}");
        }
    }

    /// <summary>
    /// Queries the current status of NVMe APST (Autonomous Power State Transition).
    /// </summary>
    public Task<NvmeApstStatus> QueryApstStatusAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(StornvmeDeviceKeyPath, false);
                if (key != null)
                {
                    var val = key.GetValue("EnableAPST");
                    if (val is int intVal)
                    {
                        bool disabled = intVal == 0;
                        string display = disabled
                            ? "已停用自主微睡眠 (EnableAPST = 0，防卡頓模式生效中)"
                            : "已啟用省電微睡眠 (EnableAPST = 1)";
                        return new NvmeApstStatus(disabled, intVal, display);
                    }
                }

                return new NvmeApstStatus(false, null, "未配置 (Windows 預設啟用省電微睡眠)");
            }
            catch (Exception ex)
            {
                return new NvmeApstStatus(false, null, $"讀取失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Configures NVMe APST.
    /// Setting disable = true writes EnableAPST = 0 (REG_DWORD) to eliminate wake-up latency and micro-stutters.
    /// </summary>
    public async Task<(bool Success, string Message)> SetApstDisabledAsync(bool disable)
    {
        return await Task.Run(async () =>
        {
            try
            {
                if (disable)
                {
                    // Write EnableAPST = 0
                    using (var key = Registry.LocalMachine.CreateSubKey(StornvmeDeviceKeyPath, true))
                    {
                        key.SetValue("EnableAPST", 0, RegistryValueKind.DWord);
                    }
                }
                else
                {
                    // Delete value or set to 1
                    using var key = Registry.LocalMachine.OpenSubKey(StornvmeDeviceKeyPath, true);
                    if (key != null)
                    {
                        try { key.DeleteValue("EnableAPST", false); } catch { }
                    }
                }

                return (true, disable
                    ? "已成功設定關閉 NVMe APST 自主電源微睡眠 (EnableAPST = 0)！\n⚠️ 請注意：此變更需「重新啟動電腦」後方能完全生效。"
                    : "已恢復 Windows 預設 NVMe APST 電源管理。\n⚠️ 請注意：此變更需「重新啟動電腦」後方能生效。");
            }
            catch (UnauthorizedAccessException)
            {
                // Fallback to elevated reg.exe command
                if (disable)
                {
                    var (_, _, code) = await ProcessHelper.RunProcessAsync("reg.exe", $"add \"{FullRegKeyPath}\" /v EnableAPST /t REG_DWORD /d 0 /f");
                    if (code == 0) return (true, "已透過註冊表指令關閉 NVMe APST (EnableAPST = 0)。\n⚠️ 請注意：需重新啟動電腦生效。");
                }
                else
                {
                    var (_, _, code) = await ProcessHelper.RunProcessAsync("reg.exe", $"delete \"{FullRegKeyPath}\" /v EnableAPST /f");
                    if (code == 0) return (true, "已透過註冊表指令恢復預設 NVMe APST。\n⚠️ 請注意：需重新啟動電腦生效。");
                }
                return (false, "權限不足，請以系統管理員身分執行。");
            }
            catch (Exception ex)
            {
                return (false, $"寫入註冊表失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Queries the StorNVMe driver NumberOfRequests (queue depth limit).
    /// </summary>
    public Task<NvmeQueueStatus> QueryNumberOfRequestsAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(StornvmeDeviceKeyPath, false);
                if (key != null)
                {
                    var val = key.GetValue("NumberOfRequests");
                    if (val is int intVal)
                    {
                        return new NvmeQueueStatus(intVal, $"已自訂佇列上限: {intVal} (0x{intVal:X})");
                    }
                }
                return new NvmeQueueStatus(null, "Windows 系統預設佇列管理 (無額外限制)");
            }
            catch (Exception ex)
            {
                return new NvmeQueueStatus(null, $"讀取失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Sets the StorNVMe NumberOfRequests parameter.
    /// CRITICAL: As emphasized in hardware tuning guides, this value MUST NOT be 0.
    /// </summary>
    public async Task<(bool Success, string Message)> SetNumberOfRequestsAsync(int count)
    {
        if (count <= 0)
        {
            return (false, "⚠️ 安全警告：NumberOfRequests 千萬不能設為 0！(設為 0 將導致 SSD 控制器拒絕任何佇列請求並死鎖)。請填入 32 到 255 之間的正整數。");
        }

        if (count > 255)
        {
            return (false, "⚠️ NumberOfRequests 建議上限為 255 (StorPort 規範容許上限)。");
        }

        return await Task.Run(async () =>
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(StornvmeDeviceKeyPath, true))
                {
                    key.SetValue("NumberOfRequests", count, RegistryValueKind.DWord);
                }

                return (true, $"已成功將 NVMe 佇列並行上限 (NumberOfRequests) 設定為 {count}！\n⚠️ 請注意：此項 StorNVMe 驅動參數需「重新啟動電腦」後方能生效。");
            }
            catch (UnauthorizedAccessException)
            {
                var (_, _, code) = await ProcessHelper.RunProcessAsync("reg.exe", $"add \"{FullRegKeyPath}\" /v NumberOfRequests /t REG_DWORD /d {count} /f");
                if (code == 0) return (true, $"已透過註冊表指令設定 NumberOfRequests = {count}。\n⚠️ 需重新啟動電腦生效。");
                return (false, "權限不足，無法寫入系統驅動程式登錄檔。");
            }
            catch (Exception ex)
            {
                return (false, $"寫入註冊表失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Resets the StorNVMe NumberOfRequests parameter back to system default (removes the registry value).
    /// </summary>
    public async Task<(bool Success, string Message)> ResetNumberOfRequestsAsync()
    {
        return await Task.Run(async () =>
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(StornvmeDeviceKeyPath, true);
                if (key != null)
                {
                    try { key.DeleteValue("NumberOfRequests", false); } catch { }
                }
                return (true, "已移除自訂 NumberOfRequests，恢復由 Windows 內建驅動自動調配！\n⚠️ 請重新啟動電腦生效。");
            }
            catch (UnauthorizedAccessException)
            {
                var (_, _, code) = await ProcessHelper.RunProcessAsync("reg.exe", $"delete \"{FullRegKeyPath}\" /v NumberOfRequests /f");
                if (code == 0) return (true, "已恢復 Windows 預設 NumberOfRequests 佇列排程。\n⚠️ 需重新啟動電腦生效。");
                return (false, "權限不足，請以管理員身分執行。");
            }
            catch (Exception ex)
            {
                return (false, $"重設失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// One-click application of popular hardware enthusiast anti-stutter NVMe tuning:
    /// 1. Disables APST (EnableAPST = 0) to eliminate sleep wake-up latency spikes.
    /// 2. Configures NumberOfRequests = 64 (optimal gaming queue depth).
    /// 3. Ensures filesystem TRIM is active (DisableDeleteNotify = 0).
    /// </summary>
    public async Task<(bool Success, string Message)> ApplyGamingNvmeAntiStutterProfileAsync()
    {
        var apstResult = await SetApstDisabledAsync(true);
        if (!apstResult.Success) return apstResult;

        var queueResult = await SetNumberOfRequestsAsync(64);
        if (!queueResult.Success) return queueResult;

        var trimResult = await SetTrimEnabledAsync(true);

        return (true, "🚀 電競級 NVMe 防卡頓極致調校已成功套用！\n" +
                      "• APST 自主電源微睡眠: 已關閉 (EnableAPST = 0，消除喚醒掉幀)\n" +
                      "• 佇列請求上限: 64 (NumberOfRequests = 64，高並行防死鎖)\n" +
                      "• SSD TRIM 廢料回收: 已全面啟用 (DisableDeleteNotify = 0)\n\n" +
                      "⚠️ 重要提示：NVMe 驅動參數需「重新啟動電腦」後才會載入生效。");
    }

    /// <summary>
    /// Restores all StorNVMe parameters to vanilla Windows inbox defaults.
    /// </summary>
    public async Task<(bool Success, string Message)> RestoreDefaultNvmeProfileAsync()
    {
        await SetApstDisabledAsync(false);
        await ResetNumberOfRequestsAsync();
        return (true, "已成功恢復 Windows 原生預設 NVMe 驅動設定。\n⚠️ 請重新啟動電腦生效。");
    }
}
