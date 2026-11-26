using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

/// <summary>
/// Clean-Run, Startup Manager, Background Bloat Tamer, and Auto RAM Guard partial ViewModel
/// for SystemOptimizerPage (TabCleanRun).
/// </summary>
public partial class SystemOptimizerViewModel
{
    private readonly StartupManagerService _startupService = new();
    private readonly CleanRunOptimizerService _cleanRunService = new();

    public ObservableCollection<StartupEntryItem> AllStartupEntries { get; } = new();
    public ObservableCollection<StartupEntryItem> FilteredStartupEntries { get; } = new();
    public ObservableCollection<BackgroundBloatTweakItem> BackgroundBloatTweaks { get; } = new();

    [ObservableProperty] private bool _isCleanRunLoading;
    [ObservableProperty] private string _cleanRunStatusMessage = "就緒：可套用一鍵流暢預設、管理開機啟動項或啟用自動記憶體守衛。";
    [ObservableProperty] private string _startupSearchQuery = "";
    [ObservableProperty] private bool _showOnlyProblemStartupEntries;
    [ObservableProperty] private string _startupSummaryBadge = "尚未掃描";

    // ── Auto RAM Guard Settings (persisted to AppSettings) ──
    [ObservableProperty] private bool _autoRamGuardEnabled;
    [ObservableProperty] private int _autoRamGuardThresholdPercent = 82;
    [ObservableProperty] private int _autoRamGuardCooldownMinutes = 10;
    [ObservableProperty] private bool _autoRamGuardPurgeStandbyList = true;
    [ObservableProperty] private bool _autoRamGuardExcludeForeground = true;
    [ObservableProperty] private string _autoRamGuardWhitelistCsv = "obs64,audiodg,voicemeeter,dwm";
    [ObservableProperty] private string _autoRamGuardLastSummary = "尚未觸發自動釋放";

    private bool _cleanRunSettingsLoaded;

    private void LoadCleanRunSettingsFromStore()
    {
        var s = SettingsService.Instance.Current;
        _cleanRunSettingsLoaded = false;
        AutoRamGuardEnabled = s.AutoRamGuardEnabled;
        AutoRamGuardThresholdPercent = Math.Clamp(s.AutoRamGuardThresholdPercent, 70, 95);
        AutoRamGuardCooldownMinutes = Math.Clamp(s.AutoRamGuardCooldownMinutes, 1, 120);
        AutoRamGuardPurgeStandbyList = s.AutoRamGuardPurgeStandbyList;
        AutoRamGuardExcludeForeground = s.AutoRamGuardExcludeForeground;
        AutoRamGuardWhitelistCsv = s.AutoRamGuardWhitelistCsv;
        AutoRamGuardLastSummary = AutoRamGuardService.Instance.LastStatusSummary;
        _cleanRunSettingsLoaded = true;
    }

    private void SaveAutoRamGuardSettings()
    {
        if (!_cleanRunSettingsLoaded) return;
        var s = SettingsService.Instance.Current;
        s.AutoRamGuardEnabled = AutoRamGuardEnabled;
        s.AutoRamGuardThresholdPercent = Math.Clamp(AutoRamGuardThresholdPercent, 70, 95);
        s.AutoRamGuardCooldownMinutes = Math.Clamp(AutoRamGuardCooldownMinutes, 1, 120);
        s.AutoRamGuardPurgeStandbyList = AutoRamGuardPurgeStandbyList;
        s.AutoRamGuardExcludeForeground = AutoRamGuardExcludeForeground;
        s.AutoRamGuardWhitelistCsv = AutoRamGuardWhitelistCsv ?? "";
        SettingsService.Instance.Save();

        if (AutoRamGuardEnabled)
        {
            AutoRamGuardService.Instance.Start();
        }
    }

    partial void OnAutoRamGuardEnabledChanged(bool value) => SaveAutoRamGuardSettings();
    partial void OnAutoRamGuardThresholdPercentChanged(int value) => SaveAutoRamGuardSettings();
    partial void OnAutoRamGuardCooldownMinutesChanged(int value) => SaveAutoRamGuardSettings();
    partial void OnAutoRamGuardPurgeStandbyListChanged(bool value) => SaveAutoRamGuardSettings();
    partial void OnAutoRamGuardExcludeForegroundChanged(bool value) => SaveAutoRamGuardSettings();
    partial void OnAutoRamGuardWhitelistCsvChanged(string value) => SaveAutoRamGuardSettings();

    partial void OnStartupSearchQueryChanged(string value) => ApplyStartupFilter();
    partial void OnShowOnlyProblemStartupEntriesChanged(bool value) => ApplyStartupFilter();

    private void ApplyStartupFilter()
    {
        FilteredStartupEntries.Clear();
        var q = StartupSearchQuery?.Trim() ?? "";
        foreach (var item in AllStartupEntries)
        {
            if (ShowOnlyProblemStartupEntries && !item.HasWarning) continue;
            if (q.Length > 0 &&
                !item.Name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !item.Command.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !item.SourceDisplay.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            FilteredStartupEntries.Add(item);
        }
    }

    [RelayCommand]
    public async Task RefreshCleanRunDataAsync()
    {
        if (IsCleanRunLoading) return;
        IsCleanRunLoading = true;
        try
        {
            LoadCleanRunSettingsFromStore();

            var startupTask = _startupService.ScanStartupEntriesAsync();
            var tweaksTask = _cleanRunService.GetBackgroundBloatTweaksAsync();
            await Task.WhenAll(startupTask, tweaksTask);

            var startups = await startupTask;
            var tweaks = await tweaksTask;

            IsUpdatingProgrammatically = true;
            try
            {
                AllStartupEntries.Clear();
                foreach (var entry in startups) AllStartupEntries.Add(entry);
                ApplyStartupFilter();

                int enabledCount = startups.Count(x => x.IsEnabled);
                int warningCount = startups.Count(x => x.IsEnabled && x.HasWarning);
                StartupSummaryBadge = $"共 {startups.Count} 項｜已啟用 {enabledCount} 項｜高耗／重複警示 {warningCount} 項";

                BackgroundBloatTweaks.Clear();
                foreach (var t in tweaks) BackgroundBloatTweaks.Add(t);
            }
            finally
            {
                IsUpdatingProgrammatically = false;
            }

            CleanRunStatusMessage = $"已更新全機減負狀態（開機啟動項 {startups.Count} 筆，背景減負項目 {tweaks.Count} 項）。";
        }
        catch (Exception ex)
        {
            CleanRunStatusMessage = $"讀取全機減負資料失敗: {ex.Message}";
            AppendLog($"[ERROR] Clean-Run refresh failed: {ex.Message}");
        }
        finally
        {
            IsCleanRunLoading = false;
        }
    }

    [RelayCommand]
    public async Task ApplyCleanRunPresetSafeAsync() => await RunCleanRunPresetCoreAsync(CleanRunPresetKind.SafeDaily);

    [RelayCommand]
    public async Task ApplyCleanRunPreset16GbAsync() => await RunCleanRunPresetCoreAsync(CleanRunPresetKind.Balanced16Gb);

    [RelayCommand]
    public async Task RestoreCleanRunDefaultsAsync() => await RunCleanRunPresetCoreAsync(CleanRunPresetKind.RestoreWindowsDefaults);

    private async Task RunCleanRunPresetCoreAsync(CleanRunPresetKind preset)
    {
        if (IsCleanRunLoading) return;
        IsCleanRunLoading = true;
        try
        {
            CleanRunStatusMessage = $"正在套用全機流暢設定檔 [{preset}]...";
            var (ok, summary) = await _cleanRunService.ApplyCleanRunPresetAsync(preset, AppendLog);
            CleanRunStatusMessage = summary;
            StatusMessage = summary;
            if (ok) AudioFeedbackService.PlaySuccess();
            else AudioFeedbackService.PlayError();

            IsCleanRunLoading = false;
            await RefreshCleanRunDataAsync();
        }
        finally
        {
            IsCleanRunLoading = false;
        }
    }

    [RelayCommand]
    public async Task ReleaseMemoryNowAsync()
    {
        if (IsCleanRunLoading) return;
        IsCleanRunLoading = true;
        try
        {
            CleanRunStatusMessage = "正在執行前景保護安全記憶體釋放...";
            var res = await _cleanRunService.ReleaseMemorySafelyAsync(
                purgeStandbyList: AutoRamGuardPurgeStandbyList,
                excludeForeground: AutoRamGuardExcludeForeground,
                whitelistCsv: AutoRamGuardWhitelistCsv);
            AutoRamGuardLastSummary = $"[{DateTime.Now:HH:mm:ss}] 手動釋放 +{res.ReclaimedMb:N0} MB (修剪 {res.TrimmedProcessCount} 行程)";
            CleanRunStatusMessage = res.Message;
            StatusMessage = res.Message;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {res.Message}");
            AudioFeedbackService.PlaySuccess();
        }
        catch (Exception ex)
        {
            CleanRunStatusMessage = $"釋放記憶體失敗: {ex.Message}";
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally
        {
            IsCleanRunLoading = false;
        }
    }

    public async Task ToggleStartupEntryAsync(StartupEntryItem item, bool targetEnabled)
    {
        var (ok, msg) = await _startupService.SetStartupEntryEnabledAsync(item, targetEnabled);
        CleanRunStatusMessage = msg;
        StatusMessage = msg;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}");
        if (!ok)
        {
            IsUpdatingProgrammatically = true;
            try { item.IsEnabled = !targetEnabled; }
            finally { IsUpdatingProgrammatically = false; }
            AudioFeedbackService.PlayError();
        }
        else
        {
            int enabledCount = AllStartupEntries.Count(x => x.IsEnabled);
            int warningCount = AllStartupEntries.Count(x => x.IsEnabled && x.HasWarning);
            StartupSummaryBadge = $"共 {AllStartupEntries.Count} 項｜已啟用 {enabledCount} 項｜高耗／重複警示 {warningCount} 項";
        }
    }

    [RelayCommand]
    public async Task DisableDuplicateAndBrowserStartupsAsync()
    {
        if (IsCleanRunLoading) return;
        IsCleanRunLoading = true;
        try
        {
            var (_, summary) = await _startupService.DisableDuplicatesAndBrowserAutoLaunchAsync(AllStartupEntries, AppendLog);
            CleanRunStatusMessage = summary;
            StatusMessage = summary;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {summary}");
            int enabledCount = AllStartupEntries.Count(x => x.IsEnabled);
            int warningCount = AllStartupEntries.Count(x => x.IsEnabled && x.HasWarning);
            StartupSummaryBadge = $"共 {AllStartupEntries.Count} 項｜已啟用 {enabledCount} 項｜高耗／重複警示 {warningCount} 項";
            AudioFeedbackService.PlaySuccess();
        }
        finally
        {
            IsCleanRunLoading = false;
        }
    }

    public async Task ToggleBackgroundBloatTweakAsync(BackgroundBloatTweakItem item, bool targetOptimized)
    {
        var (ok, msg) = await _cleanRunService.ApplyBackgroundBloatTweakAsync(item.Id, targetOptimized);
        CleanRunStatusMessage = msg;
        StatusMessage = msg;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}");

        if (!ok)
        {
            IsUpdatingProgrammatically = true;
            try { item.IsOptimized = !targetOptimized; }
            finally { IsUpdatingProgrammatically = false; }
            AudioFeedbackService.PlayError();
            return;
        }

        item.IsOptimized = targetOptimized;
        AudioFeedbackService.PlaySuccess();
        await RefreshCleanRunDataAsync();
    }
}
