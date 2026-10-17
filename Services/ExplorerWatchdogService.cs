using System.Diagnostics;
using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Background watchdog service that monitors whether Windows Explorer (explorer.exe)
/// has crashed or been unexpectedly terminated, and automatically restarts it to restore
/// the desktop shell and taskbar. Supports temporary suppression during intentional restarts.
/// </summary>
public sealed class ExplorerWatchdogService
{
    private static readonly Lazy<ExplorerWatchdogService> _instance = new(() => new ExplorerWatchdogService());
    public static ExplorerWatchdogService Instance => _instance.Value;

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private DateTime _suppressUntilUtc = DateTime.MinValue;
    private readonly object _gate = new();

    public bool IsEnabled { get; set; } = true;
    public bool IsExplorerAlive { get; private set; } = true;
    public int AutoRecoveryCount { get; private set; }
    public string LastRecoveryTimeText { get; private set; } = "尚未觸發自動救援 (0 次)";

    public event Action? StatusChanged;

    private ExplorerWatchdogService()
    {
        if (IsWatchdogGloballyDisabled())
        {
            IsEnabled = false;
        }
    }

    /// <summary>
    /// Checks whether the Explorer watchdog service is disabled by registry policy
    /// (HKCU\Software\DiskMaster or HKLM\Software\DiskMaster -> EnableExplorerWatchdog == 0).
    /// </summary>
    public static bool IsWatchdogGloballyDisabled()
    {
        try
        {
            using var cuKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\DiskMaster");
            if (cuKey != null)
            {
                var val = cuKey.GetValue("EnableExplorerWatchdog");
                if (val is int intVal && intVal == 0) return true;
                if (val is string strVal && (strVal == "0" || strVal.Equals("false", StringComparison.OrdinalIgnoreCase))) return true;
            }
        }
        catch { }

        try
        {
            using var lmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\DiskMaster");
            if (lmKey != null)
            {
                var val = lmKey.GetValue("EnableExplorerWatchdog");
                if (val is int intVal && intVal == 0) return true;
                if (val is string strVal && (strVal == "0" || strVal.Equals("false", StringComparison.OrdinalIgnoreCase))) return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Persists the watchdog enabled/disabled preference to the current user registry.
    /// </summary>
    public static void SetWatchdogGloballyDisabled(bool disabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\DiskMaster");
            key?.SetValue("EnableExplorerWatchdog", disabled ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch { }
    }

    /// <summary>
    /// Starts the background monitoring loop if not already running.
    /// If disabled via registry policy, bypasses start unless force is true.
    /// </summary>
    public void Start(bool force = false)
    {
        if (!force && IsWatchdogGloballyDisabled())
        {
            IsEnabled = false;
            DebugLogService.Instance.Info("ExplorerWatchdogService bypassed by registry policy (EnableExplorerWatchdog = 0).", "ExplorerWatchdog");
            return;
        }

        if (force)
        {
            SetWatchdogGloballyDisabled(false);
            IsEnabled = true;
        }

        lock (_gate)
        {
            if (_monitorTask != null && !_monitorTask.IsCompleted)
                return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _monitorTask = Task.Run(() => MonitorLoopAsync(token), token);
            DebugLogService.Instance.Info("ExplorerWatchdogService started.", "ExplorerWatchdog");
        }
    }

    /// <summary>
    /// Stops the background monitoring loop (called on explicit app exit).
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            _monitorTask = null;
        }
    }

    /// <summary>
    /// Temporarily suppresses automatic Explorer recovery during intentional shell restarts
    /// (e.g., Icon Cache Rebuild or Context Menu Switch).
    /// </summary>
    public void SuppressAutoRestartFor(TimeSpan duration)
    {
        var until = DateTime.UtcNow.Add(duration);
        lock (_gate)
        {
            if (until > _suppressUntilUtc)
                _suppressUntilUtc = until;
        }
    }

    /// <summary>
    /// Checks whether any explorer.exe process is currently running.
    /// </summary>
    public static bool CheckExplorerRunning()
    {
        try
        {
            var procs = Process.GetProcessesByName("explorer");
            bool running = procs.Length > 0;
            foreach (var p in procs)
            {
                try { p.Dispose(); } catch { }
            }
            return running;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Performs a clean, controlled restart of explorer.exe while suppressing the watchdog.
    /// </summary>
    public async Task<(bool Success, string Message)> RestartExplorerManuallyAsync()
    {
        SuppressAutoRestartFor(TimeSpan.FromSeconds(8));
        return await Task.Run(async () =>
        {
            try
            {
                try
                {
                    await ProcessHelper.RunProcessAsync("taskkill.exe", "/f /im explorer.exe");
                }
                catch { }

                await Task.Delay(700);
                LaunchExplorerShell();
                await Task.Delay(900);

                IsExplorerAlive = CheckExplorerRunning();
                NotifyStatusChanged();
                return (true, "✅ Windows 檔案總管 (explorer.exe) 已安全重新啟動！");
            }
            catch (Exception ex)
            {
                LaunchExplorerShell();
                return (false, $"❌ 重啟檔案總管發生錯誤: {ex.Message}");
            }
        });
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(2500));
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(token))
                    break;

                bool alive = CheckExplorerRunning();
                bool wasAlive = IsExplorerAlive;
                IsExplorerAlive = alive;

                if (!alive && IsEnabled)
                {
                    DateTime suppressUntil;
                    lock (_gate) { suppressUntil = _suppressUntilUtc; }

                    if (DateTime.UtcNow >= suppressUntil)
                    {
                        // Double-check after 800ms to avoid racing a normal shell restart
                        await Task.Delay(800, token);
                        lock (_gate) { suppressUntil = _suppressUntilUtc; }

                        if (DateTime.UtcNow >= suppressUntil && !CheckExplorerRunning())
                        {
                            DebugLogService.Instance.Warning("Detected explorer.exe termination. Auto-restarting Windows shell...", "ExplorerWatchdog");
                            LaunchExplorerShell();
                            AutoRecoveryCount++;
                            LastRecoveryTimeText = $"上次自動救援: {DateTime.Now:HH:mm:ss} (累計 {AutoRecoveryCount} 次)";
                            await Task.Delay(1200, token);
                            IsExplorerAlive = CheckExplorerRunning();
                            NotifyStatusChanged();
                            continue;
                        }
                    }
                }

                if (wasAlive != IsExplorerAlive)
                {
                    NotifyStatusChanged();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                DebugLogService.Instance.Debug($"ExplorerWatchdog tick error: {ex.Message}", "ExplorerWatchdog");
            }
        }
    }

    private static void LaunchExplorerShell()
    {
        try
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var explorerPath = Path.Combine(winDir, "explorer.exe");
            Process.Start(new ProcessStartInfo
            {
                FileName = File.Exists(explorerPath) ? explorerPath : "explorer.exe",
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c start explorer.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch { }
        }
    }

    private void NotifyStatusChanged()
    {
        try { StatusChanged?.Invoke(); } catch { }
    }
}
