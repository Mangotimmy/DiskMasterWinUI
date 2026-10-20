using System.Text;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Detects repeated startup crashes. The app increments a marker when it starts and resets it
/// after the UI has stayed alive for a short grace period. Three consecutive failures enable
/// safe mode, which skips optional background initialization and warns the user.
/// </summary>
public static class StartupCrashGuard
{
    private static readonly object Gate = new();
    private static readonly string MarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskMaster_Portable",
        "startup_guard.txt");

    public static bool IsSafeMode { get; private set; }
    public static int ConsecutiveFailures { get; private set; }

    /// <summary>Call once at startup. Returns true when safe mode should be used.</summary>
    public static bool BeginStartup()
    {
        lock (Gate)
        {
            int failures = ReadFailures() + 1;
            ConsecutiveFailures = failures;
            IsSafeMode = failures >= 3;
            WriteFailures(failures);
            if (IsSafeMode)
            {
                DebugLogService.Instance.Warning(
                    $"Safe mode enabled after {failures} consecutive startup attempts. Optional startup work will be skipped.",
                    "CrashGuard");
            }
            return IsSafeMode;
        }
    }

    /// <summary>Reset the failure counter after the window has survived the startup grace period.</summary>
    public static void MarkStartupHealthy()
    {
        lock (Gate)
        {
            ConsecutiveFailures = 0;
            IsSafeMode = false;
            WriteFailures(0);
        }
    }

    public static async Task MarkStartupHealthyAfterDelayAsync(TimeSpan delay, CancellationToken ct = default)
    {
        try
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            MarkStartupHealthy();
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private static int ReadFailures()
    {
        try
        {
            if (!File.Exists(MarkerPath)) return 0;
            string text = File.ReadAllText(MarkerPath, Encoding.UTF8).Trim();
            return int.TryParse(text, out int value) ? Math.Clamp(value, 0, 99) : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void WriteFailures(int failures)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, failures.ToString(), Encoding.UTF8);
        }
        catch { }
    }
}
