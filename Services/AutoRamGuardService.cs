using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Lightweight background Auto RAM Guard service.
/// Polls physical RAM every 15s via 0ms native GlobalMemoryStatusEx (no WMI/PowerShell overhead),
/// and safely trims background working sets (excluding foreground active window &amp; critical/whitelisted apps)
/// when RAM crosses the user-configured threshold after the cooldown window.
/// </summary>
public sealed class AutoRamGuardService : IDisposable
{
    private static readonly Lazy<AutoRamGuardService> _instance = new(() => new AutoRamGuardService());
    public static AutoRamGuardService Instance => _instance.Value;

    private readonly CleanRunOptimizerService _optimizer = new();
    private Timer? _timer;
    private int _isChecking;
    private DateTime _lastTriggerUtc = DateTime.MinValue;
    private bool _disposed;

    public DateTime? LastTrimTimeLocal { get; private set; }
    public long LastReclaimedMb { get; private set; }
    public string LastStatusSummary { get; private set; } = "尚未觸發自動釋放";

    public event Action<MemoryReleaseResult>? GuardTriggered;

    private AutoRamGuardService() { }

    public void Start()
    {
        if (_disposed) return;
        if (_timer != null) return;

        // 15-second lightweight native check
        _timer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTimerTick(object? state)
    {
        if (_disposed) return;
        var settings = SettingsService.Instance.Current;
        if (!settings.AutoRamGuardEnabled) return;

        if (Interlocked.CompareExchange(ref _isChecking, 1, 0) != 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                int threshold = Math.Clamp(settings.AutoRamGuardThresholdPercent, 70, 95);
                int cooldownMin = Math.Clamp(settings.AutoRamGuardCooldownMinutes, 1, 120);

                if (DateTime.UtcNow - _lastTriggerUtc < TimeSpan.FromMinutes(cooldownMin))
                    return;

                var (usagePct, _, _) = CleanRunOptimizerService.GetQuickMemoryStatus();
                if (usagePct < threshold) return;

                _lastTriggerUtc = DateTime.UtcNow;
                var result = await _optimizer.ReleaseMemorySafelyAsync(
                    purgeStandbyList: settings.AutoRamGuardPurgeStandbyList,
                    excludeForeground: settings.AutoRamGuardExcludeForeground,
                    whitelistCsv: settings.AutoRamGuardWhitelistCsv);

                LastTrimTimeLocal = DateTime.Now;
                LastReclaimedMb = result.ReclaimedMb;
                LastStatusSummary = $"[{LastTrimTimeLocal:HH:mm:ss}] RAM 達 {usagePct:F0}% (門檻 {threshold}%)，已釋放 +{result.ReclaimedMb:N0} MB";

                DebugLogService.Instance.Info($"Auto RAM Guard: {result.Message}", "AutoRamGuard");
                GuardTriggered?.Invoke(result);
            }
            catch (Exception ex)
            {
                DebugLogService.Instance.Warning($"Auto RAM Guard 檢查異常: {ex.Message}", "AutoRamGuard");
            }
            finally
            {
                Interlocked.Exchange(ref _isChecking, 0);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
