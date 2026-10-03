using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

public partial class SystemHealthViewModel : ObservableObject
{
    private readonly SystemRepairService _repairService = new();
    private readonly ThrottledLogBuffer _logBuffer;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private string _currentStatus = "Ready to scan or repair system";
    [ObservableProperty] private string _terminalOutput = "";

    [ObservableProperty] private string _repairSourcePath = "";
    [ObservableProperty] private string _offlineImagePath = "";
    [ObservableProperty] private string _offlineBootDir = "";
    [ObservableProperty] private string _offlineWinDir = "";
    [ObservableProperty] private string _featureName = "";
    [ObservableProperty] private string _featureSourcePath = "";
    [ObservableProperty] private string _packageName = "";
    [ObservableProperty] private string _driverExportDir = @"D:\DriverBackup";

    // ── Structured UI Lists & Filters ──
    public ObservableCollection<WindowsFeatureItem> AllFeatures { get; } = new();
    public ObservableCollection<WindowsFeatureItem> FilteredFeatures { get; } = new();
    public ObservableCollection<WindowsPackageItem> AllPackages { get; } = new();
    public ObservableCollection<WindowsPackageItem> FilteredPackages { get; } = new();

    [ObservableProperty] private WindowsFeatureItem? _selectedFeature;
    [ObservableProperty] private WindowsPackageItem? _selectedPackage;
    [ObservableProperty] private string _featureFilterText = "";
    [ObservableProperty] private string _packageFilterText = "";
    [ObservableProperty] private bool _isLoadingFeatures;
    [ObservableProperty] private bool _isLoadingPackages;
    [ObservableProperty] private bool _hasFeatures;
    [ObservableProperty] private bool _hasPackages;
    private readonly WindowsUpdateRepairService _wuService = new();
    private readonly TempCleanService _tempCleanService = new();

    // ── Windows Update & SoftwareDistribution ──
    [ObservableProperty] private WindowsUpdateInfo _wuInfo = new();
    [ObservableProperty] private bool _isWuUpdating;
    [ObservableProperty] private bool _backupSoftwareDist = true;

    // ── Deep Temp & Cache Cleaning ──
    public ObservableCollection<TempCategoryItem> TempCategories { get; } = new();
    [ObservableProperty] private bool _isScanningTemp;
    [ObservableProperty] private bool _isCleaningTemp;
    [ObservableProperty] private string _totalScannedTempDisplay = "0 B";
    [ObservableProperty] private long _totalScannedTempBytes;
    [ObservableProperty] private bool _isAllTempSelected = true;

    private bool _isInitialized;

    public SystemHealthViewModel()
    {
        _logBuffer = new ThrottledLogBuffer(text => TerminalOutput = text);
        IsAdmin = AdminHelper.IsRunningAsAdmin();
        AppendOutput("Windows System Repair Engine Initialized (SFC, DISM, WU Repair & Temp Cleaner).");
        if (!IsAdmin)
        {
            AppendOutput("[WARNING] SFC and DISM require Administrator privileges to repair system files.");
        }
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        await RefreshWindowsUpdateStatusAsync();
        await ScanTempCategoriesAsync();
    }

    private void AppendOutput(string line)
    {
        StatusBarViewModel.Instance.ProcessOutputLine(line);
        _logBuffer.AppendLine(line);
    }

    [RelayCommand]
    private async Task RunOneClickRepairAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            CurrentStatus = "Running 1-Click Complete System Repair...";
            StatusBarViewModel.Instance.StartOperation("1-Click System Repair", CancelOperation);
            _cts = new CancellationTokenSource();
            await _repairService.RunOneClickFullHealthRepairAsync(AppendOutput, _cts.Token);
            CurrentStatus = "1-Click System Repair Complete.";
            StatusBarViewModel.Instance.CompleteOperation("1-Click Repair Complete.");
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task RunSfcScanNowAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            CurrentStatus = "Running sfc /scannow...";
            StatusBarViewModel.Instance.StartOperation("sfc /scannow", CancelOperation);
            _cts = new CancellationTokenSource();
            await _repairService.RunSfcScanNowAsync(AppendOutput, _cts.Token);
            CurrentStatus = "SFC Scan Complete.";
            StatusBarViewModel.Instance.CompleteOperation("SFC Scan Complete.");
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task RunSfcVerifyOnlyAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            CurrentStatus = "Running sfc /verifyonly...";
            StatusBarViewModel.Instance.StartOperation("sfc /verifyonly", CancelOperation);
            _cts = new CancellationTokenSource();
            await _repairService.RunSfcVerifyOnlyAsync(AppendOutput, _cts.Token);
            CurrentStatus = "SFC Verify Complete.";
            StatusBarViewModel.Instance.CompleteOperation("SFC Verify Complete.");
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task RunDismCheckHealthAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            CurrentStatus = "Running dism /checkhealth...";
            StatusBarViewModel.Instance.StartOperation("dism /checkhealth", CancelOperation);
            _cts = new CancellationTokenSource();
            await _repairService.RunDismCheckHealthAsync(AppendOutput, _cts.Token);
            CurrentStatus = "DISM CheckHealth Complete.";
            StatusBarViewModel.Instance.CompleteOperation("DISM CheckHealth Complete.");
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task RunDismScanHealthAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            CurrentStatus = "Running dism /scanhealth...";
            StatusBarViewModel.Instance.StartOperation("dism /scanhealth", CancelOperation);
            _cts = new CancellationTokenSource();
            await _repairService.RunDismScanHealthAsync(AppendOutput, _cts.Token);
            CurrentStatus = "DISM ScanHealth Complete.";
            StatusBarViewModel.Instance.CompleteOperation("DISM ScanHealth Complete.");
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task RunDismRestoreHealthAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            CurrentStatus = "Running dism /restorehealth...";
            StatusBarViewModel.Instance.StartOperation("dism /restorehealth", CancelOperation);
            _cts = new CancellationTokenSource();
            await _repairService.RunDismRestoreHealthAsync(AppendOutput, _cts.Token);
            CurrentStatus = "DISM RestoreHealth Complete.";
            StatusBarViewModel.Instance.CompleteOperation("DISM RestoreHealth Complete.");
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task RunDismCleanupAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            CurrentStatus = "Running dism /startcomponentcleanup...";
            StatusBarViewModel.Instance.StartOperation("dism /cleanup", CancelOperation);
            _cts = new CancellationTokenSource();
            await _repairService.RunDismComponentCleanupAsync(AppendOutput, _cts.Token);
            CurrentStatus = "DISM Component Cleanup Complete.";
            StatusBarViewModel.Instance.CompleteOperation("DISM Cleanup Complete.");
        }
        finally
        {
            IsRunning = false;
        }
    }

    // ── Phase 2: Advanced DISM Operations ──

    [RelayCommand]
    private async Task RunDismAnalyzeStoreAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            CurrentStatus = "Analyzing component store...";
            StatusBarViewModel.Instance.StartOperation("dism /analyzecomponentstore", CancelOperation);
            _cts = new CancellationTokenSource();
            await _repairService.RunDismAnalyzeComponentStoreAsync(AppendOutput, _cts.Token);
            CurrentStatus = "Component store analysis complete.";
            StatusBarViewModel.Instance.CompleteOperation("Analysis complete.");
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task RunDismResetBaseAsync()
    {
        if (IsRunning) return;

        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "深度清理 (DISM /ResetBase)",
            "執行 /ResetBase 後將抹除所有舊版 Windows 更新備份以釋放數 GB 磁碟空間。但之後將「永遠無法解除安裝」目前已安裝的更新！",
            "WinSxS Component Store");
        if (!confirmed) return;

        try
        {
            IsRunning = true;
            CurrentStatus = "Running deep cleanup /ResetBase...";
            StatusBarViewModel.Instance.StartOperation("dism /resetbase", CancelOperation);
            _cts = new CancellationTokenSource();
            await _repairService.RunDismCleanupResetBaseAsync(AppendOutput, _cts.Token);
            CurrentStatus = "Deep cleanup complete.";
            StatusBarViewModel.Instance.CompleteOperation("ResetBase complete.");
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task RunDismRestoreWithSourceAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(RepairSourcePath)) return;
        try
        {
            IsRunning = true;
            CurrentStatus = $"RestoreHealth with source: {RepairSourcePath}...";
            StatusBarViewModel.Instance.StartOperation("dism /restorehealth /source", CancelOperation);
            _cts = new CancellationTokenSource();
            await _repairService.RunDismRestoreHealthWithSourceAsync(RepairSourcePath, AppendOutput, _cts.Token);
            CurrentStatus = "RestoreHealth with source complete.";
            StatusBarViewModel.Instance.CompleteOperation("RestoreHealth with source complete.");
        }
        finally { IsRunning = false; }
    }

    partial void OnSelectedFeatureChanged(WindowsFeatureItem? value)
    {
        if (value != null)
        {
            FeatureName = value.FeatureName;
        }
    }

    partial void OnSelectedPackageChanged(WindowsPackageItem? value)
    {
        if (value != null)
        {
            PackageName = value.PackageIdentity;
        }
    }

    partial void OnFeatureFilterTextChanged(string value)
    {
        ApplyFeatureFilter();
    }

    partial void OnPackageFilterTextChanged(string value)
    {
        ApplyPackageFilter();
    }

    private void ApplyFeatureFilter()
    {
        FilteredFeatures.Clear();
        var query = FeatureFilterText?.Trim() ?? "";
        foreach (var item in AllFeatures)
        {
            if (string.IsNullOrEmpty(query) ||
                item.FeatureName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.State.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredFeatures.Add(item);
            }
        }
        HasFeatures = AllFeatures.Count > 0;
    }

    private void ApplyPackageFilter()
    {
        FilteredPackages.Clear();
        var query = PackageFilterText?.Trim() ?? "";
        foreach (var item in AllPackages)
        {
            if (string.IsNullOrEmpty(query) ||
                item.PackageIdentity.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.KbArticle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.ReleaseType.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredPackages.Add(item);
            }
        }
        HasPackages = AllPackages.Count > 0;
    }

    [RelayCommand]
    private async Task RunDismGetFeaturesAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            IsLoadingFeatures = true;
            CurrentStatus = "Enumerating Windows features...";
            AppendOutput("[INFO] Querying Windows features via DISM (Populating interactive UI list)...");

            var (features, rawOutput, error, exitCode) = await _repairService.GetFeaturesStructuredAsync();

            if (exitCode == 740)
            {
                AppendOutput("[ERROR] DISM 權限不足 (錯誤碼 740: 需要系統管理員權限)！請以「系統管理員身分」重新啟動 DiskMaster！");
                CurrentStatus = "需要系統管理員權限 (Error 740)";
                return;
            }

            if (exitCode != 0)
            {
                AppendOutput($"[ERROR] DISM 查詢失敗 (Exit Code: {exitCode}): {error}");
                CurrentStatus = $"查詢失敗 (Exit Code: {exitCode})";
                return;
            }

            AllFeatures.Clear();
            foreach (var f in features)
            {
                AllFeatures.Add(f);
            }
            ApplyFeatureFilter();

            if (AllFeatures.Count > 0)
            {
                AppendOutput($"[SUCCESS] 成功載入 {AllFeatures.Count} 項 Windows 功能至互動清單。");
                CurrentStatus = $"已載入 {AllFeatures.Count} 項功能。";
            }
            else
            {
                AppendOutput("[WARNING] DISM 執行完成但未解析到功能項目。可於下方手動輸入功能名稱進行開關。");
                CurrentStatus = "未找到功能項目。";
            }
        }
        catch (Exception ex)
        {
            AppendOutput($"[ERROR] Failed to enumerate features: {ex.Message}");
        }
        finally
        {
            IsLoadingFeatures = false;
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task ToggleFeatureStateAsync(WindowsFeatureItem? item)
    {
        if (item == null || IsRunning) return;
        FeatureName = item.FeatureName;
        if (item.IsEnabled)
        {
            await RunDismDisableFeatureAsync();
        }
        else
        {
            await RunDismEnableFeatureAsync();
        }
        await RunDismGetFeaturesAsync();
    }

    [RelayCommand]
    private async Task RunDismEnableFeatureAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(FeatureName)) return;
        try
        {
            IsRunning = true;
            CurrentStatus = $"Enabling feature: {FeatureName}...";
            _cts = new CancellationTokenSource();
            var src = string.IsNullOrWhiteSpace(FeatureSourcePath) ? null : FeatureSourcePath;
            await _repairService.RunDismEnableFeatureAsync(FeatureName, src, AppendOutput, _cts.Token);
            CurrentStatus = $"Feature {FeatureName} enabled.";
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task RunDismDisableFeatureAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(FeatureName)) return;
        try
        {
            IsRunning = true;
            CurrentStatus = $"Disabling feature: {FeatureName}...";
            _cts = new CancellationTokenSource();
            await _repairService.RunDismDisableFeatureAsync(FeatureName, AppendOutput, _cts.Token);
            CurrentStatus = $"Feature {FeatureName} disabled.";
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task RunDismGetPackagesAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            IsLoadingPackages = true;
            CurrentStatus = "Enumerating installed packages...";
            AppendOutput("[INFO] Querying installed packages via DISM (Populating interactive UI list)...");

            var (packages, rawOutput, error, exitCode) = await _repairService.GetPackagesStructuredAsync();

            if (exitCode == 740)
            {
                AppendOutput("[ERROR] DISM 權限不足 (錯誤碼 740: 需要系統管理員權限)！請以「系統管理員身分」重新啟動 DiskMaster！");
                CurrentStatus = "需要系統管理員權限 (Error 740)";
                return;
            }

            if (exitCode != 0)
            {
                AppendOutput($"[ERROR] DISM 查詢失敗 (Exit Code: {exitCode}): {error}");
                CurrentStatus = $"查詢失敗 (Exit Code: {exitCode})";
                return;
            }

            AllPackages.Clear();
            foreach (var p in packages)
            {
                AllPackages.Add(p);
            }
            ApplyPackageFilter();

            if (AllPackages.Count > 0)
            {
                AppendOutput($"[SUCCESS] 成功載入 {AllPackages.Count} 項已安裝更新套件至互動清單。");
                CurrentStatus = $"已載入 {AllPackages.Count} 項套件。";
            }
            else
            {
                AppendOutput("[WARNING] DISM 執行完成但未解析到更新套件項目。");
                CurrentStatus = "未找到套件項目。";
            }
        }
        catch (Exception ex)
        {
            AppendOutput($"[ERROR] Failed to enumerate packages: {ex.Message}");
        }
        finally
        {
            IsLoadingPackages = false;
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task RemovePackageDirectAsync(WindowsPackageItem? item)
    {
        if (item == null || IsRunning) return;
        PackageName = item.PackageIdentity;
        await RunDismRemovePackageAsync();
        await RunDismGetPackagesAsync();
    }

    [RelayCommand]
    private async Task RunDismRemovePackageAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(PackageName)) return;
        try
        {
            IsRunning = true;
            CurrentStatus = $"Removing package: {PackageName}...";
            _cts = new CancellationTokenSource();
            await _repairService.RunDismRemovePackageAsync(PackageName, AppendOutput, _cts.Token);
            CurrentStatus = "Package removed.";
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task RunDismExportDriversAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(DriverExportDir)) return;
        try
        {
            IsRunning = true;
            CurrentStatus = $"Exporting drivers to {DriverExportDir}...";
            _cts = new CancellationTokenSource();
            await _repairService.RunDismExportDriversAsync(DriverExportDir, AppendOutput, _cts.Token);
            CurrentStatus = "Driver export complete.";
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task RunDismOfflineRestoreHealthAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(OfflineWinDir)) return;
        try
        {
            IsRunning = true;
            CurrentStatus = $"Starting offline DISM on {OfflineWinDir}...";
            _cts = new CancellationTokenSource();
            await _repairService.RunDismOfflineRestoreHealthAsync(OfflineWinDir, AppendOutput, _cts.Token);
            CurrentStatus = "Offline DISM finished.";
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task RunSfcOfflineAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(OfflineBootDir) || string.IsNullOrWhiteSpace(OfflineWinDir)) return;
        try
        {
            IsRunning = true;
            CurrentStatus = $"Starting offline SFC (Boot: {OfflineBootDir}, Win: {OfflineWinDir})...";
            _cts = new CancellationTokenSource();
            await _repairService.RunSfcOfflineAsync(OfflineBootDir, OfflineWinDir, AppendOutput, _cts.Token);
            CurrentStatus = "Offline SFC scan finished.";
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            AppendOutput("[INFO] Requesting cancellation...");
            _cts.Cancel();
        }
    }

    [RelayCommand]
    private void ClearTerminal()
    {
        _logBuffer.Clear();
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        AdminHelper.RestartAsAdmin();
    }

    // ── Windows Update Repair Commands ──

    [RelayCommand]
    public async Task RefreshWindowsUpdateStatusAsync()
    {
        try
        {
            var info = await Task.Run(() => _wuService.GetUpdateStatusAsync());
            DispatcherHelper.RunOnUIThread(() =>
            {
                WuInfo = info;
            });
        }
        catch { }
    }

    [RelayCommand]
    public async Task FixWindowsUpdateErrorsAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            IsWuUpdating = true;
            CurrentStatus = "正在修復 Windows Update 核心錯誤...";
            StatusBarViewModel.Instance.StartOperation("修復 Windows Update 錯誤", CancelOperation);
            _cts = new CancellationTokenSource();
            await _wuService.FixWindowsUpdateErrorsAsync(AppendOutput, _cts.Token);
            CurrentStatus = "Windows Update 修復完成。";
            StatusBarViewModel.Instance.CompleteOperation("Windows Update 修復完成。");
            await RefreshWindowsUpdateStatusAsync();
        }
        finally
        {
            IsRunning = false;
            IsWuUpdating = false;
        }
    }

    [RelayCommand]
    public async Task ResetSoftwareDistributionAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            IsWuUpdating = true;
            CurrentStatus = "正在重置 SoftwareDistribution 與 Catroot2 資料夾...";
            StatusBarViewModel.Instance.StartOperation("重建 SoftwareDistribution", CancelOperation);
            _cts = new CancellationTokenSource();
            await _wuService.ResetSoftwareDistributionFolderAsync(BackupSoftwareDist, AppendOutput, _cts.Token);
            CurrentStatus = "SoftwareDistribution 重建完成。";
            StatusBarViewModel.Instance.CompleteOperation("SoftwareDistribution 重建完成。");
            await RefreshWindowsUpdateStatusAsync();
            await ScanTempCategoriesAsync();
        }
        finally
        {
            IsRunning = false;
            IsWuUpdating = false;
        }
    }

    [RelayCommand]
    public async Task ToggleWindowsUpdateAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            bool targetDisable = !WuInfo.IsDisabled;
            CurrentStatus = targetDisable ? "正在停用 Windows Update..." : "正在啟用 Windows Update...";
            await _wuService.SetWindowsUpdateDisabledAsync(targetDisable, AppendOutput);
            await RefreshWindowsUpdateStatusAsync();
            CurrentStatus = "Windows Update 狀態切換完成。";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    public async Task TriggerUpdateCheckAsync()
    {
        try
        {
            await _wuService.TriggerUpdateCheckAsync(AppendOutput);
        }
        catch (Exception ex)
        {
            AppendOutput($"[ERROR] 觸發檢查更新失敗: {ex.Message}");
        }
    }

    // ── Deep Temp & Cache Cleaning Commands ──

    [RelayCommand]
    public async Task ScanTempCategoriesAsync()
    {
        if (IsScanningTemp) return;
        try
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsScanningTemp = true;
                CurrentStatus = "正在掃描系統與使用者暫存空間...";
            });

            var items = await Task.Run(() => _tempCleanService.ScanTempCategoriesAsync());

            DispatcherHelper.RunOnUIThread(() =>
            {
                TempCategories.Clear();
                long total = 0;
                foreach (var item in items)
                {
                    TempCategories.Add(item);
                    total += item.SizeBytes;
                }

                TotalScannedTempBytes = total;
                TotalScannedTempDisplay = FormatBytes(total);
                CurrentStatus = $"暫存掃描完成，共計 {TotalScannedTempDisplay} 可清理空間。";
            });
        }
        catch (Exception ex)
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                AppendOutput($"[ERROR] 掃描暫存失敗: {ex.Message}");
            });
        }
        finally
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsScanningTemp = false;
            });
        }
    }

    [RelayCommand]
    public async Task CleanSelectedTempCategoriesAsync()
    {
        if (IsRunning || IsCleaningTemp) return;
        try
        {
            var selectedIds = TempCategories.Where(x => x.IsSelected).Select(x => x.Id).ToList();
            if (selectedIds.Count == 0)
            {
                AppendOutput("[INFO] 請先勾選欲清理的暫存項目。");
                return;
            }

            IsRunning = true;
            IsCleaningTemp = true;
            CurrentStatus = "正在清理選取的系統暫存檔案...";
            StatusBarViewModel.Instance.StartOperation("清理系統暫存", CancelOperation);
            _cts = new CancellationTokenSource();

            var (files, bytes, display) = await _tempCleanService.CleanSelectedCategoriesAsync(selectedIds, AppendOutput, _cts.Token);
            CurrentStatus = $"暫存清理完成！釋放 {display}";
            StatusBarViewModel.Instance.CompleteOperation($"暫存清理完成 (釋放 {display})");

            await ScanTempCategoriesAsync();
            await RefreshWindowsUpdateStatusAsync();
        }
        finally
        {
            IsRunning = false;
            IsCleaningTemp = false;
        }
    }

    [RelayCommand]
    public async Task CleanAllTempCategoriesAsync()
    {
        foreach (var c in TempCategories) c.IsSelected = true;
        await CleanSelectedTempCategoriesAsync();
    }

    [RelayCommand]
    public void ToggleSelectAllTemp()
    {
        bool target = IsAllTempSelected;
        foreach (var c in TempCategories)
        {
            c.IsSelected = target;
        }
    }

    [RelayCommand]
    public async Task RunCleanmgrAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            CurrentStatus = "正在執行 Windows 原生磁碟清理工具...";
            StatusBarViewModel.Instance.StartOperation("Windows 磁碟清理", CancelOperation);
            _cts = new CancellationTokenSource();
            await _tempCleanService.RunCleanmgrAutomatedAsync(AppendOutput, _cts.Token);
            CurrentStatus = "Windows 磁碟清理完成。";
            StatusBarViewModel.Instance.CompleteOperation("磁碟清理完成。");
            await ScanTempCategoriesAsync();
        }
        finally
        {
            IsRunning = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F2} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }
}
