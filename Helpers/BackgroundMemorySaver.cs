using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Trims the process working set after the window has been hidden in the tray for a while.
/// WinUI 3 + Windows App SDK keep a large resident baseline, and hiding the window releases
/// nothing by itself. This only lets Windows page out the parts of the process that are not in
/// use while the window is hidden; it never closes pages or changes app state.
/// </summary>
public static class BackgroundMemorySaver
{
    private const int FirstTrimDelayMs = 15_000;
    private const int RepeatTrimMs = 5 * 60_000;
    private const int TickMs = 5_000;

    private static DispatcherQueue? _dispatcher;
    private static DispatcherQueueTimer? _timer;
    private static long _nextTrimTick;
    private static bool _hidden;

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    public static void Initialize(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>Call when the window is hidden to the tray. The first trim runs after 15 seconds.</summary>
    public static void OnHidden()
    {
        if (_dispatcher == null) return;
        if (!SettingsService.Instance.Current.BackgroundMemorySaver) return;

        _hidden = true;
        _nextTrimTick = Environment.TickCount64 + FirstTrimDelayMs;

        if (_timer == null)
        {
            _timer = _dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(TickMs);
            _timer.IsRepeating = true;
            _timer.Tick += (_, _) => Tick();
        }
        if (!_timer.IsRunning) _timer.Start();
    }

    /// <summary>Call when the window is shown again; cancels every pending trim.</summary>
    public static void OnShown()
    {
        _hidden = false;
        if (_timer?.IsRunning == true) _timer.Stop();
    }

    /// <summary>Immediately collects garbage and empties this process's working set.</summary>
    public static void TrimNow()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            long privateBefore = process.PrivateMemorySize64;
            long wsBefore = process.WorkingSet64;

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(GetCurrentProcess());

            process.Refresh();
            long privateAfter = process.PrivateMemorySize64;
            long wsAfter = process.WorkingSet64;
            DebugLogService.Instance.Info(
                $"背景記憶體釋放 (Background memory trim): WS {wsBefore / 1048576} MB -> {wsAfter / 1048576} MB; " +
                $"Private {privateBefore / 1048576} MB -> {privateAfter / 1048576} MB",
                "Memory");
        }
        catch (Exception ex)
        {
            try { DebugLogService.Instance.Warning("背景記憶體釋放失敗: " + ex.Message, "Memory"); } catch { }
        }
    }

    private static void Tick()
    {
        if (!_hidden) return;
        if (!SettingsService.Instance.Current.BackgroundMemorySaver)
        {
            OnShown();
            return;
        }

        long now = Environment.TickCount64;
        if (now < _nextTrimTick) return;
        _nextTrimTick = now + RepeatTrimMs;
        TrimNow();
    }
}
