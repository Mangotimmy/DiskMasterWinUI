using System.Diagnostics;

namespace DiskMasterWinUI.Services;

public class ChkdskService
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
                if (e.Data != null) onOutputLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) onOutputLine($"[ERROR] {e.Data}");
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

    /// <summary>Online non-destructive NTFS scan (system keeps running)</summary>
    public async Task<int> ScanAsync(string drive, Action<string> onOutput, CancellationToken ct = default)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting chkdsk {cleanDrive} /scan (Online Non-Destructive Scan)...");
        var code = await RunStreamingProcessAsync("chkdsk.exe", $"{cleanDrive} /scan", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] chkdsk /scan finished with exit code {code}.");
        return code;
    }

    /// <summary>Spot fix - takes volume offline briefly to repair issues found by /scan</summary>
    public async Task<int> SpotFixAsync(string drive, Action<string> onOutput, CancellationToken ct = default)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting chkdsk {cleanDrive} /spotfix (Quick Spot Fix)...");
        var code = await RunStreamingProcessAsync("chkdsk.exe", $"{cleanDrive} /spotfix", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] chkdsk /spotfix finished with exit code {code}.");
        return code;
    }

    /// <summary>Standard fix - locks volume and fixes filesystem errors</summary>
    public async Task<int> FixAsync(string drive, Action<string> onOutput, CancellationToken ct = default)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting chkdsk {cleanDrive} /f (Fix Filesystem Errors)...");
        var code = await RunStreamingProcessAsync("chkdsk.exe", $"{cleanDrive} /f", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] chkdsk /f finished with exit code {code}.");
        return code;
    }

    /// <summary>Deep bad sector scan with recovery - unmounts volume</summary>
    public async Task<int> FixAndRecoverAsync(string drive, Action<string> onOutput, CancellationToken ct = default)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting chkdsk {cleanDrive} /f /r /x (Deep Bad Sector Repair)...");
        onOutput("[WARNING] This will dismount the volume and may take a very long time on large disks.");
        var code = await RunStreamingProcessAsync("chkdsk.exe", $"{cleanDrive} /f /r /x", onOutput, ct);
        onOutput($"[{DateTime.Now:HH:mm:ss}] chkdsk /f /r /x finished with exit code {code}.");
        return code;
    }

    /// <summary>Query dirty bit status of a volume</summary>
    public async Task<string> QueryDirtyBitAsync(string drive)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        var (output, err, code) = await Helpers.ProcessHelper.RunProcessAsync("fsutil.exe", $"dirty query {cleanDrive}");
        return string.IsNullOrWhiteSpace(output) ? err : output.Trim();
    }

    /// <summary>Schedule chkdsk on next system reboot</summary>
    public async Task<string> ScheduleBootCheckAsync(string drive)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        var (output, err, code) = await Helpers.ProcessHelper.RunProcessAsync("chkntfs.exe", $"/c {cleanDrive}");
        return string.IsNullOrWhiteSpace(output) ? err : output.Trim();
    }
}
