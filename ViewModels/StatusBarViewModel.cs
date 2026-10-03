using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.ViewModels;

public partial class StatusBarViewModel : ObservableObject
{
    public static StatusBarViewModel Instance { get; } = new();

    private readonly ProgressAndEtaTracker _tracker = new();
    private Action? _cancelAction;

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
        _tracker.ProgressUpdated += (percent, elapsed, eta) =>
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                ProgressPercent = percent;
                ProgressText = percent > 0 ? $"{percent:F1}%" : "";
                IsIndeterminate = percent <= 0;
                ElapsedText = elapsed;
                EtaText = eta;
            });
        };
    }

    public void StartOperation(string title, Action? cancelAction = null)
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            OperationTitle = title;
            _cancelAction = cancelAction;
            CanCancel = cancelAction != null;
            IsActive = true;
            IsIndeterminate = true;
            ProgressPercent = 0;
            ProgressText = "0%";
            ElapsedText = "00:00";
            EtaText = "Estimating...";
            _tracker.Start(title);
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

    public void CompleteOperation(string message = "Done")
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            _tracker.Stop();
            OperationTitle = message;
            ProgressPercent = 100;
            ProgressText = "100%";
            IsIndeterminate = false;
            CanCancel = false;
            _cancelAction = null;
        });
    }

    public void Reset()
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            _tracker.Reset();
            IsActive = false;
            OperationTitle = "Ready";
            ProgressPercent = 0;
            ProgressText = "0%";
            IsIndeterminate = false;
            ElapsedText = "00:00";
            EtaText = "";
            CanCancel = false;
            _cancelAction = null;
        });
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancelAction?.Invoke();
        CompleteOperation("Operation Cancelled.");
    }
}
