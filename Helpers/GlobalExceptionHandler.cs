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
    private const long MaxCrashLogBytes = 2 * 1024 * 1024; // roll at 2 MB so the log cannot grow without bound
    private static readonly object _logLock = new();
    private static readonly object _lastLock = new();
    private static DispatcherQueue? _dispatcherQueue;
    private static Exception? _lastException;
    private static string _lastExceptionContext = "";

    public static event Action<Exception, string>? ExceptionReported;

    public static Exception? LastException { get { lock (_lastLock) return _lastException; } }
    public static string LastExceptionContext { get { lock (_lastLock) return _lastExceptionContext; } }

    /// <summary>
    /// Reads the last reported failure as one atomic pair, so the details shown in the UI and
    /// the exception copied to the clipboard always belong to the same report.
    /// </summary>
    public static (Exception? Exception, string Context) GetLastFailure()
    {
        lock (_lastLock) return (_lastException, _lastExceptionContext);
    }

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
        lock (_lastLock)
        {
            _lastException = ex;
            _lastExceptionContext = context;
        }

        var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}]\nException: {ex.GetType().FullName}: {ex.Message}\nStackTrace:\n{ex.StackTrace}\n\n";
        AppendCrashLog(logEntry);

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
    /// Appends a crash record under a lock, rolling the file at a fixed size, and falls back to
    /// %LOCALAPPDATA% when the application directory is not writable (Program Files, read-only
    /// portable media). Failures here are ignored on purpose: this runs inside exception handling.
    /// </summary>
    private static void AppendCrashLog(string logEntry)
    {
        lock (_logLock)
        {
            foreach (var path in GetCrashLogPaths())
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxCrashLogBytes)
                    {
                        File.Move(path, path + ".1", overwrite: true);
                    }
                    File.AppendAllText(path, logEntry);
                    return;
                }
                catch
                {
                    // Try the next candidate location.
                }
            }
        }
    }

    private static List<string> GetCrashLogPaths()
    {
        var paths = new List<string> { Path.Combine(AppContext.BaseDirectory, "crash.log") };
        try
        {
            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiskMaster_Portable");
            Directory.CreateDirectory(local);
            paths.Add(Path.Combine(local, "crash.log"));
        }
        catch { }
        return paths;
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
