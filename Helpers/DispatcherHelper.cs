using Microsoft.UI.Dispatching;

namespace DiskMasterWinUI.Helpers;

public static class DispatcherHelper
{
    private static DispatcherQueue? _uiDispatcher;

    /// <summary>
    /// The UI dispatcher queue. Kept volatile: it is written once during window creation and
    /// read from background threads, and a stale null read would run "UI" work on a worker thread.
    /// </summary>
    public static DispatcherQueue? UIDispatcher
    {
        get => Volatile.Read(ref _uiDispatcher);
        set => Volatile.Write(ref _uiDispatcher, value);
    }

    public static void RunOnUIThread(Action action)
    {
        if (action == null) return;

        var dispatcher = Volatile.Read(ref _uiDispatcher);
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            if (!dispatcher.TryEnqueue(() => Invoke(action)))
            {
                // The queue is shutting down. Running the action inline here would touch UI from
                // a background thread, so report the dropped update instead of doing that.
                Report($"UI 更新已捨棄（DispatcherQueue 無法排入）: {action.Method.DeclaringType?.Name}.{action.Method.Name}");
            }
        }
        else
        {
            Invoke(action);
        }
    }

    private static void Invoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            // The old code swallowed this, so a failing UI update left a permanently stale panel
            // with no trace anywhere (debug logging is off by default).
            try { DiskMasterWinUI.Services.DebugLogService.Instance.Error("UI 更新失敗 (DispatcherHelper.RunOnUIThread)", ex, "UI"); } catch { }
            try { GlobalExceptionHandler.ReportException(ex, "[UI 更新失敗] DispatcherHelper.RunOnUIThread"); } catch { }
        }
    }

    private static void Report(string message)
    {
        try { DiskMasterWinUI.Services.DebugLogService.Instance.Warning(message, "UI"); } catch { }
    }
}
