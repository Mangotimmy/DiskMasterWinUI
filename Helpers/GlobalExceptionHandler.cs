using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Crash-proof global exception handling and reporting service.
/// Intercepts UI thread, background tasks, and unobserved task exceptions to keep the app 100% running.
/// </summary>
public static class GlobalExceptionHandler
{
    private static DispatcherQueue? _dispatcherQueue;
    public static event Action<Exception, string>? ExceptionReported;
    public static Exception? LastException { get; private set; }
    public static string LastExceptionContext { get; private set; } = "";

    /// <summary>
    /// Initializes the dispatcher queue for safe UI invocation from any background thread.
    /// </summary>
    public static void Initialize(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    /// <summary>
    /// Reports an exception gracefully without crashing the application.
    /// </summary>
    public static void ReportException(Exception ex, string context = "一般非同步操作 (General Operation)")
    {
        LastException = ex;
        LastExceptionContext = context;

        // Log to crash.log
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}]\nException: {ex.GetType().FullName}: {ex.Message}\nStackTrace:\n{ex.StackTrace}\n\n";
            File.AppendAllText(logPath, logEntry);
        }
        catch { }

        // Dispatch to UI thread if possible
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ExceptionReported?.Invoke(ex, context);
            });
        }
        else
        {
            ExceptionReported?.Invoke(ex, context);
        }
    }

    /// <summary>
    /// Formats an exception into a human-readable and diagnostic report.
    /// </summary>
    public static string FormatExceptionDetails(Exception? ex, string context)
    {
        if (ex == null) return "未發生任何異常 (No exception recorded).";

        return $"""
═══ DiskMaster Pro 異常防護日誌 ═══
發生時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
發生環節: {context}
異常類型: {ex.GetType().FullName}
錯誤訊息: {ex.Message}

【內部詳細 StackTrace】
{ex.StackTrace}

【內部原因 InnerException】
{ex.InnerException?.ToString() ?? "無 (None)"}
═════════════════════════════════════
""";
    }

    /// <summary>
    /// Copies full diagnostic details to Windows Clipboard.
    /// </summary>
    public static void CopyToClipboard(Exception? ex, string context)
    {
        try
        {
            var text = FormatExceptionDetails(ex, context);
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch { }
    }
}
