using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

public partial class StatusBarViewModel : ObservableObject
{
    public static StatusBarViewModel Instance { get; } = new();

    private readonly ProgressAndEtaTracker _tracker = new();
    private readonly System.Threading.Timer _elapsedTimer;
    private Action? _cancelAction;
    private string _operationBaseTitle = "";
    private DateTime _operationStartedUtc;
    private TimeSpan? _expectedDuration;
    private bool _operationCompleted;

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _operationTitle = "Ready";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _progressText = "0%";
    [ObservableProperty] private bool _isIndeterminate = true;
    [ObservableProperty] private string _elapsedText = "00:00";
    [ObservableProperty] private string _etaText = "Ready";
    [ObservableProperty] private bool _canCancel;

    public StatusBarViewModel()
    {
        _elapsedTimer = new System.Threading.Timer(OnElapsedTick, null, Timeout.Infinite, Timeout.Infinite);

        _tracker.ProgressUpdated += (percent, elapsed, eta) =>
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                ProgressPercent = percent;
                ProgressText = percent > 0 ? $"{percent:F1}%" : "";
                IsIndeterminate = percent <= 0;
                ElapsedText = elapsed;
                EtaText = LocalizeTrackerText(eta);
            });
        };

        OperationTitle = LocReady();
    }

    private static string LocReady()
    {
        var value = LocalizationService.Instance["StatusReady"];
        return string.IsNullOrWhiteSpace(value) || value == "StatusReady" ? "Ready" : value;
    }

    private static string LocEstimating() =>
        LocalizationService.T("估算中...", "估算中...", "Estimating...", "推定中...");

    private static string LocCompleted() =>
        LocalizationService.T("已完成", "已完成", "Completed", "完了");

    private static string LocStopped() =>
        LocalizationService.T("已停止", "已停止", "Stopped", "停止");

    private static string LocalizeTrackerText(string text)
    {
        switch (text)
        {
            case "Estimating...": return LocEstimating();
            case "Completed": return LocCompleted();
            case "Calculating ETA...":
                return LocalizationService.T("正在計算剩餘時間...", "正在计算剩余时间...", "Calculating ETA...", "残り時間を計算中...");
            case "Almost done...":
                return LocalizationService.T("即將完成...", "即将完成...", "Almost done...", "まもなく完了...");
        }

        var remaining = Regex.Match(text, @"^~(?<v>\d{1,3}(?::\d{2}){1,2}(?:\s*h\s*\d{1,2}\s*m)?|\d{1,3}h\s*\d{1,2}m) remaining$");
        if (remaining.Success)
        {
            var value = remaining.Groups["v"].Value;
            return LocalizationService.T($"剩餘 ~{value}", $"剩余 ~{value}", $"~{value} remaining", $"残り ~{value}");
        }

        return text;
    }

    public void StartOperation(string title, Action? cancelAction = null, TimeSpan? estimatedDuration = null)
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            OperationTitle = title;
            _operationBaseTitle = title;
            _operationStartedUtc = DateTime.UtcNow;
            _expectedDuration = estimatedDuration;
            _operationCompleted = false;
            _cancelAction = cancelAction;
            CanCancel = cancelAction != null;
            IsActive = true;
            IsIndeterminate = true;
            ProgressPercent = 0;
            ProgressText = "0%";
            ElapsedText = "00:00";
            EtaText = LocEstimating();
            _tracker.Start(title, estimatedDuration);
            _elapsedTimer.Change(1000, 1000);
        });
    }

    public void ProcessOutputLine(string line)
    {
        _tracker.ProcessLine(line);
    }

    public void UpdateProgressExplicit(double percent, string eta = "")
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            _tracker.UpdateProgress(percent);
            if (!string.IsNullOrEmpty(eta))
            {
                EtaText = eta;
            }
        });
    }

    public void CompleteOperation(string message = "Done", bool success = true, bool notify = true)
    {
        bool firstCompletion = !_operationCompleted;
        _operationCompleted = true;
        if (!firstCompletion)
        {
            // Cancel() and the underlying operation both call CompleteOperation. Keep the
            // final status update, but never emit a second sound/toast for the same operation.
            notify = false;
        }

        var elapsed = _tracker.ElapsedTime;
        if (elapsed <= TimeSpan.Zero && _operationStartedUtc != default)
        {
            elapsed = DateTime.UtcNow - _operationStartedUtc;
        }
        var operationTitle = _operationBaseTitle;
        var targetTab = ResolveNotificationTarget(operationTitle);

        DispatcherHelper.RunOnUIThread(() =>
        {
            _elapsedTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _tracker.Complete();
            OperationTitle = message;
            ProgressPercent = 100;
            ProgressText = "100%";
            IsIndeterminate = false;
            EtaText = success ? LocCompleted() : LocStopped();
            CanCancel = false;
            _cancelAction = null;
        });

        if (firstCompletion && success)
        {
            AudioFeedbackService.PlaySuccess();
        }
        else if (firstCompletion && notify)
        {
            AudioFeedbackService.PlayError();
        }

        if (!notify || elapsed.TotalSeconds < 5 || string.IsNullOrWhiteSpace(operationTitle))
        {
            return;
        }

        try
        {
            string title = success
                ? LocalizationService.T("DiskMaster Pro — 操作完成", "DiskMaster Pro — 操作完成", "DiskMaster Pro — Operation complete", "DiskMaster Pro — 操作完了")
                : LocalizationService.T("DiskMaster Pro — 操作失敗", "DiskMaster Pro — 操作失敗", "DiskMaster Pro — Operation failed", "DiskMaster Pro — 操作失敗");
            string body = $"{operationTitle} · {FormatElapsed(elapsed)}\n{message}";
            WindowsNotificationService.Instance.ShowNotification(title, body, targetTab, playSound: false);
            DebugLogService.Instance.LogAction("通知 (Notification)", $"已完成操作通知: {operationTitle}", $"{message} | 耗時 {FormatElapsed(elapsed)}");

            if (SettingsService.Instance.Current.EnableTaskbarFlash)
            {
                TaskbarFlashService.FlashWindow(WindowHelper.CurrentHwnd, 3);
            }
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Warning($"完成通知發送失敗: {ex.Message}", "Notification");
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private static string? ResolveNotificationTarget(string operationTitle)
    {
        if (string.IsNullOrWhiteSpace(operationTitle)) return null;
        if (operationTitle.Contains("chkdsk", StringComparison.OrdinalIgnoreCase) ||
            operationTitle.Contains("defrag", StringComparison.OrdinalIgnoreCase) ||
            operationTitle.Contains("TRIM", StringComparison.OrdinalIgnoreCase) ||
            operationTitle.Contains("BitLocker", StringComparison.OrdinalIgnoreCase))
        {
            return "DiskTools";
        }
        if (operationTitle.Contains("sfc", StringComparison.OrdinalIgnoreCase) ||
            operationTitle.Contains("dism", StringComparison.OrdinalIgnoreCase))
        {
            return "SystemHealth";
        }
        return null;
    }

    public void Reset()
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            _elapsedTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _tracker.Reset();
            _operationCompleted = false;
            _operationBaseTitle = "";
            IsActive = false;
            OperationTitle = LocReady();
            ProgressPercent = 0;
            ProgressText = "0%";
            IsIndeterminate = false;
            ElapsedText = "00:00";
            EtaText = "";
            CanCancel = false;
            _cancelAction = null;
        });
    }

    /// <summary>
    /// Ticks once per second while an operation is active so elapsed time and ETA stay live even
    /// when the underlying process produces no output line that matches a progress regex.
    /// </summary>
    private void OnElapsedTick(object? state)
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            if (!IsActive) return;

            var snapshot = _tracker.GetLiveSnapshot();
            ElapsedText = snapshot.Elapsed;

            if (snapshot.Percent > 0)
            {
                ProgressPercent = snapshot.Percent;
                ProgressText = $"{snapshot.Percent:F1}%";
                IsIndeterminate = false;
            }

            EtaText = string.IsNullOrEmpty(snapshot.Eta) ? LocEstimating() : snapshot.Eta;
        });
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancelAction?.Invoke();
        CompleteOperation("Operation Cancelled.", success: false, notify: false);
    }
}
