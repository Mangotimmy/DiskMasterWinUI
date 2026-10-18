using System.Diagnostics;
using Microsoft.UI.Dispatching;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Lightweight UI-thread watchdog. A DispatcherQueueTimer updates a heartbeat counter every
/// 500 ms; a background timer watches that counter. The background half is essential: if the
/// UI thread is starved by a synchronous loop, the dispatcher timer can never fire, so it
/// cannot report its own stall. The background watchdog logs the incident while it is happening.
/// </summary>
public static class UiHeartbeatMonitor
{
    private const int IntervalMs = 500;
    private const int StallThresholdMs = 2000;
    private const int BackgroundStallThresholdMs = 3000;
    private const int WarningCooldownMs = 10_000;

    private static DispatcherQueueTimer? _timer;
    private static System.Threading.Timer? _backgroundWatchdog;
    private static long _lastUiTick = Environment.TickCount64;
    private static long _lastWarningTick;
    private static long _lastBackgroundWarningTick;

    public static void Start(DispatcherQueue dispatcherQueue)
    {
        if (_timer != null || dispatcherQueue == null) return;

        _lastUiTick = Environment.TickCount64;
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(IntervalMs);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) =>
        {
            long now = Environment.TickCount64;
            long gap = now - _lastUiTick;
            _lastUiTick = now;

            if (gap < StallThresholdMs) return;
            if (_lastWarningTick != 0 && now - _lastWarningTick < WarningCooldownMs) return;
            _lastWarningTick = now;

            DebugLogService.Instance.Warning($"UI thread stalled for {gap} ms (last action: {DescribeLastAction()})", "UI");
        };
        _timer.Start();

        _backgroundWatchdog = new System.Threading.Timer(CheckFromBackground, null, 1000, 1000);
    }

    private static void CheckFromBackground(object? state)
    {
        long now = Environment.TickCount64;
        long gap = now - Interlocked.Read(ref _lastUiTick);
        if (gap < BackgroundStallThresholdMs) return;
        if (_lastBackgroundWarningTick != 0 && now - _lastBackgroundWarningTick < WarningCooldownMs) return;
        _lastBackgroundWarningTick = now;

        // This runs on the thread pool, so it can still write evidence while the UI thread is blocked.
        DebugLogService.Instance.Warning(
            $"UI thread unresponsive for {gap} ms (last action: {DescribeLastAction()})",
            "UI");
    }

    private static string DescribeLastAction()
    {
        try
        {
            var recent = DebugLogService.Instance.GetRecentLogItems();
            if (recent.Count > 0)
            {
                var item = recent[^1];
                return $"{item.Category}: {item.Action}";
            }
        }
        catch { }
        return "unknown";
    }
}
