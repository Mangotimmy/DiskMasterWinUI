using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Comprehensive system diagnostics exporter.
/// Collects operating system, CPU, memory, physical disks, volumes, SMART health,
/// Windows Update status, network/TCP settings, and recent crash logs into a formatted report.
/// </summary>
public class DiagnosticExportService
{
    private static readonly Lazy<DiagnosticExportService> _instance = new(() => new DiagnosticExportService());
    public static DiagnosticExportService Instance => _instance.Value;

    public async Task<string> GenerateReportContentAsync()
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;

        sb.AppendLine("══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("         DiskMaster Pro — 系統深度診斷與除錯分析報告 (Diagnostic Report)       ");
        sb.AppendLine("══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine($"產生時間 (Generated): {now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"電腦名稱 (Computer) : {Environment.MachineName}");
        sb.AppendLine($"使用者帳號 (User)   : {Environment.UserName}");
        sb.AppendLine($"系統管理員權限 (Admin): {(AdminHelper.IsRunningAsAdmin() ? "✅ 是 (Elevated)" : "❌ 否 (Standard User)")}");
        sb.AppendLine($"軟體核心版本 (App)  : {typeof(DiagnosticExportService).Assembly.GetName().Version}");
        sb.AppendLine();

        // 1. Operating System & Kernel
        sb.AppendLine("── 1. 作業系統與核心環境 (Operating System & Kernel) ──────────────────────────");
        sb.AppendLine($"OS 描述: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"核心架構: {RuntimeInformation.OSArchitecture} (Process: {RuntimeInformation.ProcessArchitecture})");
        sb.AppendLine($"系統目錄: {Environment.SystemDirectory}");
        sb.AppendLine($"開機運作時間 (Uptime): {TimeSpan.FromMilliseconds(Environment.TickCount64):d'天 'h'小時 'm'分 's'秒'}");
        sb.AppendLine($".NET 執行階段: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine();

        // 2. Hardware: CPU & Memory
        sb.AppendLine("── 2. 硬體架構與處理器／記憶體 (CPU & Physical Memory) ────────────────────────");
        sb.AppendLine($"邏輯核心數 (Logical Cores): {Environment.ProcessorCount}");
        try
        {
            var gcMem = GC.GetGCMemoryInfo();
            var totalRamGB = gcMem.TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0;
            sb.AppendLine($"實體記憶體總量 (RAM): {totalRamGB:F2} GB");
            using var currentProc = Process.GetCurrentProcess();
            sb.AppendLine($"DiskMaster 記憶體佔用: {currentProc.WorkingSet64 / 1024.0 / 1024.0:F2} MB (Threads: {currentProc.Threads.Count})");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"記憶體讀取警示: {ex.Message}");
        }
        sb.AppendLine();

        // 3. Physical Disks & S.M.A.R.T. Health
        sb.AppendLine("── 3. 實體磁碟與 S.M.A.R.T. 健康狀態 (Physical Disks & Health) ────────────────");
        try
        {
            var (diskpartOut, _, _) = await ProcessHelper.RunProcessAsync("diskpart.exe", "/s \"\" \n list disk");
            if (!string.IsNullOrWhiteSpace(diskpartOut))
            {
                sb.AppendLine("▸ DiskPart 實體磁碟摘要:");
                sb.AppendLine(diskpartOut.Trim());
            }

            var healthService = new HealthMonitorService();
            var healthList = await healthService.GetDiskHealthAsync();
            if (healthList != null && healthList.Count > 0)
            {
                sb.AppendLine("▸ S.M.A.R.T. 硬碟健康與溫度明細:");
                foreach (var d in healthList)
                {
                    sb.AppendLine($"  • [{d.DeviceId}] {d.FriendlyName} | 類型: {d.MediaType} ({d.BusType}) | 狀態: {d.HealthStatus} | 溫度: {d.TemperatureDisplay} | 容量: {d.SizeDisplay}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"磁碟資訊檢索異常: {ex.Message}");
        }
        sb.AppendLine();

        // 4. Logical Volumes & BitLocker
        sb.AppendLine("── 4. 邏輯磁碟區與檔案系統 (Logical Volumes & Storage) ─────────────────────────");
        try
        {
            var allDrives = DriveInfo.GetDrives();
            foreach (var d in allDrives)
            {
                if (!d.IsReady)
                {
                    sb.AppendLine($"  • 磁碟機 {d.Name} [未就緒或抽取式裝置]");
                    continue;
                }
                var freeGb = d.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                var totalGb = d.TotalSize / 1024.0 / 1024.0 / 1024.0;
                var pct = totalGb > 0 ? (freeGb / totalGb * 100.0) : 0;
                sb.AppendLine($"  • 磁碟機 {d.Name} [{d.VolumeLabel}] | 格式: {d.DriveFormat} | 可用: {freeGb:F1} GB / {totalGb:F1} GB ({pct:F0}% 可用)");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"磁碟區資訊檢索異常: {ex.Message}");
        }
        sb.AppendLine();

        // 5. Windows Update & Component Store
        sb.AppendLine("── 5. Windows Update 與元件儲存庫 (Windows Update & SoftwareDistribution) ──────");
        try
        {
            var wuService = new WindowsUpdateRepairService();
            var wuStatus = await wuService.GetUpdateStatusAsync();
            sb.AppendLine($"  • Windows Update 服務狀態        : {wuStatus.StatusBadge} ({wuStatus.ServiceStatus}, {wuStatus.StartupType})");
            sb.AppendLine($"  • SoftwareDistribution 資料夾容量: {wuStatus.SoftwareDistributionDisplay} ({wuStatus.SoftwareDistributionFiles} 個檔案)");
            sb.AppendLine($"  • Catroot2 資料夾容量            : {wuStatus.Catroot2Display}");
            sb.AppendLine($"  • 系統是否處於待重啟狀態 (Pending Reboot): {(wuStatus.IsRebootRequired ? "⚠️ 是 (需重啟)" : "✅ 否 (正常)")}");
            sb.AppendLine($"  • Windows Update 停用原則旗標: {(wuStatus.IsDisabled ? "🔴 已停用更新" : "🟢 正常啟用中")}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Windows Update 狀態讀取異常: {ex.Message}");
        }
        sb.AppendLine();

        // 6. Network & TCP Optimization
        sb.AppendLine("── 6. 網路堆疊與 TCP 最佳化參數 (Network & TCP Configuration) ──────────────────");
        try
        {
            var (netshOut, _, _) = await ProcessHelper.RunProcessAsync("netsh.exe", "int tcp show global");
            if (!string.IsNullOrWhiteSpace(netshOut))
            {
                sb.AppendLine(netshOut.Trim());
            }
            else
            {
                sb.AppendLine("  (無法透過 netsh 取得 TCP 參數)");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"網路設定檢索異常: {ex.Message}");
        }
        sb.AppendLine();

        // 7. Recent Application Crash & Exception Logs
        sb.AppendLine("── 7. 軟體例外防護與最近崩潰日誌 (Recent Crash & Safety Logs) ──────────────────");
        try
        {
            var crashLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskMaster_Portable", "crash.log");
            if (File.Exists(crashLogPath))
            {
                var lines = await File.ReadAllLinesAsync(crashLogPath);
                var recentLines = lines.TakeLast(30);
                sb.AppendLine($"  (來源檔案: {crashLogPath})");
                foreach (var line in recentLines)
                {
                    sb.AppendLine($"  {line}");
                }
            }
            else
            {
                sb.AppendLine("  ✅ 無未處理崩潰日誌紀錄 (系統運行穩定，無 crash.log)");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"崩潰日誌讀取異常: {ex.Message}");
        }

        sb.AppendLine();
        sb.AppendLine("══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("                           報告結束 (End of Report)                           ");
        sb.AppendLine("══════════════════════════════════════════════════════════════════════════════");

        return sb.ToString();
    }

    /// <summary>
    /// Exports the diagnostic report to a text file on Desktop or Downloads,
    /// and optionally opens the file in Notepad and Explorer.
    /// </summary>
    public async Task<string> ExportReportToFileAsync(bool autoOpenFile = true)
    {
        var content = await GenerateReportContentAsync();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"DiskMaster_Diagnostics_{timestamp}.txt";

        var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var targetDir = Directory.Exists(desktopDir)
            ? desktopDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        var filePath = Path.Combine(targetDir, fileName);
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);

        if (autoOpenFile)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        return filePath;
    }
}
