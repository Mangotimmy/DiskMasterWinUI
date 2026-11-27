using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

/// <summary>
/// File Safety Protection, Quarantine Restore Vault, and collapsible console state
/// for SystemHealthPage.
/// </summary>
public partial class SystemHealthViewModel
{
    private readonly FileQuarantineService _quarantineService = new();

    public ObservableCollection<QuarantineSessionItem> QuarantineSessions { get; } = new();

    [ObservableProperty] private bool _enableQuarantineBackup = true;
    [ObservableProperty] private bool _enableTempFileAgeGuard = true;
    [ObservableProperty] private int _tempFileMinAgeHours = 24;
    [ObservableProperty] private bool _createRestorePointBeforeRiskyClean;
    [ObservableProperty] private string _quarantineVaultSummary = "隔離還原庫：0 筆備份 (0 B)｜保留 7 天 / 上限 2 GB";
    [ObservableProperty] private bool _hasQuarantineSessions;
    [ObservableProperty] private bool _isConsoleExpanded;

    private bool _protectionSettingsLoaded;

    public string ConsoleToggleLabel => IsConsoleExpanded ? "🔽 收合終端機日誌" : "🔼 展開終端機日誌";

    partial void OnIsConsoleExpandedChanged(bool value) => OnPropertyChanged(nameof(ConsoleToggleLabel));

    private void LoadProtectionSettings()
    {
        var s = SettingsService.Instance.Current;
        _protectionSettingsLoaded = false;
        EnableQuarantineBackup = s.EnableQuarantineBackup;
        EnableTempFileAgeGuard = s.EnableTempFileAgeGuard;
        TempFileMinAgeHours = Math.Clamp(s.TempFileMinAgeHours, 1, 168);
        CreateRestorePointBeforeRiskyClean = s.CreateRestorePointBeforeRiskyClean;
        _protectionSettingsLoaded = true;
    }

    private void SaveProtectionSettings()
    {
        if (!_protectionSettingsLoaded) return;
        var s = SettingsService.Instance.Current;
        s.EnableQuarantineBackup = EnableQuarantineBackup;
        s.EnableTempFileAgeGuard = EnableTempFileAgeGuard;
        s.TempFileMinAgeHours = Math.Clamp(TempFileMinAgeHours, 1, 168);
        s.CreateRestorePointBeforeRiskyClean = CreateRestorePointBeforeRiskyClean;
        SettingsService.Instance.Save();
    }

    partial void OnEnableQuarantineBackupChanged(bool value) => SaveProtectionSettings();
    partial void OnEnableTempFileAgeGuardChanged(bool value) => SaveProtectionSettings();
    partial void OnTempFileMinAgeHoursChanged(int value) => SaveProtectionSettings();
    partial void OnCreateRestorePointBeforeRiskyCleanChanged(bool value) => SaveProtectionSettings();

    [RelayCommand]
    public void ToggleConsoleExpanded()
    {
        IsConsoleExpanded = !IsConsoleExpanded;
    }

    [RelayCommand]
    public async Task RefreshQuarantineVaultAsync()
    {
        try
        {
            LoadProtectionSettings();
            var (sessions, _, totalDisplay) = await _quarantineService.ListSessionsAsync();
            DispatcherHelper.RunOnUIThread(() =>
            {
                QuarantineSessions.Clear();
                foreach (var s in sessions) QuarantineSessions.Add(s);
                HasQuarantineSessions = QuarantineSessions.Count > 0;
                var cfg = SettingsService.Instance.Current;
                QuarantineVaultSummary = $"隔離還原庫：{sessions.Count} 筆可還原批次 ({totalDisplay})｜自動保留 {cfg.QuarantineRetentionDays} 天 / 上限 {cfg.QuarantineMaxSizeMb / 1024.0:F0} GB";
            });
        }
        catch { }
    }

    [RelayCommand]
    public async Task RestoreQuarantineSessionAsync(QuarantineSessionItem? item)
    {
        if (item == null || IsRunning) return;
        try
        {
            IsRunning = true;
            CurrentStatus = $"正在還原隔離批次 [{item.CategorySummary}]...";
            var (ok, count, msg) = await _quarantineService.RestoreSessionAsync(item.SessionId, AppendOutput);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: ok);
            if (ok) item.IsRestored = true;
            await RefreshQuarantineVaultAsync();
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    public async Task DeleteQuarantineSessionAsync(QuarantineSessionItem? item)
    {
        if (item == null || IsRunning) return;
        await _quarantineService.DeleteSessionAsync(item.SessionId);
        AppendOutput($"[INFO] 已移除隔離備份批次: {item.SessionId}");
        await RefreshQuarantineVaultAsync();
    }

    [RelayCommand]
    public async Task PurgeQuarantineVaultAsync()
    {
        if (IsRunning) return;
        bool confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "確認清空隔離還原庫",
            "清空隔離還原庫後，先前清理備份的檔案將永久刪除且無法再一鍵還原（驅動程式備份不受影響）。確定要清空嗎？",
            QuarantineVaultSummary);
        if (!confirmed) return;

        var (_, msg) = await _quarantineService.PurgeAllSessionsAsync();
        CurrentStatus = msg;
        AppendOutput($"[INFO] {msg}");
        await RefreshQuarantineVaultAsync();
    }

    [RelayCommand]
    public void OpenQuarantineFolder()
    {
        try
        {
            Directory.CreateDirectory(_quarantineService.VaultRootPath);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_quarantineService.VaultRootPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendOutput($"[ERROR] 無法開啟隔離庫資料夾: {ex.Message}");
        }
    }
}
