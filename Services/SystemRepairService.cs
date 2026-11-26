using System.Diagnostics;
using System.Text;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public class SystemRepairService
{
    private static async Task<int> RunStreamingProcessAsync(
        string fileName,
        string arguments,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        var result = await DiskMasterWinUI.Helpers.StreamingProcessRunner.RunAsync(
            "system-repair",
            fileName,
            arguments,
            onOutputLine,
            cancellationToken);
        return result.ExitCode;
    }

    public async Task<int> RunSfcScanNowAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting sfc /scannow (System File Checker)...");
        var exitCode = await RunStreamingProcessAsync("sfc.exe", "/scannow", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] sfc /scannow finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunSfcVerifyOnlyAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting sfc /verifyonly (Integrity Check)...");
        var exitCode = await RunStreamingProcessAsync("sfc.exe", "/verifyonly", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] sfc /verifyonly finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismCheckHealthAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting dism /online /cleanup-image /checkhealth...");
        var exitCode = await RunStreamingProcessAsync("dism.exe", "/online /cleanup-image /checkhealth", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] dism /checkhealth finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismScanHealthAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting dism /online /cleanup-image /scanhealth...");
        var exitCode = await RunStreamingProcessAsync("dism.exe", "/online /cleanup-image /scanhealth", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] dism /scanhealth finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismRestoreHealthAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting dism /online /cleanup-image /restorehealth...");
        var exitCode = await RunStreamingProcessAsync("dism.exe", "/online /cleanup-image /restorehealth", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] dism /restorehealth finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismComponentCleanupAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting dism /online /cleanup-image /startcomponentcleanup...");
        var exitCode = await RunStreamingProcessAsync("dism.exe", "/online /cleanup-image /startcomponentcleanup", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] dism /startcomponentcleanup finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<bool> RunOneClickFullHealthRepairAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        var result = await RunFullDismSfcSequenceAsync(
            runScanHealth: true,
            runCheckHealth: true,
            runRestoreHealth: true,
            repairSource: null,
            runSfc: true,
            onOutput: onOutput,
            ct: ct);
        return result.Success;
    }

    /// <summary>
    /// Executes the Microsoft-recommended DISM &amp; SFC component store repair pipeline:
    /// ScanHealth -> CheckHealth -> RestoreHealth (optional /Source) -> sfc /scannow,
    /// while detecting critical CBS error codes such as 0x800F0831 and 0x800F081F.
    /// </summary>
    public async Task<DismSfcRepairResult> RunFullDismSfcSequenceAsync(
        bool runScanHealth,
        bool runCheckHealth,
        bool runRestoreHealth,
        string? repairSource,
        bool runSfc,
        Action<string> onOutput,
        CancellationToken ct = default)
    {
        string? detectedError = null;
        bool rebootRecommended = false;
        int lastExitCode = 0;
        bool anyFailed = false;

        void InterceptOutput(string line)
        {
            onOutput(line);
            if (!string.IsNullOrWhiteSpace(line))
            {
                if (line.Contains("0x800f0831", StringComparison.OrdinalIgnoreCase) || line.Contains("CBS_E_STORE_CORRUPTION", StringComparison.OrdinalIgnoreCase))
                    detectedError = "0x800F0831";
                else if (line.Contains("0x800f081f", StringComparison.OrdinalIgnoreCase))
                    detectedError ??= "0x800F081F";
                else if (line.Contains("0x800f0906", StringComparison.OrdinalIgnoreCase))
                    detectedError ??= "0x800F0906";
                else if (line.Contains("successfully repaired", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("成功修復", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("restart", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("重新啟動", StringComparison.OrdinalIgnoreCase))
                    rebootRecommended = true;
            }
        }

        int totalSteps = (runScanHealth ? 1 : 0) + (runCheckHealth ? 1 : 0) + (runRestoreHealth ? 1 : 0) + (runSfc ? 1 : 0);
        int currentStep = 0;

        onOutput("══════════════════════════════════════════════════════");
        onOutput($"[{DateTime.Now:HH:mm:ss}] 啟動 Windows 元件存放區與系統檔案修復流程 (DISM + SFC)");
        onOutput("══════════════════════════════════════════════════════");

        if (runScanHealth)
        {
            if (ct.IsCancellationRequested) return new DismSfcRepairResult { Success = false, LastExitCode = -1, Summary = "作業已取消" };
            currentStep++;
            onOutput($"\n[Step {currentStep}/{totalSteps}] DISM /Online /Cleanup-Image /ScanHealth");
            lastExitCode = await RunDismScanHealthAsync(InterceptOutput, ct);
            if (lastExitCode == 3010) rebootRecommended = true;
            else if (lastExitCode != 0) anyFailed = true;
        }

        if (runCheckHealth)
        {
            if (ct.IsCancellationRequested) return new DismSfcRepairResult { Success = false, LastExitCode = -1, Summary = "作業已取消" };
            currentStep++;
            onOutput($"\n[Step {currentStep}/{totalSteps}] DISM /Online /Cleanup-Image /CheckHealth");
            lastExitCode = await RunDismCheckHealthAsync(InterceptOutput, ct);
            if (lastExitCode == 3010) rebootRecommended = true;
            else if (lastExitCode != 0) anyFailed = true;
        }

        if (runRestoreHealth)
        {
            if (ct.IsCancellationRequested) return new DismSfcRepairResult { Success = false, LastExitCode = -1, Summary = "作業已取消" };
            currentStep++;
            if (!string.IsNullOrWhiteSpace(repairSource))
            {
                onOutput($"\n[Step {currentStep}/{totalSteps}] DISM /Online /Cleanup-Image /RestoreHealth /Source:\"{repairSource}\" /LimitAccess");
                lastExitCode = await RunDismRestoreHealthWithSourceAsync(repairSource.Trim(), InterceptOutput, ct);
            }
            else
            {
                onOutput($"\n[Step {currentStep}/{totalSteps}] DISM /Online /Cleanup-Image /RestoreHealth");
                lastExitCode = await RunDismRestoreHealthAsync(InterceptOutput, ct);
            }
            if (lastExitCode == 3010) rebootRecommended = true;
            else if (lastExitCode != 0) anyFailed = true;
        }

        if (runSfc)
        {
            if (ct.IsCancellationRequested) return new DismSfcRepairResult { Success = false, LastExitCode = -1, Summary = "作業已取消" };
            currentStep++;
            onOutput($"\n[Step {currentStep}/{totalSteps}] sfc /scannow (System File Checker)");
            lastExitCode = await RunSfcScanNowAsync(InterceptOutput, ct);
            if (lastExitCode == 3010) rebootRecommended = true;
            else if (lastExitCode != 0) anyFailed = true;
        }

        if (detectedError == null && lastExitCode == unchecked((int)0x800F0831)) detectedError = "0x800F0831";
        if (detectedError == null && lastExitCode == unchecked((int)0x800F081F)) detectedError = "0x800F081F";

        bool success = !anyFailed;
        string summary = success
            ? (rebootRecommended ? "DISM / SFC 修復完成，建議重新啟動電腦套用變更。" : "DISM / SFC 修復完成，系統元件狀態健康。")
            : detectedError != null
                ? $"修復回報錯誤 {detectedError} (ExitCode={lastExitCode})：請掃描 CBS.log 找出缺失套件並從 Microsoft Update 目錄手動安裝，或指定本機 WIM/ESD 來源。"
                : $"修復流程結束但部分步驟回傳錯誤碼 ({lastExitCode})，請檢視下方終端機輸出。";

        onOutput("\n══════════════════════════════════════════════════════");
        onOutput($"[{DateTime.Now:HH:mm:ss}] {summary}");
        onOutput("══════════════════════════════════════════════════════");

        return new DismSfcRepairResult
        {
            Success = success,
            LastExitCode = lastExitCode,
            RebootRecommended = rebootRecommended || success,
            DetectedErrorCode = detectedError,
            Summary = summary
        };
    }
    // ── Phase 2: Advanced DISM Operations ──

    public async Task<int> RunDismAnalyzeComponentStoreAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting dism /online /cleanup-image /analyzecomponentstore...");
        var exitCode = await RunStreamingProcessAsync("dism.exe", "/online /cleanup-image /analyzecomponentstore", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] dism /analyzecomponentstore finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismCleanupResetBaseAsync(Action<string> onOutput, CancellationToken ct = default)
        => await RunDismResetBaseAsync(onOutput, ct);

    public async Task<int> RunDismRestoreHealthWithSourceAsync(string sourcePath, Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting dism /online /cleanup-image /restorehealth /source:\"{sourcePath}\" /limitaccess...");
        var args = $"/online /cleanup-image /restorehealth /source:\"{sourcePath}\" /limitaccess";
        var exitCode = await RunStreamingProcessAsync("dism.exe", args, onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] dism /restorehealth with source finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismOfflineRestoreHealthAsync(string imagePath, Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting dism /image:\"{imagePath}\" /cleanup-image /restorehealth...");
        var args = $"/image:\"{imagePath}\" /cleanup-image /restorehealth";
        var exitCode = await RunStreamingProcessAsync("dism.exe", args, onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] Offline DISM RestoreHealth finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunSfcOfflineAsync(string bootDir, string winDir, Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting offline SFC: /offbootdir={bootDir} /offwindir={winDir}...");
        var args = $"/scannow /offbootdir={bootDir} /offwindir={winDir}";
        var exitCode = await RunStreamingProcessAsync("sfc.exe", args, onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] Offline SFC finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismGetPackagesAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Enumerating installed packages...");
        var exitCode = await RunStreamingProcessAsync("dism.exe", "/online /get-packages /format:table", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] Package enumeration finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismRemovePackageAsync(string packageName, Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Removing package: {packageName}...");
        var args = $"/online /remove-package /packagename:\"{packageName}\" /norestart";
        var exitCode = await RunStreamingProcessAsync("dism.exe", args, onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] Package removal finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismGetFeaturesAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Enumerating Windows features...");
        var exitCode = await RunStreamingProcessAsync("dism.exe", "/online /get-features /format:table", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] Feature enumeration finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismEnableFeatureAsync(string featureName, string? sourcePath, Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Enabling feature: {featureName}...");
        var sourceArg = string.IsNullOrWhiteSpace(sourcePath) ? "" : $" /source:\"{sourcePath}\"";
        var args = $"/online /enable-feature /featurename:\"{featureName}\" /all{sourceArg} /norestart";
        var exitCode = await RunStreamingProcessAsync("dism.exe", args, onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] Feature enable finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismDisableFeatureAsync(string featureName, Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Disabling feature: {featureName}...");
        var args = $"/online /disable-feature /featurename:\"{featureName}\" /norestart";
        var exitCode = await RunStreamingProcessAsync("dism.exe", args, onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] Feature disable finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismExportDriversAsync(string destinationDir, Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Exporting all OEM drivers to {destinationDir}...");
        var args = $"/online /export-driver /destination:\"{destinationDir}\"";
        var exitCode = await RunStreamingProcessAsync("dism.exe", args, onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] Driver export finished with exit code {exitCode}.");
        return exitCode;
    }

    public async Task<int> RunDismResetBaseAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] 啟動 WinSxS 元件存放區深度重設基線 (ResetBase 清理被取代的舊版更新)...");
        var args = "/online /cleanup-image /startcomponentcleanup /resetbase";
        var exitCode = await RunStreamingProcessAsync("dism.exe", args, onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] WinSxS 重設基線清理完成 (結束代碼: {exitCode})。");
        return exitCode;
    }

    public async Task<(List<WindowsFeatureItem> Items, string RawOutput, string Error, int ExitCode)> GetFeaturesStructuredAsync()
    {
        var (output, err, exitCode) = await ProcessHelper.RunProcessAsync("dism.exe", "/online /get-features /format:table");
        var items = OutputParser.ParseFeatures(output);
        if (items.Count == 0 && exitCode == 0)
        {
            var (fbOutput, fbErr, fbCode) = await ProcessHelper.RunProcessAsync("dism.exe", "/online /get-features");
            if (fbCode == 0)
            {
                items = OutputParser.ParseFeatures(fbOutput);
                output = fbOutput;
            }
        }
        return (items, output, err, exitCode);
    }

    public async Task<(List<WindowsPackageItem> Items, string RawOutput, string Error, int ExitCode)> GetPackagesStructuredAsync()
    {
        var (output, err, exitCode) = await ProcessHelper.RunProcessAsync("dism.exe", "/online /get-packages /format:table");
        var items = OutputParser.ParsePackages(output);
        if (items.Count == 0 && exitCode == 0)
        {
            var (fbOutput, fbErr, fbCode) = await ProcessHelper.RunProcessAsync("dism.exe", "/online /get-packages");
            if (fbCode == 0)
            {
                items = OutputParser.ParsePackages(fbOutput);
                output = fbOutput;
            }
        }
        return (items, output, err, exitCode);
    }
}
