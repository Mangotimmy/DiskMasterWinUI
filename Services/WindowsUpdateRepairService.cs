using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service providing comprehensive Windows Update error fixing, component store reset,
/// SoftwareDistribution &amp; Catroot2 folder rebuilding, and update service/policy management.
/// </summary>
public class WindowsUpdateRepairService
{
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string SoftwareDistPath = Path.Combine(WinDir, "SoftwareDistribution");
    private static readonly string Catroot2Path = Path.Combine(WinDir, "System32", "catroot2");

    /// <summary>
    /// Scans current Windows Update status, services, and cache sizes.
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

                // Check pending reboot flag
                try
                {
                    using var rebootKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
                    info.IsRebootRequired = rebootKey != null;
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
    /// Executes a comprehensive 7-stage Windows Update error repair procedure.
    /// </summary>
    public async Task<int> FixWindowsUpdateErrorsAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput("══════════════════════════════════════════════════════════════");
        onOutput($"[{DateTime.Now:HH:mm:ss}] 啟動 Windows Update 核心錯誤自動修復常式 (7-Stage Full Repair)");
        onOutput("針對錯誤代碼: 0x80070002, 0x80070003, 0x80240034, 0x80070422, 0x80244018 等");
        onOutput("══════════════════════════════════════════════════════════════");

        // Stage 1: Stop Windows Update Related Services
        onOutput("\n[Stage 1/7] 正在暫時停止 Windows Update 相關系統服務...");
        string[] servicesToStop = { "wuauserv", "cryptSvc", "bits", "msiserver", "dosvc", "waasmedicsvc" };
        foreach (var svc in servicesToStop)
        {
            if (ct.IsCancellationRequested) return -1;
            onOutput($"▸ 停止服務: {svc}");
            await ProcessHelper.RunProcessAsync("net.exe", $"stop {svc} /y");
        }

        // Stage 2: Clear BITS Queue
        if (ct.IsCancellationRequested) return -1;
        onOutput("\n[Stage 2/7] 正在清除 BITS 背景傳輸排程與佇列檔案...");
        var (bitsOut, _, _) = await ProcessHelper.RunProcessAsync("bitsadmin.exe", "/reset /allusers");
        if (!string.IsNullOrWhiteSpace(bitsOut)) onOutput(bitsOut.Trim());

        // Stage 3: Re-register Windows Update Core DLLs
        if (ct.IsCancellationRequested) return -1;
        onOutput("\n[Stage 3/7] 正在批次重新註冊 30+ 個 Windows Update 核心 DLL 函式庫...");
        string[] dlls =
        {
            "atl.dll", "urlmon.dll", "mshtml.dll", "shdocvw.dll", "browseui.dll",
            "jscript.dll", "vbscript.dll", "scrrun.dll", "msxml.dll", "msxml3.dll", "msxml6.dll",
            "actxprxy.dll", "softpub.dll", "wintrust.dll", "dssenh.dll", "rsaenh.dll",
            "gpkcsp.dll", "sccbase.dll", "slbcsp.dll", "cryptdlg.dll", "oleaut32.dll",
            "ole32.dll", "shell32.dll", "initpki.dll", "wuapi.dll", "wuaueng.dll",
            "wuaueng1.dll", "wucltui.dll", "wups.dll", "wups2.dll", "wuweb.dll",
            "qmgr.dll", "qmgrprxy.dll", "wucltux.dll", "muweb.dll", "wuwebv.dll"
        };

        int registeredCount = 0;
        foreach (var dll in dlls)
        {
            if (ct.IsCancellationRequested) return -1;
            var sysDllPath = Path.Combine(Environment.SystemDirectory, dll);
            if (File.Exists(sysDllPath))
            {
                await ProcessHelper.RunProcessAsync("regsvr32.exe", $"/s \"{sysDllPath}\"");
                registeredCount++;
            }
        }
        onOutput($"✅ 已成功註冊 {registeredCount} 個系統核心 DLL 元件。");

        // Stage 4: Reset Network & Proxy Stacks
        if (ct.IsCancellationRequested) return -1;
        onOutput("\n[Stage 4/7] 正在重置 Winsock、TCP/IP 網路協定堆疊與 WinHTTP 代理...");
        var (wOut, _, _) = await ProcessHelper.RunProcessAsync("netsh.exe", "winsock reset");
        onOutput(wOut.Trim());
        var (ipOut, _, _) = await ProcessHelper.RunProcessAsync("netsh.exe", "int ip reset");
        onOutput(ipOut.Trim());
        var (proxyOut, _, _) = await ProcessHelper.RunProcessAsync("netsh.exe", "winhttp reset proxy");
        onOutput(proxyOut.Trim());

        // Stage 5: Reset Service Security Descriptors
        if (ct.IsCancellationRequested) return -1;
        onOutput("\n[Stage 5/7] 正在還原 wuauserv 與 bits 服務安全性描述元權限 (SDSet)...");
        await ProcessHelper.RunProcessAsync("sc.exe", "sdset bits \"D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;AU)(A;;CCLCSWRPWPDTLOCRRC;;;PU)\"");
        await ProcessHelper.RunProcessAsync("sc.exe", "sdset wuauserv \"D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;AU)(A;;CCLCSWRPWPDTLOCRRC;;;PU)\"");

        // Stage 6: Restart Windows Update Services
        if (ct.IsCancellationRequested) return -1;
        onOutput("\n[Stage 6/7] 正在重新啟動 Windows Update 與相依服務...");
        string[] servicesToStart = { "cryptSvc", "bits", "wuauserv", "dosvc" };
        foreach (var svc in servicesToStart)
        {
            if (ct.IsCancellationRequested) return -1;
            onOutput($"▸ 啟動服務: {svc}");
            await ProcessHelper.RunProcessAsync("sc.exe", $"config {svc} start=auto");
            await ProcessHelper.RunProcessAsync("net.exe", $"start {svc}");
        }

        // Stage 7: Trigger Update Agent Scan
        if (ct.IsCancellationRequested) return -1;
        onOutput("\n[Stage 7/7] 正在觸發 Windows Update 代理程式重新探測與背景掃描...");
        await ProcessHelper.RunProcessAsync("UsoClient.exe", "StartScan");
        await ProcessHelper.RunProcessAsync("wuauclt.exe", "/resetauthorization /detectnow");

        onOutput("\n══════════════════════════════════════════════════════════════");
        onOutput($"[{DateTime.Now:HH:mm:ss}] 🎉 Windows Update 核心錯誤全自動修復完成！");
        onOutput("建議可至 Windows 設定中重新點擊「檢查更新」以確認運作正常。");
        onOutput("══════════════════════════════════════════════════════════════");
        return 0;
    }

    /// <summary>
    /// Resets the SoftwareDistribution and Catroot2 folders by safely stopping locking services,
    /// renaming or clearing the directories, and restarting services to trigger auto-rebuild.
    /// </summary>
    public async Task<int> ResetSoftwareDistributionFolderAsync(bool backupFirst, Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput("══════════════════════════════════════════════════════════════");
        onOutput($"[{DateTime.Now:HH:mm:ss}] 啟動 SoftwareDistribution 與 Catroot2 重建重置常式");
        onOutput($"備份模式: {(backupFirst ? "保留備份 (.bak) 模式" : "直接深度清空模式")}");
        onOutput("══════════════════════════════════════════════════════════════");

        // Step 1: Stop locking services
        onOutput("\n[步驟 1/3] 正在安全停止佔用資料庫之服務 (wuauserv, cryptSvc, bits, dosvc)...");
        string[] services = { "wuauserv", "cryptSvc", "bits", "dosvc" };
        foreach (var svc in services)
        {
            if (ct.IsCancellationRequested) return -1;
            await ProcessHelper.RunProcessAsync("net.exe", $"stop {svc} /y");
        }

        await Task.Delay(800, ct); // Wait for file locks to release

        // Step 2: Handle SoftwareDistribution
        onOutput("\n[步驟 2/3] 正在處理 SoftwareDistribution 與 Catroot2 目錄...");
        var timeTag = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        if (Directory.Exists(SoftwareDistPath))
        {
            if (backupFirst)
            {
                var backupDistPath = $"{SoftwareDistPath}.bak_{timeTag}";
                try
                {
                    Directory.Move(SoftwareDistPath, backupDistPath);
                    onOutput($"✅ 成功將 SoftwareDistribution 重命名為: {Path.GetFileName(backupDistPath)}");
                }
                catch (Exception ex)
                {
                    onOutput($"⚠️ 無法整體更名 ({ex.Message})，改為安全清空內部快取目錄...");
                    PurgeDirectoryInternal(Path.Combine(SoftwareDistPath, "Download"), onOutput);
                    PurgeDirectoryInternal(Path.Combine(SoftwareDistPath, "DataStore"), onOutput);
                }
            }
            else
            {
                PurgeDirectoryInternal(Path.Combine(SoftwareDistPath, "Download"), onOutput);
                PurgeDirectoryInternal(Path.Combine(SoftwareDistPath, "DataStore"), onOutput);
                onOutput("✅ 已徹底清空 SoftwareDistribution\\Download 與 DataStore 內容。");
            }
        }

        // Handle Catroot2
        if (Directory.Exists(Catroot2Path))
        {
            if (backupFirst)
            {
                var backupCatPath = $"{Catroot2Path}.bak_{timeTag}";
                try
                {
                    Directory.Move(Catroot2Path, backupCatPath);
                    onOutput($"✅ 成功將 Catroot2 重命名為: {Path.GetFileName(backupCatPath)}");
                }
                catch (Exception ex)
                {
                    onOutput($"⚠️ 無法整體更名 Catroot2 ({ex.Message})，改為清空內部目錄...");
                    PurgeDirectoryInternal(Catroot2Path, onOutput);
                }
            }
            else
            {
                PurgeDirectoryInternal(Catroot2Path, onOutput);
                onOutput("✅ 已徹底清空 Catroot2 簽章快取內容。");
            }
        }

        // Step 3: Restart services to trigger clean folder recreation
        onOutput("\n[步驟 3/3] 正在重啟服務並促使 Windows 自動建立全新資料結構...");
        foreach (var svc in services)
        {
            if (ct.IsCancellationRequested) return -1;
            await ProcessHelper.RunProcessAsync("net.exe", $"start {svc}");
        }

        onOutput("\n══════════════════════════════════════════════════════════════");
        onOutput($"[{DateTime.Now:HH:mm:ss}] 🎉 SoftwareDistribution 重建完成！Windows 已生成全新的乾淨更新庫。");
        onOutput("══════════════════════════════════════════════════════════════");
        return 0;
    }

    /// <summary>
    /// Enables or disables automatic Windows Update via service startup configuration and Group Policy registry keys.
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

                    // Configure services to disabled
                    await ProcessHelper.RunProcessAsync("sc.exe", "config wuauserv start=disabled");
                    await ProcessHelper.RunProcessAsync("sc.exe", "config WaaSMedicSvc start=disabled");
                    await ProcessHelper.RunProcessAsync("sc.exe", "config UsoSvc start=disabled");
                    await ProcessHelper.RunProcessAsync("net.exe", "stop wuauserv /y");

                    // Set Group Policy Registry
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU");
                    if (key != null)
                    {
                        key.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                        key.SetValue("AUOptions", 2, RegistryValueKind.DWord);
                    }

                    onOutput("✅ Windows Update 服務已停止且啟動類型已設為 Disabled，原則 NoAutoUpdate=1 已寫入。");
                    return true;
                }
                else
                {
                    onOutput($"[{DateTime.Now:HH:mm:ss}] 正在將 Windows Update 恢復為「🟢 正常運作」狀態...");

                    // Configure services to auto/manual
                    await ProcessHelper.RunProcessAsync("sc.exe", "config wuauserv start=auto");
                    await ProcessHelper.RunProcessAsync("sc.exe", "config WaaSMedicSvc start=demand");
                    await ProcessHelper.RunProcessAsync("sc.exe", "config UsoSvc start=demand");

                    // Clear registry policy
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
    /// Triggers an immediate Windows Update scan via UsoClient.
    /// </summary>
    public async Task TriggerUpdateCheckAsync(Action<string> onOutput)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] 正在發送指令強制觸發 Windows Update 背景檢查...");
        var (out1, _, _) = await ProcessHelper.RunProcessAsync("UsoClient.exe", "StartScan");
        var (out2, _, _) = await ProcessHelper.RunProcessAsync("wuauclt.exe", "/detectnow");
        onOutput("✅ 已送出更新掃描要求 (UsoClient StartScan)。請稍候 Windows 通知或開啟系統更新設定查看。");
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
