using System.Diagnostics;
using System.Runtime.InteropServices;
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
    /// Resumes automatic recovery immediately by clearing any active suppression window.
    /// </summary>
    public void ResumeAutoRestart()
    {
        lock (_gate)
        {
            _suppressUntilUtc = DateTime.MinValue;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr ShellExecute(IntPtr hwnd, string lpOperation, string lpFile, string? lpParameters, string? lpDirectory, int nShowCmd);

    /// <summary>
    /// Checks whether the Windows desktop shell window (Desktop or Taskbar) is actively rendered.
    /// </summary>
    public static bool IsDesktopShellAlive()
    {
        try
        {
            var shellHwnd = GetShellWindow();
            if (shellHwnd != IntPtr.Zero) return true;

            var trayHwnd = FindWindow("Shell_TrayWnd", null);
            if (trayHwnd != IntPtr.Zero) return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether Windows Explorer (explorer.exe) is currently running in the active user session.
    /// Validates both session-specific process existence and shell window handles.
    /// </summary>
    public static bool CheckExplorerRunning()
    {
        try
        {
            // If the shell desktop window or taskbar is alive, explorer is running.
            if (IsDesktopShellAlive())
            {
                return true;
            }

            int currentSessionId;
            try
            {
                currentSessionId = Process.GetCurrentProcess().SessionId;
            }
            catch
            {
                currentSessionId = -1;
            }

            var procs = Process.GetProcessesByName("explorer");
            bool running = false;
            foreach (var p in procs)
            {
                try
                {
                    if (!p.HasExited && (currentSessionId < 0 || p.SessionId == currentSessionId))
                    {
                        running = true;
                    }
                }
                catch { }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
            return running;
        }
        catch
        {
            return IsDesktopShellAlive();
        }
    }

    /// <summary>
    /// Performs a clean, controlled restart of explorer.exe while temporarily suppressing the watchdog,
    /// then verifies that the desktop shell successfully recovers before returning.
    /// </summary>
    public async Task<(bool Success, string Message)> RestartExplorerManuallyAsync()
    {
        SuppressAutoRestartFor(TimeSpan.FromSeconds(10));
        return await Task.Run(async () =>
        {
            try
            {
                DebugLogService.Instance.Info("RestartExplorerManuallyAsync invoked. Terminating explorer.exe...", "ExplorerWatchdog");
                try
                {
                    await ProcessHelper.RunProcessAsync("taskkill.exe", "/f /im explorer.exe");
                }
                catch { }

                await Task.Delay(800);
                LaunchExplorerShell();

                // Poll for recovery up to 4 seconds
                bool recovered = false;
                for (int i = 0; i < 8; i++)
                {
                    await Task.Delay(500);
                    if (CheckExplorerRunning())
                    {
                        recovered = true;
                        break;
                    }
                }

                // If not yet recovered, trigger secondary emergency launch
                if (!recovered)
                {
                    DebugLogService.Instance.Warning("Explorer did not recover on primary pass; triggering emergency relaunch...", "ExplorerWatchdog");
                    LaunchExplorerShell();
                    await Task.Delay(1000);
                    recovered = CheckExplorerRunning();
                }

                ResumeAutoRestart();
                IsExplorerAlive = CheckExplorerRunning();
                NotifyStatusChanged();

                if (recovered || IsExplorerAlive)
                {
                    return (true, "✅ Windows 檔案總管 (explorer.exe) 已安全重新啟動！");
                }
                else
                {
                    return (false, "⚠️ 已發送重啟指令，但 Windows 檔案總管尚未完全就緒，請稍候或手動透過工作管理員啟動。");
                }
            }
            catch (Exception ex)
            {
                LaunchExplorerShell();
                ResumeAutoRestart();
                return (false, $"❌ 重啟檔案總管發生錯誤: {ex.Message}");
            }
        });
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(2000));
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
                        // Double-check after 500ms to avoid racing a normal shell restart or transient glitch
                        await Task.Delay(500, token);
                        lock (_gate) { suppressUntil = _suppressUntilUtc; }

                        if (DateTime.UtcNow >= suppressUntil && !CheckExplorerRunning())
                        {
                            DebugLogService.Instance.Warning("Detected explorer.exe termination. Auto-restarting Windows shell...", "ExplorerWatchdog");
                            LaunchExplorerShell();
                            AutoRecoveryCount++;
                            LastRecoveryTimeText = $"上次自動救援: {DateTime.Now:HH:mm:ss} (累計 {AutoRecoveryCount} 次)";

                            // Poll for up to 3 seconds for recovery
                            for (int i = 0; i < 6; i++)
                            {
                                await Task.Delay(500, token);
                                if (CheckExplorerRunning())
                                {
                                    break;
                                }
                            }

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

    /// <summary>
    /// Resiliently launches Windows Explorer shell across multiple execution strategies
    /// to bypass Administrator / High-Integrity UIPI restrictions and restore the desktop/taskbar.
    /// </summary>
    public static bool LaunchExplorerShell()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var explorerPath = Path.Combine(winDir, "explorer.exe");
        if (!File.Exists(explorerPath))
        {
            explorerPath = "explorer.exe";
        }

        bool launchedAny = false;

        // Strategy 1: cmd.exe /c start "" explorer.exe with UseShellExecute = true
        // Under Windows Vista/7/8/10/11, delegating through 'start' via ShellExecute breaks out of
        // the elevated parent token inheritance and instructs the Windows Shell/Winlogon service
        // to spawn the interactive user desktop shell.
        try
        {
            var psiCmd = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c start \"\" explorer.exe",
                WorkingDirectory = winDir,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psiCmd);
            launchedAny = true;
            DebugLogService.Instance.Info("Launched explorer shell via cmd start strategy.", "ExplorerWatchdog");
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Debug($"Strategy 1 (cmd start) failed: {ex.Message}", "ExplorerWatchdog");
        }

        // Brief delay to see if strategy 1 registered
        Thread.Sleep(300);
        if (CheckExplorerRunning())
            return true;

        // Strategy 2: Direct ShellExecute Win32 API
        try
        {
            var res = ShellExecute(IntPtr.Zero, "open", explorerPath, null, winDir, 1);
            if (res.ToInt64() > 32)
            {
                launchedAny = true;
                DebugLogService.Instance.Info("Launched explorer shell via native ShellExecute.", "ExplorerWatchdog");
            }
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Debug($"Strategy 2 (ShellExecute) failed: {ex.Message}", "ExplorerWatchdog");
        }

        Thread.Sleep(300);
        if (CheckExplorerRunning())
            return true;

        // Strategy 3: Standard Process.Start with explicit WorkingDirectory and UseShellExecute
        try
        {
            var psiDirect = new ProcessStartInfo
            {
                FileName = explorerPath,
                WorkingDirectory = winDir,
                UseShellExecute = true
            };
            using var p = Process.Start(psiDirect);
            launchedAny = true;
            DebugLogService.Instance.Info("Launched explorer shell via Process.Start direct.", "ExplorerWatchdog");
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Debug($"Strategy 3 (direct Process.Start) failed: {ex.Message}", "ExplorerWatchdog");
        }

        Thread.Sleep(400);
        if (CheckExplorerRunning())
            return true;

        // Strategy 4: PowerShell fallback (for deep UAC / sandboxed edge cases)
        try
        {
            var psiPs = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Start-Process explorer.exe\"",
                WorkingDirectory = winDir,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psiPs);
            p?.WaitForExit(2000);
            launchedAny = true;
            DebugLogService.Instance.Info("Launched explorer shell via PowerShell fallback.", "ExplorerWatchdog");
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Debug($"Strategy 4 (PowerShell fallback) failed: {ex.Message}", "ExplorerWatchdog");
        }

        return launchedAny;
    }

    private void NotifyStatusChanged()
    {
        try { StatusChanged?.Invoke(); } catch { }
    }
}
