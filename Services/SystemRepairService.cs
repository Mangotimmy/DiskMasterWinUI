using System.Diagnostics;
using System.Text;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public class SystemRepairService
{
    private async Task<int> RunStreamingProcessAsync(
        string fileName,
        string arguments,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        var encoding = DiskMasterWinUI.Helpers.ProcessHelper.GetConsoleEncoding();
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    onOutputLine(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    onOutputLine($"[ERROR] {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var reg = cancellationToken.Register(() =>
            {
                try { process.Kill(true); } catch { }
            });

            await process.WaitForExitAsync(cancellationToken);
            process.WaitForExit(200);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            onOutputLine("[INFO] Operation cancelled by user.");
            return -1;
        }
        catch (Exception ex)
        {
            onOutputLine($"[EXCEPTION] Failed to execute {fileName}: {ex.Message}");
            return -1;
        }
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
        onOutput("══════════════════════════════════════════════════════");
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting 1-Click Complete Windows Health Repair Routine");
        onOutput("Step 1/2: DISM /Online /Cleanup-Image /RestoreHealth");
        onOutput("══════════════════════════════════════════════════════");

        var dismCode = await RunDismRestoreHealthAsync(onOutput, ct);
        if (ct.IsCancellationRequested) return false;

        onOutput("\n══════════════════════════════════════════════════════");
        onOutput("Step 2/2: SFC /scannow (System File Checker)");
        onOutput("══════════════════════════════════════════════════════");

        var sfcCode = await RunSfcScanNowAsync(onOutput, ct);

        onOutput("\n══════════════════════════════════════════════════════");
        onOutput($"[{DateTime.Now:HH:mm:ss}] Full System Health Repair Complete! (DISM: {dismCode}, SFC: {sfcCode})");
        onOutput("══════════════════════════════════════════════════════");
        return dismCode == 0 && sfcCode == 0;
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
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting dism /online /cleanup-image /startcomponentcleanup /resetbase...");
        onOutput("[WARNING] After /ResetBase, previously installed updates cannot be uninstalled.");
        var exitCode = await RunStreamingProcessAsync("dism.exe", "/online /cleanup-image /startcomponentcleanup /resetbase", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] dism /resetbase finished with exit code {exitCode}.");
        return exitCode;
    }

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
