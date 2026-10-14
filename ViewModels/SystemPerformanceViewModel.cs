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
    [ObservableProperty] private DiskPerformanceItem? _selectedDisk;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isAutoRefresh = true;
    [ObservableProperty] private string _processSearchQuery = "";
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private ProcessHunterItem? _selectedProcess;

    public ObservableCollection<ProcessHunterItem> AllProcesses { get; } = new();
    public ObservableCollection<ProcessHunterItem> FilteredProcesses { get; } = new();
    public ObservableCollection<DiskPerformanceItem> DetectedDisks { get; } = new();

    // 60-second historical disk usage buffer for Task Manager style chart
    public Queue<double> DiskUsageHistory { get; } = new(Enumerable.Repeat(0.0, 60));
    public event Action? DiskHistoryUpdated;

    private bool _isInitialized;

    // C1: PID-keyed row cache (reuse ProcessHunterItem instead of rebuilding) + filter bookkeeping.
    private readonly Dictionary<int, ProcessHunterItem> _processIndex = new();
    private readonly HashSet<int> _filteredIndex = new();

    public SystemPerformanceViewModel()
    {
        _refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(1.5);
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
                RefreshProcessesCoreAsync(forceWmicRefresh: true),
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
    public void SelectDisk(DiskPerformanceItem? disk)
    {
        if (disk == null) return;
        SelectedDisk = disk;
        foreach (var d in DetectedDisks)
        {
            d.IsSelected = (d == disk);
        }

        // Synchronize active waveform history queue with selected disk
        DiskUsageHistory.Clear();
        foreach (var val in disk.UsageHistory)
        {
            DiskUsageHistory.Enqueue(val);
        }
        DiskHistoryUpdated?.Invoke();
    }

    [RelayCommand]
    public async Task RefreshMetricsAsync()
    {
        try
        {
            var snap = await _perfService.GetMetricsSnapshotAsync();
            CurrentMetrics = snap;

            // Sync DetectedDisks collection with incoming snapshot disks
            foreach (var incomingDisk in snap.Disks)
            {
                var existing = DetectedDisks.FirstOrDefault(d => d.InstanceName == incomingDisk.InstanceName);
                if (existing == null)
                {
                    DetectedDisks.Add(incomingDisk);
                    existing = incomingDisk;
                }
                else
                {
                    existing.ActiveTimePercent = incomingDisk.ActiveTimePercent;
                    existing.ReadLatencyMs = incomingDisk.ReadLatencyMs;
                    existing.WriteLatencyMs = incomingDisk.WriteLatencyMs;
                    existing.ReadRateDisplay = incomingDisk.ReadRateDisplay;
                    existing.WriteRateDisplay = incomingDisk.WriteRateDisplay;
                    if (!string.IsNullOrEmpty(incomingDisk.ModelName) && incomingDisk.ModelName != existing.ModelName)
                    {
                        existing.ModelName = incomingDisk.ModelName;
                    }
                    if (!string.IsNullOrEmpty(incomingDisk.MediaType))
                    {
                        existing.MediaType = incomingDisk.MediaType;
                    }
                    if (!string.IsNullOrEmpty(incomingDisk.CapacityDisplay))
                    {
                        existing.CapacityDisplay = incomingDisk.CapacityDisplay;
                    }
                }

                existing.UsageHistory.Enqueue(Math.Clamp(incomingDisk.ActiveTimePercent, 0, 100));
                while (existing.UsageHistory.Count > 60)
                {
                    existing.UsageHistory.Dequeue();
                }
            }

            var removed = DetectedDisks.Where(d => !snap.Disks.Any(s => s.InstanceName == d.InstanceName)).ToList();
            foreach (var r in removed) DetectedDisks.Remove(r);

            // Default selection: physical disk 0 (or first)
            if (SelectedDisk == null || !DetectedDisks.Contains(SelectedDisk))
            {
                SelectedDisk = DetectedDisks.FirstOrDefault(d => d.DiskIndex >= 0) ?? DetectedDisks.FirstOrDefault();
            }

            foreach (var d in DetectedDisks)
            {
                d.IsSelected = (d == SelectedDisk);
            }

            // Sync the active display history
            if (SelectedDisk != null)
            {
                DiskUsageHistory.Clear();
                foreach (var val in SelectedDisk.UsageHistory)
                {
                    DiskUsageHistory.Enqueue(val);
                }
            }
            else
            {
                DiskUsageHistory.Enqueue(Math.Clamp(snap.DiskUsagePercent, 0, 100));
                while (DiskUsageHistory.Count > 60)
                {
                    DiskUsageHistory.Dequeue();
                }
            }

            DiskHistoryUpdated?.Invoke();
        }
        catch { }
    }

    [RelayCommand]
    public Task RefreshProcessesAsync() => RefreshProcessesCoreAsync(forceWmicRefresh: false);

    /// <summary>
    /// C1: refreshes the process rows in place. Rows are keyed by PID - the existing
    /// <see cref="ProcessHunterItem"/> is reused and only fields that actually changed are written,
    /// so a tick with no real change raises zero CollectionChanged events (previously every tick did
    /// Clear() + ~300 Add() and allocated ~300 new items).
    /// </summary>
    public async Task RefreshProcessesCoreAsync(bool forceWmicRefresh)
    {
        try
        {
            var procs = await _perfService.GetRunningProcessesDeepAsync(forceWmicRefresh: forceWmicRefresh);
            MergeProcesses(procs);
            ApplyProcessFilter();
            StatusMessage = $"Processes updated: {AllProcesses.Count} active (Orphans: {AllProcesses.Count(p => p.IsOrphan)})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to fetch processes: {ex.Message}";
        }
    }

    private void MergeProcesses(List<ProcessHunterItem> incoming)
    {
        var livePids = new HashSet<int>();
        foreach (var p in incoming)
        {
            livePids.Add(p.Pid);
            if (_processIndex.TryGetValue(p.Pid, out var existing))
            {
                if (existing.ParentPid != p.ParentPid)
                {
                    // PID reuse (identity change): HierarchyDisplay is computed from Pid/ParentPid and the
                    // model exposes no change hook for it, so swap the row - a rare event - instead of
                    // silently showing a stale parent chain.
                    ReplaceProcessItem(existing, p);
                    _processIndex[p.Pid] = p;
                }
                else
                {
                    CopyChangedFields(existing, p);
                }
            }
            else
            {
                _processIndex[p.Pid] = p;
                AllProcesses.Add(p);
            }
        }

        if (_processIndex.Count == livePids.Count) return;

        foreach (var stalePid in _processIndex.Keys.Where(pid => !livePids.Contains(pid)).ToList())
        {
            if (_processIndex.Remove(stalePid, out var gone))
            {
                AllProcesses.Remove(gone);
                if (_filteredIndex.Remove(stalePid))
                {
                    FilteredProcesses.Remove(gone);
                }
            }
        }
    }

    /// <summary>Replaces a reused PID's row in place (observable Replace, index preserved).</summary>
    private void ReplaceProcessItem(ProcessHunterItem oldItem, ProcessHunterItem newItem)
    {
        var allIndex = AllProcesses.IndexOf(oldItem);
        if (allIndex >= 0) AllProcesses[allIndex] = newItem;

        var filteredIndex = FilteredProcesses.IndexOf(oldItem);
        if (filteredIndex >= 0) FilteredProcesses[filteredIndex] = newItem;
    }

    /// <summary>Copies only the fields whose values differ, avoiding needless PropertyChanged storms.</summary>
    private static void CopyChangedFields(ProcessHunterItem target, ProcessHunterItem source)
    {
        if (target.ProcessName != source.ProcessName) target.ProcessName = source.ProcessName;
        if (target.ExecutablePath != source.ExecutablePath) target.ExecutablePath = source.ExecutablePath;
        if (target.CommandLine != source.CommandLine) target.CommandLine = source.CommandLine;
        if (target.MemoryBytes != source.MemoryBytes) target.MemoryBytes = source.MemoryBytes;
        if (target.DisplayMemory != source.DisplayMemory) target.DisplayMemory = source.DisplayMemory;
        if (target.IsResponding != source.IsResponding) target.IsResponding = source.IsResponding;
        if (target.IsOrphan != source.IsOrphan) target.IsOrphan = source.IsOrphan;
        if (target.IsSystemProtected != source.IsSystemProtected) target.IsSystemProtected = source.IsSystemProtected;
        if (target.StatusBadge != source.StatusBadge) target.StatusBadge = source.StatusBadge;
        // ParentPid is intentionally absent: a changed parent is handled by ReplaceProcessItem so the
        // computed HierarchyDisplay re-evaluates.
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

    /// <summary>
    /// C1: incremental filter. The previous implementation cleared and re-added the whole filtered
    /// collection on every keystroke; this version diffs against the current list and only emits the
    /// Insert/Remove/Move events that the new result actually requires (identical final ordering).
    /// </summary>
    private void ApplyProcessFilter()
    {
        var query = ProcessSearchQuery.Trim();

        bool Matches(ProcessHunterItem p) =>
            string.IsNullOrEmpty(query) ||
            p.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.Pid.ToString().Contains(query) ||
            p.CommandLine.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase);

        var desiredCount = 0;
        for (int i = 0; i < AllProcesses.Count; i++)
        {
            var item = AllProcesses[i];
            if (!Matches(item))
            {
                if (_filteredIndex.Remove(item.Pid))
                {
                    FilteredProcesses.Remove(item);
                }
                continue;
            }

            if (desiredCount < FilteredProcesses.Count && ReferenceEquals(FilteredProcesses[desiredCount], item))
            {
                desiredCount++;
                continue;
            }

            int existingIndex = -1;
            for (int j = desiredCount; j < FilteredProcesses.Count; j++)
            {
                if (ReferenceEquals(FilteredProcesses[j], item)) { existingIndex = j; break; }
            }

            if (existingIndex >= 0)
            {
                FilteredProcesses.Move(existingIndex, desiredCount);
            }
            else
            {
                FilteredProcesses.Insert(desiredCount, item);
                _filteredIndex.Add(item.Pid);
            }
            desiredCount++;
        }

        while (FilteredProcesses.Count > desiredCount)
        {
            var tail = FilteredProcesses[FilteredProcesses.Count - 1];
            FilteredProcesses.RemoveAt(FilteredProcesses.Count - 1);
            _filteredIndex.Remove(tail.Pid);
        }
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
        await RefreshProcessesCoreAsync(forceWmicRefresh: true);
    }

    [RelayCommand]
    public async Task KillAllNotRespondingAsync()
    {
        StatusMessage = "Terminating all unresponsive processes...";
        var (ok, msg) = await _perfService.KillAllNotRespondingAsync();
        StatusMessage = msg;
        await RefreshProcessesCoreAsync(forceWmicRefresh: true);
    }
}
