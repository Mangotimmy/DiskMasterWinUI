using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service providing comprehensive Windows Update error fixing (including 0x800F0831 CBS_E_STORE_CORRUPTION),
/// configurable DISM/SFC + service/cache repair pipelines, CBS.log &amp; Windows Update history parsing,
/// Microsoft Update Catalog integration, local .msu/.cab package installation, and Troubleshooter launch.
/// </summary>
public class WindowsUpdateRepairService
{
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string SoftwareDistPath = Path.Combine(WinDir, "SoftwareDistribution");
    private static readonly string Catroot2Path = Path.Combine(WinDir, "System32", "catroot2");
    private static readonly string CbsLogPath = Path.Combine(WinDir, "Logs", "CBS", "CBS.log");

    public const string MicrosoftUpdateCatalogBaseUrl = "https://www.catalog.update.microsoft.com/";
    public const string Ms0x800F0831TroubleshootUrl = "https://learn.microsoft.com/zh-tw/troubleshoot/windows-server/installing-updates-features-roles/error-0x800f0831-installing-update";
    public const string MsUpdateCorruptionTroubleshootUrl = "https://learn.microsoft.com/zh-tw/troubleshoot/windows-server/installing-updates-features-roles/troubleshoot-windows-update-installation-error-corruption";

    private static readonly Regex KbRegex = new(@"(?<![A-Za-z0-9])KB\d{6,8}(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PackageIdRegex = new(@"Package_for_[A-Za-z0-9_~\.\-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ErrorCodeRegex = new(@"0x[0-9a-fA-F]{8}", RegexOptions.Compiled);
    private static readonly Regex CbsTimestampRegex = new(@"^\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}", RegexOptions.Compiled);

    /// <summary>
    /// Built-in Windows Update error code diagnostic presets.
    /// </summary>
    public static List<WindowsUpdateErrorPreset> GetErrorPresets() =>
    [
        new WindowsUpdateErrorPreset
        {
            Id = "0x800F0831",
            Code = "0x800F0831",
            Title = "CBS 元件存放區損壞 (CBS_E_STORE_CORRUPTION)",
            Description = "Windows 元件存放區缺少先前更新的資訊清單或檔案未正確套用。建議依序執行 DISM ScanHealth → CheckHealth → RestoreHealth → SFC /scannow，重啟電腦並從 CBS.log 找出缺失套件手動安裝。",
            RecommendedAction = "執行 DISM 三段修復 + SFC /scannow + 修復 TrustedInstaller + 掃描 CBS.log",
            DocsUrl = Ms0x800F0831TroubleshootUrl
        },
        new WindowsUpdateErrorPreset
        {
            Id = "0x800F081F",
            Code = "0x800F081F",
            Title = "找不到來源檔案 (CBS_E_SOURCE_MISSING)",
            Description = "Windows Update 或本機元件庫缺少必要修復來源。建議搭配本機 Windows ISO 內的 install.wim/install.esd 執行 DISM RestoreHealth + SFC，並重建 SoftwareDistribution。",
            RecommendedAction = "DISM RestoreHealth (可指定 WIM/ESD 來源) + SFC + 重建 SoftwareDistribution",
            DocsUrl = MsUpdateCorruptionTroubleshootUrl
        },
        new WindowsUpdateErrorPreset
        {
            Id = "0x80240034",
            Code = "0x80240034 / 0x80070002",
            Title = "更新下載停滯或快取資料庫損毀",
            Description = "更新下載中斷或 SoftwareDistribution / BITS 佇列資料不一致（包含 0x80070002、0x80070003、0x80240034）。",
            RecommendedAction = "停止 WU/BITS 服務 + 清空 BITS 佇列 + 備份並重建 SoftwareDistribution + 觸發更新掃描",
            DocsUrl = MsUpdateCorruptionTroubleshootUrl
        },
        new WindowsUpdateErrorPreset
        {
            Id = "0x80070422",
            Code = "0x80070422 / 0x80240016",
            Title = "更新服務已停用、權限異常或忙碌衝突",
            Description = "wuauserv / BITS / UsoSvc / TrustedInstaller 啟動類型異常或服務 ACL 權限受損，導致更新無法啟動或 TiWorker 長時間卡死。",
            RecommendedAction = "還原服務啟動類型 (含 TrustedInstaller=Manual) + 重設服務 ACL + 重新註冊核心 DLL",
            DocsUrl = MsUpdateCorruptionTroubleshootUrl
        },
        new WindowsUpdateErrorPreset
        {
            Id = "FullDeepReset",
            Code = "完整深度修復",
            Title = "全項目 Windows Update 深度重置 (不含網路堆疊)",
            Description = "一次執行 DISM 三段 + SFC + 服務重設 + 核心 DLL 註冊 + SoftwareDistribution 重建 + 服務 ACL 還原；網路堆疊預設不勾選以免中斷 TLS/VPN。",
            RecommendedAction = "執行全部安全修復項目並在完成後提示重新啟動電腦",
            DocsUrl = MsUpdateCorruptionTroubleshootUrl
        }
    ];

    /// <summary>
    /// Applies a preset's recommended checkboxes onto an existing options object.
    /// </summary>
    public static void ApplyPresetToOptions(string presetId, WindowsUpdateRepairOptions options)
    {
        switch (presetId)
        {
            case "0x800F0831":
                options.RunDismScanHealth = true;
                options.RunDismCheckHealth = true;
                options.RunDismRestoreHealth = true;
                options.RunSfcScanNow = true;
                options.ResetWuServicesAndBits = true;
                options.ReRegisterCoreDlls = false;
                options.ResetSoftwareDistribution = false;
                options.BackupSoftwareDistribution = true;
                options.ResetCatroot2WithCryptSvc = false;
                options.ResetServiceSecurityDescriptors = false;
                options.ResetNetworkStack = false;
                options.FixTrustedInstallerStartupType = true;
                options.TriggerUpdateScanAfter = true;
                options.PromptRebootAfterRepair = true;
                break;

            case "0x800F081F":
                options.RunDismScanHealth = true;
                options.RunDismCheckHealth = true;
                options.RunDismRestoreHealth = true;
                options.RunSfcScanNow = true;
                options.ResetWuServicesAndBits = true;
                options.ReRegisterCoreDlls = false;
                options.ResetSoftwareDistribution = true;
                options.BackupSoftwareDistribution = true;
                options.ResetCatroot2WithCryptSvc = false;
                options.ResetServiceSecurityDescriptors = false;
                options.ResetNetworkStack = false;
                options.FixTrustedInstallerStartupType = true;
                options.TriggerUpdateScanAfter = true;
                options.PromptRebootAfterRepair = true;
                break;

            case "0x80240034":
                options.RunDismScanHealth = false;
                options.RunDismCheckHealth = false;
                options.RunDismRestoreHealth = false;
                options.RunSfcScanNow = false;
                options.ResetWuServicesAndBits = true;
                options.ReRegisterCoreDlls = false;
                options.ResetSoftwareDistribution = true;
                options.BackupSoftwareDistribution = true;
                options.ResetCatroot2WithCryptSvc = false;
                options.ResetServiceSecurityDescriptors = false;
                options.ResetNetworkStack = false;
                options.FixTrustedInstallerStartupType = true;
                options.TriggerUpdateScanAfter = true;
                options.PromptRebootAfterRepair = false;
                break;

            case "0x80070422":
                options.RunDismScanHealth = false;
                options.RunDismCheckHealth = false;
                options.RunDismRestoreHealth = false;
                options.RunSfcScanNow = false;
                options.ResetWuServicesAndBits = true;
                options.ReRegisterCoreDlls = true;
                options.ResetSoftwareDistribution = false;
                options.BackupSoftwareDistribution = true;
                options.ResetCatroot2WithCryptSvc = false;
                options.ResetServiceSecurityDescriptors = true;
                options.ResetNetworkStack = false;
                options.FixTrustedInstallerStartupType = true;
                options.TriggerUpdateScanAfter = true;
                options.PromptRebootAfterRepair = false;
                break;

            case "FullDeepReset":
                options.RunDismScanHealth = true;
                options.RunDismCheckHealth = true;
                options.RunDismRestoreHealth = true;
                options.RunSfcScanNow = true;
                options.ResetWuServicesAndBits = true;
                options.ReRegisterCoreDlls = true;
                options.ResetSoftwareDistribution = true;
                options.BackupSoftwareDistribution = true;
                options.ResetCatroot2WithCryptSvc = false;
                options.ResetServiceSecurityDescriptors = true;
                options.ResetNetworkStack = false;
                options.FixTrustedInstallerStartupType = true;
                options.TriggerUpdateScanAfter = true;
                options.PromptRebootAfterRepair = true;
                break;
        }
    }

    /// <summary>
    /// Scans current Windows Update status, services, TrustedInstaller/TiWorker health, reboot flags, and cache sizes.
    /// </summary>
    public async Task<WindowsUpdateInfo> GetUpdateStatusAsync()
    {
        return await Task.Run(async () =>
        {
            var info = new WindowsUpdateInfo();

            try
            {
                // Query wuauserv service status via sc.exe
                var (scOutput, _, _) = await ProcessHelper.RunProcessAsync("sc.exe", "query wuauserv");
                if (scOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                {
                    info.ServiceStatus = "Running";
                    info.StatusBadge = "🟢 運作中 (Running)";
                }
                else if (scOutput.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
                {
                    info.ServiceStatus = "Stopped";
                    info.StatusBadge = "⚪ 已停止 (Stopped)";
                }
                else
                {
                    info.ServiceStatus = "Unknown";
                    info.StatusBadge = "❓ 未知狀態";
                }

                // Query startup type via sc qc
                var (qcOutput, _, _) = await ProcessHelper.RunProcessAsync("sc.exe", "qc wuauserv");
                if (qcOutput.Contains("DISABLED", StringComparison.OrdinalIgnoreCase))
                {
                    info.StartupType = "Disabled";
                    info.IsDisabled = true;
                    info.StatusBadge = "🔴 已停用 (Disabled)";
                }
                else if (qcOutput.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase))
                {
                    info.StartupType = "Automatic";
                    info.IsDisabled = false;
                }
                else if (qcOutput.Contains("DEMAND_START", StringComparison.OrdinalIgnoreCase))
                {
                    info.StartupType = "Manual";
                    info.IsDisabled = false;
                }

                // Query TrustedInstaller startup type (should normally be DEMAND_START / Manual)
                var (tiQcOutput, _, _) = await ProcessHelper.RunProcessAsync("sc.exe", "qc TrustedInstaller");
                if (tiQcOutput.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase))
                    info.TrustedInstallerStartupType = "Automatic";
                else if (tiQcOutput.Contains("DEMAND_START", StringComparison.OrdinalIgnoreCase))
                    info.TrustedInstallerStartupType = "Manual";
                else if (tiQcOutput.Contains("DISABLED", StringComparison.OrdinalIgnoreCase))
                    info.TrustedInstallerStartupType = "Disabled";

                // Measure live TiWorker.exe private RAM usage
                try
                {
                    double tiMb = 0;
                    foreach (var p in Process.GetProcessesByName("TiWorker"))
                    {
                        using (p)
                        {
                            try { tiMb += p.PrivateMemorySize64 / (1024.0 * 1024.0); } catch { }
                        }
                    }
                    info.TiWorkerRamMb = Math.Round(tiMb, 1);
                    info.IsTiWorkerAbnormal = info.TrustedInstallerStartupType == "Automatic" || info.TiWorkerRamMb >= 1024.0;
                }
                catch { }

                // Check Group Policy registry for NoAutoUpdate
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU");
                    if (key != null)
                    {
                        var noAuto = key.GetValue("NoAutoUpdate");
                        if (noAuto is int val && val == 1)
                        {
                            info.IsAutoUpdateEnabled = false;
                            info.IsDisabled = true;
                            info.StatusBadge = "🔴 已被原則停用 (Disabled via Policy)";
                        }
                    }
                }
                catch { }

                // Check pending reboot flags (WU + CBS)
                try
                {
                    using var wuReboot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
                    using var cbsReboot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                    info.IsRebootRequired = wuReboot != null || cbsReboot != null;
                }
                catch { }

                // Check last scan time
                try
                {
                    using var detectKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Detect");
                    if (detectKey != null)
                    {
                        var lastSuccess = detectKey.GetValue("LastSuccessTime");
                        if (lastSuccess != null)
                        {
                            info.LastCheckTime = lastSuccess.ToString() ?? "未記錄";
                        }
                    }
                }
                catch { }

                // Calculate SoftwareDistribution folder size
                if (Directory.Exists(SoftwareDistPath))
                {
                    var (bytes, count) = GetDirectorySizeSafe(SoftwareDistPath);
                    info.SoftwareDistributionBytes = bytes;
                    info.SoftwareDistributionFiles = count;
                    info.SoftwareDistributionDisplay = FormatBytes(bytes);
                }

                // Calculate Catroot2 folder size
                if (Directory.Exists(Catroot2Path))
                {
                    var (bytes, _) = GetDirectorySizeSafe(Catroot2Path);
                    info.Catroot2Bytes = bytes;
                    info.Catroot2Display = FormatBytes(bytes);
                }
            }
            catch { }

            return info;
        });
    }

    /// <summary>
    /// Executes the user-configured Windows Update &amp; 0x800F0831 repair pipeline with guaranteed service restoration.
    /// </summary>
    public async Task<(bool Success, bool RebootRecommended, string Summary)> RunConfigurableRepairPipelineAsync(
        WindowsUpdateRepairOptions options,
        SystemRepairService repairService,
        Action<string> onOutput,
        CancellationToken ct = default)
    {
        bool anyFailure = false;
        bool rebootRecommended = false;
        string? detectedError = null;

        onOutput("══════════════════════════════════════════════════════════════");
        onOutput($"[{DateTime.Now:HH:mm:ss}] 啟動自訂 Windows Update 與元件存放區修復流程");
        onOutput("══════════════════════════════════════════════════════════════");

        // 1. Optional TrustedInstaller startup fix (prevents runaway Automatic TiWorker)
        if (options.FixTrustedInstallerStartupType)
        {
            if (ct.IsCancellationRequested) return (false, false, "修復流程已取消。");
            var (tiOk, tiMsg) = await RestoreTrustedInstallerDefaultAsync(onOutput);
            if (!tiOk) onOutput($"[WARN] {tiMsg}");
        }

        // 2. DISM ScanHealth -> CheckHealth -> RestoreHealth + SFC /scannow (Microsoft 0x800F0831 official order)
        if (options.RunDismScanHealth || options.RunDismCheckHealth || options.RunDismRestoreHealth || options.RunSfcScanNow)
        {
            if (ct.IsCancellationRequested) return (false, false, "修復流程已取消。");
            string? src = options.UseLocalRepairSource && !string.IsNullOrWhiteSpace(options.LocalRepairSourcePath)
                ? options.LocalRepairSourcePath.Trim()
                : null;

            var dismResult = await repairService.RunFullDismSfcSequenceAsync(
                options.RunDismScanHealth,
                options.RunDismCheckHealth,
                options.RunDismRestoreHealth,
                src,
                options.RunSfcScanNow,
                onOutput,
                ct);

            if (!dismResult.Success) anyFailure = true;
            if (dismResult.RebootRecommended) rebootRecommended = true;
            detectedError ??= dismResult.DetectedErrorCode;
        }

        // 3. Service / BITS / DLL / SoftwareDistribution / Catroot2 / ACL / Network stages
        bool needsServiceCycle = options.ResetWuServicesAndBits
            || options.ReRegisterCoreDlls
            || options.ResetSoftwareDistribution
            || options.ResetCatroot2WithCryptSvc
            || options.ResetServiceSecurityDescriptors;

        if (needsServiceCycle)
        {
            if (ct.IsCancellationRequested) return (false, rebootRecommended, "修復流程已取消。");

            var stoppedServices = new List<string>();
            bool stoppedCryptSvc = false;

            try
            {
                onOutput("\n[WU 服務階段] 正在安全停止 Windows Update 相關服務...");
                string[] baseServices = ["wuauserv", "bits", "msiserver", "dosvc"];
                foreach (var svc in baseServices)
                {
                    if (ct.IsCancellationRequested) break;
                    onOutput($"▸ 停止服務: {svc}");
                    await ProcessHelper.RunProcessAsync("net.exe", $"stop {svc} /y");
                    stoppedServices.Add(svc);
                }

                if (!ct.IsCancellationRequested && options.ResetWuServicesAndBits)
                {
                    onOutput("\n[BITS 佇列] 正在清除 BITS 背景傳輸排程與佇列檔案...");
                    var (bitsOut, _, _) = await ProcessHelper.RunProcessAsync("bitsadmin.exe", "/reset /allusers");
                    if (!string.IsNullOrWhiteSpace(bitsOut)) onOutput(bitsOut.Trim());
                }

                if (!ct.IsCancellationRequested && options.ResetSoftwareDistribution)
                {
                    await Task.Delay(600, ct);
                    onOutput("\n[SoftwareDistribution] 正在重置下載與資料庫快取...");
                    ResetDirectoryWithOptionalBackup(SoftwareDistPath, options.BackupSoftwareDistribution, isSoftwareDistribution: true, onOutput);
                }

                if (!ct.IsCancellationRequested && options.ResetCatroot2WithCryptSvc)
                {
                    onOutput("\n[Catroot2] 使用者已勾選重置 Catroot2：短暫停止 CryptSvc 並保證於 finally 恢復...");
                    await ProcessHelper.RunProcessAsync("net.exe", "stop cryptSvc /y");
                    stoppedCryptSvc = true;
                    await Task.Delay(500, ct);
                    ResetDirectoryWithOptionalBackup(Catroot2Path, options.BackupSoftwareDistribution, isSoftwareDistribution: false, onOutput);
                }

                if (!ct.IsCancellationRequested && options.ReRegisterCoreDlls)
                {
                    onOutput("\n[核心 DLL] 正在重新註冊 Windows Update 核心 DLL 元件...");
                    int count = await ReRegisterUpdateDllsAsync(ct);
                    onOutput($"✅ 已完成重新註冊 {count} 個系統核心 DLL。");
                }

                if (!ct.IsCancellationRequested && options.ResetServiceSecurityDescriptors)
                {
                    onOutput("\n[服務權限 ACL] 正在還原 bits 與 wuauserv 安全性描述元...");
                    var (_, errBits, codeBits) = await ProcessHelper.RunProcessAsync("sc.exe", "sdset bits \"D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;AU)(A;;CCLCSWRPWPDTLOCRRC;;;PU)\"");
                    var (_, errWu, codeWu) = await ProcessHelper.RunProcessAsync("sc.exe", "sdset wuauserv \"D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;AU)(A;;CCLCSWRPWPDTLOCRRC;;;PU)\"");
                    if (codeBits != 0 || codeWu != 0)
                    {
                        onOutput($"[WARN] 部分服務 ACL 設定未套用 (bits={codeBits}, wuauserv={codeWu}): {errBits} {errWu}".Trim());
                    }
                }
            }
            finally
            {
                // Always restore services even on cancellation or exception!
                if (stoppedCryptSvc)
                {
                    onOutput("▸ [還原] 立即重新啟動 cryptSvc (確保 TLS/憑證正常)...");
                    await ProcessHelper.RunProcessAsync("sc.exe", "config cryptSvc start= auto");
                    await ProcessHelper.RunProcessAsync("net.exe", "start cryptSvc");
                }

                if (stoppedServices.Count > 0)
                {
                    onOutput("\n[還原服務] 正在恢復 Windows Update 與相依服務啟動設定並重啟...");
                    await ProcessHelper.RunProcessAsync("sc.exe", "config bits start= delayed-auto");
                    await ProcessHelper.RunProcessAsync("sc.exe", "config wuauserv start= demand");
                    await ProcessHelper.RunProcessAsync("sc.exe", "config dosvc start= delayed-auto");
                    foreach (var svc in new[] { "bits", "wuauserv", "dosvc" })
                    {
                        await ProcessHelper.RunProcessAsync("net.exe", $"start {svc}");
                    }
                }
            }
        }

        // 4. Optional Network Stack reset (explicit opt-in only)
        if (!ct.IsCancellationRequested && options.ResetNetworkStack)
        {
            onOutput("\n[網路堆疊] 正在重置 Winsock、TCP/IP 與 WinHTTP 代理 (使用者已勾選)...");
            var (wOut, _, _) = await ProcessHelper.RunProcessAsync("netsh.exe", "winsock reset");
            if (!string.IsNullOrWhiteSpace(wOut)) onOutput(wOut.Trim());
            var (ipOut, _, _) = await ProcessHelper.RunProcessAsync("netsh.exe", "int ip reset");
            if (!string.IsNullOrWhiteSpace(ipOut)) onOutput(ipOut.Trim());
            var (proxyOut, _, _) = await ProcessHelper.RunProcessAsync("netsh.exe", "winhttp reset proxy");
            if (!string.IsNullOrWhiteSpace(proxyOut)) onOutput(proxyOut.Trim());
            rebootRecommended = true;
            onOutput("⚠ 網路堆疊已重置：必須重新啟動電腦以恢復完整網路與 TLS 連線。");
        }

        // 5. Trigger modern Windows Update scan
        if (!ct.IsCancellationRequested && options.TriggerUpdateScanAfter)
        {
            await TriggerUpdateCheckAsync(onOutput);
        }

        if (ct.IsCancellationRequested)
        {
            return (false, rebootRecommended, "修復作業已由使用者取消（已自動恢復系統服務）。");
        }

        string finalSummary = !anyFailure
            ? (rebootRecommended ? "Windows Update 自訂修復完成，建議重新啟動電腦套用變更。" : "Windows Update 自訂修復完成！")
            : detectedError != null
                ? $"修復完成但偵測到錯誤 {detectedError}：建議使用下方「掃描 CBS.log」找出缺失 KB 並手動安裝。"
                : "修復流程已完成，但部分步驟回報警告或錯誤，請檢視日誌。";

        onOutput("\n══════════════════════════════════════════════════════════════");
        onOutput($"[{DateTime.Now:HH:mm:ss}] {finalSummary}");
        onOutput("══════════════════════════════════════════════════════════════");
        return (!anyFailure, rebootRecommended, finalSummary);
    }

    /// <summary>
    /// Executes the classic Windows Update core repair procedure with guaranteed service restoration.
    /// </summary>
    public async Task<int> FixWindowsUpdateErrorsAsync(Action<string> onOutput, CancellationToken ct = default, bool resetNetworkStack = false)
    {
        var opts = new WindowsUpdateRepairOptions
        {
            RunDismScanHealth = false,
            RunDismCheckHealth = false,
            RunDismRestoreHealth = false,
            RunSfcScanNow = false,
            ResetWuServicesAndBits = true,
            ReRegisterCoreDlls = true,
            ResetSoftwareDistribution = false,
            ResetCatroot2WithCryptSvc = false,
            ResetServiceSecurityDescriptors = true,
            ResetNetworkStack = resetNetworkStack,
            FixTrustedInstallerStartupType = true,
            TriggerUpdateScanAfter = true,
            PromptRebootAfterRepair = resetNetworkStack
        };

        var (ok, _, _) = await RunConfigurableRepairPipelineAsync(opts, new SystemRepairService(), onOutput, ct);
        return ct.IsCancellationRequested ? -1 : (ok ? 0 : 1);
    }

    /// <summary>
    /// Resets SoftwareDistribution (and optionally Catroot2 when resetCatroot2 is true) with guaranteed service restart.
    /// </summary>
    public async Task<int> ResetSoftwareDistributionFolderAsync(bool backupFirst, Action<string> onOutput, CancellationToken ct = default, bool resetCatroot2 = false)
    {
        onOutput("══════════════════════════════════════════════════════════════");
        onOutput($"[{DateTime.Now:HH:mm:ss}] 啟動 SoftwareDistribution {(resetCatroot2 ? "與 Catroot2 " : "")}重建重置常式");
        onOutput($"備份模式: {(backupFirst ? "保留備份 (.bak) 模式" : "直接深度清空模式")}");
        onOutput("══════════════════════════════════════════════════════════════");

        string[] services = ["wuauserv", "bits", "dosvc"];
        bool stoppedCryptSvc = false;

        try
        {
            onOutput("\n[步驟 1/3] 正在安全停止佔用更新庫之服務 (wuauserv, bits, dosvc)...");
            foreach (var svc in services)
            {
                if (ct.IsCancellationRequested) return -1;
                await ProcessHelper.RunProcessAsync("net.exe", $"stop {svc} /y");
            }

            if (resetCatroot2)
            {
                onOutput("▸ 短暫停止 cryptSvc 以釋放 Catroot2 檔案鎖定 (結束後自動恢復)...");
                await ProcessHelper.RunProcessAsync("net.exe", "stop cryptSvc /y");
                stoppedCryptSvc = true;
            }

            await Task.Delay(800, ct);

            onOutput("\n[步驟 2/3] 正在處理更新快取目錄...");
            ResetDirectoryWithOptionalBackup(SoftwareDistPath, backupFirst, isSoftwareDistribution: true, onOutput);

            if (resetCatroot2)
            {
                ResetDirectoryWithOptionalBackup(Catroot2Path, backupFirst, isSoftwareDistribution: false, onOutput);
            }
            else
            {
                onOutput("ℹ️ 已略過 Catroot2（未停止 CryptSvc 以保護 TLS/憑證運作；如需重置請於上方進階選項勾選）。");
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            onOutput("[INFO] 使用者已取消重建作業，正在恢復系統服務...");
            return -1;
        }
        finally
        {
            onOutput("\n[步驟 3/3] 正在重啟服務並促使 Windows 自動建立全新資料結構...");
            if (stoppedCryptSvc)
            {
                await ProcessHelper.RunProcessAsync("sc.exe", "config cryptSvc start= auto");
                await ProcessHelper.RunProcessAsync("net.exe", "start cryptSvc");
            }
            foreach (var svc in services)
            {
                await ProcessHelper.RunProcessAsync("net.exe", $"start {svc}");
            }
            onOutput($"[{DateTime.Now:HH:mm:ss}] ✅ 更新相關服務已恢復運作。");
        }
    }

    /// <summary>
    /// Enables or disables automatic Windows Update via valid sc.exe syntax and Group Policy registry keys.
    /// </summary>
    public async Task<bool> SetWindowsUpdateDisabledAsync(bool disable, Action<string> onOutput)
    {
        return await Task.Run(async () =>
        {
            try
            {
                if (disable)
                {
                    onOutput($"[{DateTime.Now:HH:mm:ss}] 正在將 Windows Update 設定為「🔴 停用」狀態...");

                    var (_, err1, code1) = await ProcessHelper.RunProcessAsync("sc.exe", "config wuauserv start= disabled");
                    await ProcessHelper.RunProcessAsync("sc.exe", "config UsoSvc start= disabled");
                    await ProcessHelper.RunProcessAsync("net.exe", "stop wuauserv /y");

                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU");
                    if (key != null)
                    {
                        key.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                        key.SetValue("AUOptions", 2, RegistryValueKind.DWord);
                    }

                    if (code1 != 0)
                    {
                        onOutput($"[WARN] sc config wuauserv 回傳 {code1}: {err1}（已套用群組原則 NoAutoUpdate=1）");
                    }
                    onOutput("✅ Windows Update 服務已停止且啟動類型已設為 Disabled，原則 NoAutoUpdate=1 已寫入。");
                    return true;
                }
                else
                {
                    onOutput($"[{DateTime.Now:HH:mm:ss}] 正在將 Windows Update 恢復為「🟢 正常運作」狀態...");

                    var (_, err1, code1) = await ProcessHelper.RunProcessAsync("sc.exe", "config wuauserv start= demand");
                    await ProcessHelper.RunProcessAsync("sc.exe", "config UsoSvc start= demand");
                    await ProcessHelper.RunProcessAsync("sc.exe", "config bits start= delayed-auto");

                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", true);
                        if (key != null)
                        {
                            key.SetValue("NoAutoUpdate", 0, RegistryValueKind.DWord);
                            key.SetValue("AUOptions", 4, RegistryValueKind.DWord);
                        }
                    }
                    catch { }

                    await ProcessHelper.RunProcessAsync("net.exe", "start wuauserv");
                    if (code1 != 0)
                    {
                        onOutput($"[WARN] sc config wuauserv 回傳 {code1}: {err1}");
                    }
                    onOutput("✅ Windows Update 服務已重新啟動，官方自動更新功能已順利恢復。");
                    return true;
                }
            }
            catch (Exception ex)
            {
                onOutput($"[ERROR] 切換 Windows Update 狀態失敗: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Restores TrustedInstaller (Windows Modules Installer) startup type to Windows default Manual (demand),
    /// preventing runaway background TiWorker.exe RAM/CPU spikes.
    /// </summary>
    public async Task<(bool Success, string Message)> RestoreTrustedInstallerDefaultAsync(Action<string>? onOutput = null)
    {
        onOutput?.Invoke($"[{DateTime.Now:HH:mm:ss}] 正在將 TrustedInstaller 啟動類型還原為 Windows 預設「手動 (Manual / demand)」...");
        var (stdout, stderr, exitCode) = await ProcessHelper.RunProcessAsync("sc.exe", "config TrustedInstaller start= demand");
        if (exitCode == 0)
        {
            var msg = "✅ TrustedInstaller 已還原為「手動 (DEMAND_START)」，可避免背景 TiWorker 長時間佔用數 GB 記憶體。";
            onOutput?.Invoke(msg);
            return (true, msg);
        }

        var err = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
        var failMsg = $"還原 TrustedInstaller 啟動類型失敗 (ExitCode={exitCode}): {err}";
        onOutput?.Invoke($"[ERROR] {failMsg}");
        return (false, failMsg);
    }

    /// <summary>
    /// Triggers an immediate Windows Update scan via modern UsoClient.
    /// </summary>
    public async Task TriggerUpdateCheckAsync(Action<string> onOutput)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] 正在發送現代更新指令觸發 Windows Update 背景掃描 (UsoClient)...");
        await ProcessHelper.RunProcessAsync("UsoClient.exe", "StartInteractiveScan");
        await ProcessHelper.RunProcessAsync("UsoClient.exe", "StartScan");
        onOutput("✅ 已送出更新掃描要求 (UsoClient StartInteractiveScan / StartScan)。請至 Windows 設定檢視更新進度。");
    }

    /// <summary>
    /// Scans the tail of C:\Windows\Logs\CBS\CBS.log (using shared read so locked files never fail)
    /// to extract 0x800F0831 / CBS_E_STORE_CORRUPTION, missing package IDs, failing KBs, and corrupt member files.
    /// </summary>
    public async Task<(List<CbsDiagnosticFinding> Findings, string Summary)> AnalyzeCbsLogAsync(int maxTailMb = 20, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(CbsLogPath))
            {
                return (new List<CbsDiagnosticFinding>(), $"找不到 {CbsLogPath}（檔案不存在或尚未產生日誌）。");
            }

            try
            {
                var tailLines = ReadFileTailLines(CbsLogPath, maxTailMb * 1024L * 1024L, ct);
                var findings = ParseCbsLogLines(tailLines);
                string summary = findings.Count > 0
                    ? $"於 CBS.log 偵測到 {findings.Count} 筆元件存放區異常／缺失套件記錄。"
                    : "CBS.log 近期記錄未發現 0x800F0831 或元件存放區損壞項目。";
                return (findings, summary);
            }
            catch (UnauthorizedAccessException)
            {
                return (new List<CbsDiagnosticFinding>(), "無法讀取 CBS.log（權限不足，請以系統管理員身分執行）。");
            }
            catch (Exception ex)
            {
                return (new List<CbsDiagnosticFinding>(), $"解析 CBS.log 失敗: {ex.Message}");
            }
        }, ct);
    }

    /// <summary>
    /// Pure parser for CBS.log lines (unit-testable without touching C:\Windows\Logs\CBS\CBS.log).
    /// </summary>
    public static List<CbsDiagnosticFinding> ParseCbsLogLines(IEnumerable<string> lines)
    {
        var results = new List<CbsDiagnosticFinding>();
        var dedupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;

            bool isStoreCorruption = rawLine.Contains("0x800f0831", StringComparison.OrdinalIgnoreCase)
                || rawLine.Contains("CBS_E_STORE_CORRUPTION", StringComparison.OrdinalIgnoreCase)
                || rawLine.Contains("Mark store corruption", StringComparison.OrdinalIgnoreCase);
            bool isMissingPkg = rawLine.Contains("Failed to resolve package", StringComparison.OrdinalIgnoreCase)
                || rawLine.Contains("Cannot resolve package", StringComparison.OrdinalIgnoreCase)
                || rawLine.Contains("Failed to open package", StringComparison.OrdinalIgnoreCase);
            bool isCorruptMember = rawLine.Contains("Cannot repair member file", StringComparison.OrdinalIgnoreCase)
                || rawLine.Contains("Hashes for file member", StringComparison.OrdinalIgnoreCase);
            bool isSourceMissing = rawLine.Contains("0x800f081f", StringComparison.OrdinalIgnoreCase)
                || rawLine.Contains("CBS_E_SOURCE_MISSING", StringComparison.OrdinalIgnoreCase);

            if (!isStoreCorruption && !isMissingPkg && !isCorruptMember && !isSourceMissing)
                continue;

            string ts = CbsTimestampRegex.Match(rawLine) is { Success: true } tm ? tm.Value : "";
            string kb = KbRegex.Match(rawLine) is { Success: true } km ? km.Value.ToUpperInvariant() : "";
            string pkg = PackageIdRegex.Match(rawLine) is { Success: true } pm ? pm.Value : "";
            string errCode = ErrorCodeRegex.Match(rawLine) is { Success: true } em
                ? em.Value.ToUpperInvariant().Replace("0X", "0x")
                : (isStoreCorruption ? "0x800F0831" : isSourceMissing ? "0x800F081F" : "");

            string category = isMissingPkg ? "缺失套件 (Missing Package)"
                : isStoreCorruption ? "存放區損壞 (0x800F0831)"
                : isSourceMissing ? "缺少修復來源 (0x800F081F)"
                : "損毀系統檔 (Corrupt Member)";

            string searchQuery = !string.IsNullOrWhiteSpace(kb) ? kb : pkg;
            string dedupKey = $"{category}|{kb}|{pkg}|{errCode}";
            if (dedupKey == $"{category}|||")
            {
                dedupKey = rawLine.Length > 120 ? rawLine[..120] : rawLine;
            }
            if (!dedupKeys.Add(dedupKey)) continue;

            string summary = rawLine.Trim();
            if (summary.Length > 200) summary = summary[..200] + "...";

            results.Add(new CbsDiagnosticFinding
            {
                Timestamp = ts,
                ErrorCode = errCode,
                KbNumber = kb,
                PackageId = pkg,
                Category = category,
                RawSummary = summary,
                SearchQuery = searchQuery,
                CatalogSearchUrl = BuildCatalogUrl(searchQuery)
            });
        }

        // Most recent entries first, cap at 50 items
        results.Reverse();
        return results.Take(50).ToList();
    }

    /// <summary>
    /// Queries Windows Update COM history to find failed/aborted updates along with HResult and KB numbers.
    /// </summary>
    public async Task<List<WindowsUpdateHistoryItem>> GetFailedUpdateHistoryAsync(int maxScan = 120, CancellationToken ct = default)
    {
        var items = new List<WindowsUpdateHistoryItem>();
        try
        {
            var script = "$s = New-Object -ComObject Microsoft.Update.Session; " +
                         "$q = $s.CreateUpdateSearcher(); " +
                         "$n = [Math]::Min(" + maxScan + ", $q.GetTotalHistoryCount()); " +
                         "if ($n -gt 0) { $q.QueryHistory(0, $n) | Where-Object { $_.ResultCode -ne 2 } | Select-Object -First 30 | ForEach-Object { " +
                         "'{0:yyyy-MM-dd HH:mm}|{1}|0x{2:X8}|{3}' -f $_.Date, $_.ResultCode, ($_.HResult -band 0xFFFFFFFF), ($_.Title -replace '\\r|\\n|\\|', ' ') } }";

            var (stdout, _, exitCode) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"{script}\"", cancellationToken: ct);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|');
                    if (parts.Length < 4) continue;
                    string dateStr = parts[0].Trim();
                    string codeStr = parts[1].Trim();
                    string hresult = parts[2].Trim();
                    string title = parts[3].Trim();

                    string kb = KbRegex.Match(title) is { Success: true } km ? km.Value.ToUpperInvariant() : "";
                    string resultText = codeStr switch
                    {
                        "3" => "部分成功 (錯誤)",
                        "4" => "安裝失敗",
                        "5" => "已中止",
                        _ => $"狀態 {codeStr}"
                    };

                    items.Add(new WindowsUpdateHistoryItem
                    {
                        DateDisplay = dateStr,
                        Title = title,
                        KbNumber = kb,
                        ResultText = resultText,
                        IsFailed = codeStr == "4",
                        HResultHex = hresult,
                        SearchQuery = kb,
                        CatalogSearchUrl = BuildCatalogUrl(kb)
                    });
                }
            }
        }
        catch { }

        return items;
    }

    /// <summary>
    /// Installs a manually downloaded .msu or .cab update package from Microsoft Update Catalog.
    /// </summary>
    public async Task<(bool Success, int ExitCode, string Message)> InstallLocalUpdatePackageAsync(
        string packagePath,
        Action<string> onOutput,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            return (false, -1, "找不到指定的更新套件檔案 (.msu / .cab)。");

        var ext = Path.GetExtension(packagePath).ToLowerInvariant();
        if (ext == ".msu")
        {
            onOutput($"[{DateTime.Now:HH:mm:ss}] 正在安裝獨立更新套件 (.msu): {packagePath}");
            var (_, stderr, exitCode) = await ProcessHelper.RunProcessAsync("wusa.exe", $"\"{packagePath}\" /quiet /norestart", cancellationToken: ct);
            bool ok = exitCode == 0 || exitCode == 3010 || exitCode == 2359302; // 2359302 = already installed
            string msg = exitCode switch
            {
                0 => "✅ .msu 更新套件安裝成功！",
                3010 => "✅ .msu 更新套件安裝成功，需重新啟動電腦生效。",
                2359302 => "ℹ️ 此更新套件已安裝於本機系統。",
                _ => $"❌ .msu 安裝失敗 (ExitCode={exitCode}): {stderr.Trim()}"
            };
            onOutput(msg);
            return (ok, exitCode, msg);
        }
        else if (ext == ".cab")
        {
            onOutput($"[{DateTime.Now:HH:mm:ss}] 正在透過 DISM 安裝更新套件 (.cab): {packagePath}");
            var res = await StreamingProcessRunner.RunAsync(
                "wu-cab-install",
                "dism.exe",
                $"/online /add-package /packagepath:\"{packagePath}\" /norestart",
                onOutput,
                ct);
            bool ok = res.ExitCode == 0 || res.ExitCode == 3010;
            string msg = res.ExitCode switch
            {
                0 => "✅ .cab 更新套件安裝成功！",
                3010 => "✅ .cab 更新套件安裝成功，需重新啟動電腦生效。",
                _ => $"❌ .cab 安裝失敗 (ExitCode={res.ExitCode})。"
            };
            onOutput(msg);
            return (ok, res.ExitCode, msg);
        }

        return (false, -1, "僅支援從 Microsoft Update 目錄下載的 .msu 或 .cab 套件檔案。");
    }

    public static string BuildCatalogUrl(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return MicrosoftUpdateCatalogBaseUrl;
        return $"{MicrosoftUpdateCatalogBaseUrl}Search.aspx?q={Uri.EscapeDataString(query.Trim())}";
    }

    public static void OpenMicrosoftUpdateCatalog(string? query = null)
    {
        Process.Start(new ProcessStartInfo(BuildCatalogUrl(query)) { UseShellExecute = true });
    }

    public static void OpenMicrosoftTroubleshootGuide(string? errorCode = null)
    {
        string url = !string.IsNullOrWhiteSpace(errorCode) && errorCode.Contains("0x800F0831", StringComparison.OrdinalIgnoreCase)
            ? Ms0x800F0831TroubleshootUrl
            : MsUpdateCorruptionTroubleshootUrl;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void LaunchWindowsUpdateTroubleshooter(bool useLegacyMsdt = false)
    {
        if (useLegacyMsdt)
        {
            Process.Start(new ProcessStartInfo("msdt.exe", "/id WindowsUpdateDiagnostic") { UseShellExecute = true });
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:troubleshoot") { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("msdt.exe", "/id WindowsUpdateDiagnostic") { UseShellExecute = true });
        }
    }

    private static async Task<int> ReRegisterUpdateDllsAsync(CancellationToken ct)
    {
        string[] dlls =
        [
            "atl.dll", "urlmon.dll", "mshtml.dll", "shdocvw.dll", "browseui.dll",
            "jscript.dll", "vbscript.dll", "scrrun.dll", "msxml.dll", "msxml3.dll", "msxml6.dll",
            "actxprxy.dll", "softpub.dll", "wintrust.dll", "dssenh.dll", "rsaenh.dll",
            "gpkcsp.dll", "sccbase.dll", "slbcsp.dll", "cryptdlg.dll", "oleaut32.dll",
            "ole32.dll", "shell32.dll", "initpki.dll", "wuapi.dll", "wuaueng.dll",
            "wuaueng1.dll", "wucltui.dll", "wups.dll", "wups2.dll", "wuweb.dll",
            "qmgr.dll", "qmgrprxy.dll", "wucltux.dll", "muweb.dll", "wuwebv.dll"
        ];

        int registeredCount = 0;
        foreach (var dll in dlls)
        {
            if (ct.IsCancellationRequested) break;
            var sysDllPath = Path.Combine(Environment.SystemDirectory, dll);
            if (File.Exists(sysDllPath))
            {
                await ProcessHelper.RunProcessAsync("regsvr32.exe", $"/s \"{sysDllPath}\"");
                registeredCount++;
            }
        }
        return registeredCount;
    }

    private static void ResetDirectoryWithOptionalBackup(string targetPath, bool backupFirst, bool isSoftwareDistribution, Action<string> onOutput)
    {
        if (!Directory.Exists(targetPath)) return;
        var timeTag = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var folderName = Path.GetFileName(targetPath);

        if (backupFirst)
        {
            var backupPath = $"{targetPath}.bak_{timeTag}";
            try
            {
                Directory.Move(targetPath, backupPath);
                onOutput($"✅ 成功將 {folderName} 重命名備份為: {Path.GetFileName(backupPath)}");
                return;
            }
            catch (Exception ex)
            {
                onOutput($"⚠️ 無法整體更名 {folderName} ({ex.Message})，改為安全清空內部快取目錄...");
            }
        }

        if (isSoftwareDistribution)
        {
            PurgeDirectoryInternal(Path.Combine(targetPath, "Download"), onOutput);
            PurgeDirectoryInternal(Path.Combine(targetPath, "DataStore"), onOutput);
            onOutput("✅ 已清空 SoftwareDistribution\\Download 與 DataStore 內容。");
        }
        else
        {
            PurgeDirectoryInternal(targetPath, onOutput);
            onOutput($"✅ 已清空 {folderName} 內容。");
        }
    }

    private static List<string> ReadFileTailLines(string path, long maxBytes, CancellationToken ct)
    {
        var lines = new List<string>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > maxBytes)
        {
            fs.Seek(-maxBytes, SeekOrigin.End);
        }

        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        if (fs.Position > 0)
        {
            // Discard partial first line after seek
            reader.ReadLine();
        }

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (ct.IsCancellationRequested) break;
            lines.Add(line);
        }
        return lines;
    }

    private static (long TotalBytes, int FileCount) GetDirectorySizeSafe(string dirPath)
    {
        long total = 0;
        int count = 0;
        try
        {
            var dir = new DirectoryInfo(dirPath);
            foreach (var fi in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    total += fi.Length;
                    count++;
                }
                catch { }
            }
        }
        catch { }
        return (total, count);
    }

    private static void PurgeDirectoryInternal(string dirPath, Action<string> onOutput)
    {
        if (!Directory.Exists(dirPath)) return;
        try
        {
            var dir = new DirectoryInfo(dirPath);
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { file.Delete(); } catch { }
            }
            foreach (var sub in dir.EnumerateDirectories())
            {
                try { sub.Delete(true); } catch { }
            }
        }
        catch (Exception ex)
        {
            onOutput($"[INFO] 清理 {Path.GetFileName(dirPath)} 時略過部分使用中項目: {ex.Message}");
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F2} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }
}
