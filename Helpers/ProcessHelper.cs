using System.Diagnostics;
using System.Globalization;
using System.Text;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Helpers;

public static class ProcessHelper
{
    static ProcessHelper()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch { }
    }

    public static Encoding GetConsoleEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var oemCp = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            if (oemCp > 0)
            {
                return Encoding.GetEncoding(oemCp);
            }
        }
        catch { }

        return Console.OutputEncoding ?? Encoding.Default;
    }

    public static async Task<(string Output, string Error, int ExitCode)> RunProcessAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        DebugLogService.Instance.LogRequest("ProcessHelper", $"啟動呼叫: {fileName}", arguments);

        var encoding = GetConsoleEncoding();
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                sw.Stop();
                DebugLogService.Instance.LogRequest("ProcessHelper", $"啟動失敗: {fileName}", arguments, sw.ElapsedMilliseconds, false);
                return ("", $"ERROR: Failed to launch {fileName}", -1);
            }

            using var reg = cancellationToken.Register(() =>
            {
                try { process.Kill(true); } catch { }
            });

            // True parallel asynchronous stream reading to prevent 4KB pipe buffer deadlocks
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(cancellationToken);

            // Allow brief moment for native stream buffers to flush
            process.WaitForExit(200);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            sw.Stop();
            int exitCode = process.ExitCode;
            DebugLogService.Instance.LogRequest("ProcessHelper", $"完成呼叫: {fileName} (ExitCode: {exitCode})", arguments, sw.ElapsedMilliseconds, exitCode == 0);

            return (stdout, stderr, exitCode);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            DebugLogService.Instance.LogRequest("ProcessHelper", $"取消呼叫: {fileName}", arguments, sw.ElapsedMilliseconds, false);
            return ("", "Operation cancelled by user.", -1);
        }
        catch (Exception ex)
        {
            sw.Stop();
            DebugLogService.Instance.LogRequest("ProcessHelper", $"異常失敗: {fileName} ({ex.Message})", arguments, sw.ElapsedMilliseconds, false);
            return ("", $"ERROR: {ex.Message}", -1);
        }
    }

    public static async Task<string> RunCommandAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        bool includeExitCode = false)
    {
        var (stdout, stderr, exitCode) = await RunProcessAsync(fileName, arguments, workingDirectory, cancellationToken);
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stdout)) sb.AppendLine(stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr)) sb.AppendLine($"[STDERR] {stderr.Trim()}");
        if (includeExitCode) sb.AppendLine($"[Exit Code: {exitCode}]");
        return sb.ToString();
    }
}
