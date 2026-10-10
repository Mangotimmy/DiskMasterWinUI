using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.Services;

/// <summary>
/// High-performance, zero-overhead diagnostic and debug logging service for DiskMaster Pro.
/// When disabled, all logging calls return immediately on the fast path without memory allocation or I/O.
/// When enabled, logs are written to %LOCALAPPDATA%\DiskMaster_Portable\Logs\diskmaster_debug.log with automatic size rolling.
/// </summary>
public class DebugLogService
{
    private static readonly Lazy<DebugLogService> _instance = new(() => new DebugLogService());
    public static DebugLogService Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly string _logsDirectory;
    private readonly string _logFilePath;
    private const long MaxLogFileSizeBytes = 10 * 1024 * 1024; // 10 MB limit before roll
    private const int MaxRollingFiles = 5;

    public bool IsEnabled { get; set; }

    public string LogFilePath => _logFilePath;
    public string LogsDirectory => _logsDirectory;

    private DebugLogService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logsDirectory = Path.Combine(appData, "DiskMaster_Portable", "Logs");
        _logFilePath = Path.Combine(_logsDirectory, "diskmaster_debug.log");

        try
        {
            if (!Directory.Exists(_logsDirectory))
            {
                Directory.CreateDirectory(_logsDirectory);
            }
        }
        catch { }
    }

    /// <summary>
    /// Initializes the logger state from application settings.
    /// </summary>
    public void Initialize(bool isEnabled)
    {
        IsEnabled = isEnabled;
        if (IsEnabled)
        {
            Info("=== DiskMaster Pro Diagnostic Session Initialized ===", "Engine");
            Info($"Environment: {AppEnvironmentHelper.ExecutionModeDescription} | Version: {UpdateService.CurrentVersion}", "Engine");
        }
    }

    public void Debug(string message, string? category = null, [CallerMemberName] string? caller = null)
    {
        if (!IsEnabled) return;
        WriteEntry("DEBUG", category ?? caller ?? "General", message);
    }

    public void Info(string message, string? category = null)
    {
        if (!IsEnabled) return;
        WriteEntry("INFO ", category ?? "General", message);
    }

    public void Warning(string message, string? category = null)
    {
        if (!IsEnabled) return;
        WriteEntry("WARN ", category ?? "General", message);
    }

    public void Error(string message, Exception? ex = null, string? category = null)
    {
        if (!IsEnabled) return;
        var fullMessage = ex != null
            ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}"
            : message;
        WriteEntry("ERROR", category ?? "General", fullMessage);
    }

    private void WriteEntry(string level, string category, string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"[{timestamp}] [{level}] [{category}] {message}{Environment.NewLine}";

            lock (_lock)
            {
                CheckAndRollLogFile();
                File.AppendAllText(_logFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Do not crash application if logging fails (e.g. disk full or restricted permissions)
        }
    }

    private void CheckAndRollLogFile()
    {
        try
        {
            if (!File.Exists(_logFilePath)) return;

            var fi = new FileInfo(_logFilePath);
            if (fi.Length < MaxLogFileSizeBytes) return;

            // Roll existing files: log_4 -> delete, log_3 -> log_4, etc.
            for (int i = MaxRollingFiles - 1; i >= 1; i--)
            {
                var src = Path.Combine(_logsDirectory, $"diskmaster_debug_{i}.log");
                var dst = Path.Combine(_logsDirectory, $"diskmaster_debug_{i + 1}.log");
                if (File.Exists(src))
                {
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(src, dst);
                }
            }

            var firstRoll = Path.Combine(_logsDirectory, "diskmaster_debug_1.log");
            if (File.Exists(firstRoll)) File.Delete(firstRoll);
            File.Move(_logFilePath, firstRoll);
        }
        catch { }
    }

    /// <summary>
    /// Opens the logs folder in Windows File Explorer.
    /// </summary>
    public bool OpenLogsFolder()
    {
        try
        {
            if (!Directory.Exists(_logsDirectory))
            {
                Directory.CreateDirectory(_logsDirectory);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = _logsDirectory,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Opens the current debug log file using the default system text editor (e.g. Notepad).
    /// </summary>
    public bool ViewCurrentLog()
    {
        try
        {
            if (!File.Exists(_logFilePath))
            {
                lock (_lock)
                {
                    File.WriteAllText(_logFilePath, $"[INFO] New log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}", Encoding.UTF8);
                }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{_logFilePath}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clears all existing debug log files.
    /// </summary>
    public bool ClearLogs()
    {
        try
        {
            lock (_lock)
            {
                if (Directory.Exists(_logsDirectory))
                {
                    var logFiles = Directory.GetFiles(_logsDirectory, "diskmaster_debug*.log");
                    foreach (var f in logFiles)
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Retrieves current log file statistics (file size and line count).
    /// </summary>
    public (long SizeBytes, int LineCount, string FormattedText) GetLogStats()
    {
        try
        {
            if (!File.Exists(_logFilePath))
            {
                return (0, 0, "無日誌檔案 (0 KB)");
            }

            var fi = new FileInfo(_logFilePath);
            long bytes = fi.Length;
            int lines = 0;

            // Efficient line counting
            using (var reader = new StreamReader(new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                while (reader.ReadLine() != null)
                {
                    lines++;
                }
            }

            string sizeStr = bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
                _ => $"{bytes / (1024.0 * 1024.0):F2} MB"
            };

            return (bytes, lines, $"{sizeStr} ({lines:N0} 行)");
        }
        catch
        {
            return (0, 0, "讀取中...");
        }
    }
}
