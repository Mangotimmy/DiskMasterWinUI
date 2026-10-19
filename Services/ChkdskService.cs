using System.Diagnostics;
using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.Services;

public class ChkdskService
{
    private static async Task<int> RunStreamingProcessAsync(
        string fileName,
        string arguments,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default,
        Func<string, string?>? inputResponder = null,
        TimeSpan? idleTimeout = null)
    {
        var result = await DiskMasterWinUI.Helpers.StreamingProcessRunner.RunAsync(
            "chkdsk",
            fileName,
            arguments,
            onOutputLine,
            cancellationToken,
            inputResponder,
            idleTimeout);
        return result.ExitCode;
    }

    private static TimeSpan FixIdleTimeout => TimeSpan.FromMinutes(10);

    /// <summary>Online non-destructive NTFS scan (system keeps running)</summary>
    public async Task<int> ScanAsync(string drive, Action<string> onOutput, CancellationToken ct = default)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting chkdsk {cleanDrive} /scan (Online Non-Destructive Scan)...");
        var code = await RunStreamingProcessAsync("chkdsk.exe", $"{cleanDrive} /scan", onOutput, ct, idleTimeout: FixIdleTimeout);
        onOutput($"[{DateTime.Now:HH:mm:ss}] chkdsk /scan finished with exit code {code}.");
        return code;
    }

    /// <summary>Spot fix - takes volume offline briefly to repair issues found by /scan</summary>
    public async Task<int> SpotFixAsync(string drive, Action<string> onOutput, CancellationToken ct = default)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting chkdsk {cleanDrive} /spotfix (Quick Spot Fix)...");
        var code = await RunStreamingProcessAsync("chkdsk.exe", $"{cleanDrive} /spotfix", onOutput, ct, idleTimeout: FixIdleTimeout);
        onOutput($"[{DateTime.Now:HH:mm:ss}] chkdsk /spotfix finished with exit code {code}.");
        return code;
    }

    /// <summary>Standard fix - locks volume and fixes filesystem errors</summary>
    public async Task<int> FixAsync(string drive, Action<string> onOutput, CancellationToken ct = default)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting chkdsk {cleanDrive} /f (Fix Filesystem Errors)...");

        bool volumeBusy = false;
        bool promptAnswered = false;
        string? Respond(string text)
        {
            if (ChkdskPromptDetector.IsVolumeBusy(text)) volumeBusy = true;
            if (!ChkdskPromptDetector.IsSchedulePrompt(text) || promptAnswered) return null;

            promptAnswered = true;
            volumeBusy = true;
            onOutput("[INFO] chkdsk requested a scheduled boot-time check; answered N (do not schedule).");
            return ChkdskPromptDetector.DeclineResponse;
        }

        var code = await RunStreamingProcessAsync(
            "chkdsk.exe", $"{cleanDrive} /f", onOutput, ct, Respond, FixIdleTimeout);
        if (volumeBusy) code = -2;
        onOutput($"[{DateTime.Now:HH:mm:ss}] chkdsk /f finished with code {code}.");
        return code;
    }

    /// <summary>Deep bad sector scan with recovery - unmounts volume</summary>
    public async Task<int> FixAndRecoverAsync(string drive, Action<string> onOutput, CancellationToken ct = default)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting chkdsk {cleanDrive} /f /r /x (Deep Bad Sector Repair)...");
        onOutput("[WARNING] This will dismount the volume and may take a very long time on large disks.");

        bool volumeBusy = false;
        bool promptAnswered = false;
        string? Respond(string text)
        {
            if (ChkdskPromptDetector.IsVolumeBusy(text)) volumeBusy = true;
            if (!ChkdskPromptDetector.IsSchedulePrompt(text) || promptAnswered) return null;

            promptAnswered = true;
            volumeBusy = true;
            onOutput("[INFO] chkdsk requested a scheduled boot-time check; answered N (do not schedule).");
            return ChkdskPromptDetector.DeclineResponse;
        }

        var code = await RunStreamingProcessAsync(
            "chkdsk.exe", $"{cleanDrive} /f /r /x", onOutput, ct, Respond, FixIdleTimeout);
        if (volumeBusy) code = -2;
        onOutput($"[{DateTime.Now:HH:mm:ss}] chkdsk /f /r /x finished with code {code}.");
        return code;
    }

    /// <summary>Turbo online scan using maximum system resources to finish as quickly as possible</summary>
    public async Task<int> ScanWithPerfAsync(string drive, Action<string> onOutput, CancellationToken ct = default)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting chkdsk {cleanDrive} /scan /perf (High-Performance Turbo Scan)...");
        var code = await RunStreamingProcessAsync("chkdsk.exe", $"{cleanDrive} /scan /perf", onOutput, ct, idleTimeout: FixIdleTimeout);
        onOutput($"[{DateTime.Now:HH:mm:ss}] chkdsk /scan /perf finished with exit code {code}.");
        return code;
    }

    /// <summary>Offline scan and fix</summary>
    public async Task<int> OfflineScanAndFixAsync(string drive, Action<string> onOutput, CancellationToken ct = default)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        onOutput($"[{DateTime.Now:HH:mm:ss}] Starting chkdsk {cleanDrive} /offlinescanandfix...");
        var code = await RunStreamingProcessAsync("chkdsk.exe", $"{cleanDrive} /offlinescanandfix", onOutput, ct, idleTimeout: FixIdleTimeout);
        onOutput($"[{DateTime.Now:HH:mm:ss}] chkdsk /offlinescanandfix finished with exit code {code}.");
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

    /// <summary>Cancel scheduled chkdsk check on next system reboot</summary>
    public async Task<string> CancelBootCheckAsync(string drive)
    {
        var cleanDrive = drive.TrimEnd(':', '\\') + ":";
        var (output, err, code) = await Helpers.ProcessHelper.RunProcessAsync("chkntfs.exe", $"/x {cleanDrive}");
        return string.IsNullOrWhiteSpace(output) ? err : output.Trim();
    }
}
