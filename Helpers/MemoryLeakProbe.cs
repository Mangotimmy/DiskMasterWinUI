using System.Diagnostics;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Lightweight navigation memory probe. It records managed heap size after page switches and
/// writes a diagnostic warning when repeated navigation grows the heap substantially.
/// It never forces GC on the UI thread and never changes navigation behavior.
/// </summary>
public static class MemoryLeakProbe
{
    private const long WarningGrowthBytes = 48L * 1024 * 1024;
    private const int MinimumNavigationsBeforeWarning = 5;
    private static long _lastBytes;
    private static int _navigationCount;
    private static long _peakBytes;

    public static int NavigationCount => Volatile.Read(ref _navigationCount);
    public static long PeakBytes => Interlocked.Read(ref _peakBytes);
    public static long LastBytes => Interlocked.Read(ref _lastBytes);

    public static void RecordNavigation(string? pageTag)
    {
        long bytes = GC.GetTotalMemory(forceFullCollection: false);
        long previous = Interlocked.Exchange(ref _lastBytes, bytes);
        if (bytes > Interlocked.Read(ref _peakBytes)) Interlocked.Exchange(ref _peakBytes, bytes);

        int count = Interlocked.Increment(ref _navigationCount);
        if (previous > 0 && count >= MinimumNavigationsBeforeWarning)
        {
            long growth = bytes - previous;
            if (growth >= WarningGrowthBytes)
            {
                DebugLogService.Instance.Warning(
                    $"Navigation memory growth: {growth / (1024 * 1024)} MB after opening '{pageTag}'. " +
                    $"Managed heap: {bytes / (1024 * 1024)} MB; peak: {PeakBytes / (1024 * 1024)} MB.",
                    "MemoryProbe");
            }
        }
    }

    public static void Reset()
    {
        Interlocked.Exchange(ref _lastBytes, 0);
        Interlocked.Exchange(ref _peakBytes, 0);
        Interlocked.Exchange(ref _navigationCount, 0);
    }
}
