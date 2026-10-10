using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;
using Microsoft.UI.Dispatching;

namespace DiskMasterWinUI.ViewModels;

public partial class SystemPerformanceViewModel : ObservableObject
{
    private readonly SystemPerformanceService _perfService = new();
    private readonly DispatcherQueueTimer _refreshTimer;

    [ObservableProperty] private SystemMetricsSnapshot _currentMetrics = new();
    [ObservableProperty] private SystemInfoSummary _systemInfo = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isAutoRefresh = true;
    [ObservableProperty] private string _processSearchQuery = "";
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private ProcessHunterItem? _selectedProcess;

    public ObservableCollection<ProcessHunterItem> AllProcesses { get; } = new();
    public ObservableCollection<ProcessHunterItem> FilteredProcesses { get; } = new();

    private bool _isInitialized;

    public SystemPerformanceViewModel()
    {
        _refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(3);
        _refreshTimer.Tick += async (_, _) =>
        {
            if (IsAutoRefresh && !IsLoading)
            {
                await RefreshMetricsAsync();
            }
        };
    }

    public void StartAutoRefresh()
    {
        if (!_refreshTimer.IsRunning)
        {
            _refreshTimer.Start();
        }
    }

    public void StopAutoRefresh()
    {
        if (_refreshTimer.IsRunning)
        {
            _refreshTimer.Stop();
        }
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        IsLoading = true;
        try
        {
            // Execute metrics, process hunter, and system profile concurrently
            await Task.WhenAll(
                RefreshMetricsAsync(),
                RefreshProcessesAsync(),
                RefreshSystemInfoAsync()
            );
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        _isInitialized = false;
        await InitializeAsync();
    }

    [RelayCommand]
    public async Task RefreshMetricsAsync()
    {
        try
        {
            var snap = await _perfService.GetMetricsSnapshotAsync();
            CurrentMetrics = snap;
        }
        catch { }
    }

    [RelayCommand]
    public async Task RefreshProcessesAsync()
    {
        try
        {
            var procs = await _perfService.GetRunningProcessesDeepAsync();
            AllProcesses.Clear();
            foreach (var p in procs) AllProcesses.Add(p);
            ApplyProcessFilter();
            StatusMessage = $"Processes updated: {AllProcesses.Count} active (Orphans: {AllProcesses.Count(p => p.IsOrphan)})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to fetch processes: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RefreshSystemInfoAsync()
    {
        try
        {
            var info = await _perfService.GetSystemInfoSummaryAsync();
            SystemInfo = info;
        }
        catch { }
    }

    partial void OnProcessSearchQueryChanged(string value)
    {
        ApplyProcessFilter();
    }

    private void ApplyProcessFilter()
    {
        FilteredProcesses.Clear();
        var query = ProcessSearchQuery.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? AllProcesses
            : AllProcesses.Where(p =>
                p.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Pid.ToString().Contains(query) ||
                p.CommandLine.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var m in matches) FilteredProcesses.Add(m);
    }

    [RelayCommand]
    public async Task KillProcessTreeAsync(ProcessHunterItem? item)
    {
        item ??= SelectedProcess;
        if (item == null) return;

        if (item.IsSystemProtected)
        {
            StatusMessage = $"Protected process {item.ProcessName} (PID {item.Pid}) cannot be terminated.";
            return;
        }

        StatusMessage = $"Terminating PID {item.Pid} ({item.ProcessName}) and child tree...";
        var (ok, msg) = await _perfService.KillProcessTreeAsync(item.Pid);
        StatusMessage = msg;
        await RefreshProcessesAsync();
    }

    [RelayCommand]
    public async Task KillAllNotRespondingAsync()
    {
        StatusMessage = "Terminating all unresponsive processes...";
        var (ok, msg) = await _perfService.KillAllNotRespondingAsync();
        StatusMessage = msg;
        await RefreshProcessesAsync();
    }
}
