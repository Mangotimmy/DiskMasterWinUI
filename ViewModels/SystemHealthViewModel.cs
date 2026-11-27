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

    // ── Configurable Windows Update & 0x800F0831 Repair Center ──
    public WindowsUpdateRepairOptions WuRepairOptions { get; } = new();
    public List<WindowsUpdateErrorPreset> WuErrorPresets { get; } = WindowsUpdateRepairService.GetErrorPresets();
    [ObservableProperty] private WindowsUpdateErrorPreset? _selectedWuPreset;
    [ObservableProperty] private string _selectedWuPresetDescription = "";

    public ObservableCollection<CbsDiagnosticFinding> CbsFindings { get; } = new();
    public ObservableCollection<WindowsUpdateHistoryItem> FailedUpdateHistory { get; } = new();
    [ObservableProperty] private bool _isScanningCbs;
    [ObservableProperty] private string _cbsScanSummary = "點擊「掃描 CBS.log 與更新歷程」以偵測 0x800F0831、失敗 KB 編號與缺失套件。";
    [ObservableProperty] private bool _hasCbsFindings;
    [ObservableProperty] private bool _hasFailedUpdateHistory;
    [ObservableProperty] private string _manualCatalogSearchQuery = "";

    // ── Deep Temp & Cache Cleaning ──
    public ObservableCollection<TempCategoryItem> TempCategories { get; } = new();
    [ObservableProperty] private bool _isScanningTemp;
    [ObservableProperty] private bool _isCleaningTemp;
    [ObservableProperty] private string _totalScannedTempDisplay = "0 B";
    [ObservableProperty] private long _totalScannedTempBytes;
    [ObservableProperty] private bool _isAllTempSelected = true;

    // Live Cleaning & Deletion Status
    [ObservableProperty] private string _cleaningStatusMessage = "";
    [ObservableProperty] private string _cleaningDetailMessage = "";
    [ObservableProperty] private double _cleaningProgressPercent;
    [ObservableProperty] private bool _isCleaningIndeterminate = true;
    [ObservableProperty] private bool _hasCleaningStatus;

    private bool _isInitialized;

    public SystemHealthViewModel()
    {
        _logBuffer = new ThrottledLogBuffer(text => TerminalOutput = text);
        IsAdmin = AdminHelper.IsRunningAsAdmin();
        BackupSoftwareDist = SettingsService.Instance.Current.WuRepairBackupSoftwareDist;
        WuRepairOptions.BackupSoftwareDistribution = BackupSoftwareDist;
        WuRepairOptions.PromptRebootAfterRepair = SettingsService.Instance.Current.WuRepairPromptReboot;
        SelectedWuPreset = WuErrorPresets.FirstOrDefault();

        AppendOutput("Windows System Repair Engine Initialized (SFC, DISM, WU 0x800F0831 Repair & Temp Cleaner).");
        if (!IsAdmin)
        {
            AppendOutput("[WARNING] SFC and DISM require Administrator privileges to repair system files.");
        }
    }

    partial void OnSelectedWuPresetChanged(WindowsUpdateErrorPreset? value)
    {
        if (value == null) return;
        SelectedWuPresetDescription = $"{value.Description}\n建議流程：{value.RecommendedAction}";
        WindowsUpdateRepairService.ApplyPresetToOptions(value.Id, WuRepairOptions);
        WuRepairOptions.BackupSoftwareDistribution = BackupSoftwareDist;
        WuRepairOptions.PromptRebootAfterRepair = SettingsService.Instance.Current.WuRepairPromptReboot;
    }

    partial void OnBackupSoftwareDistChanged(bool value)
    {
        WuRepairOptions.BackupSoftwareDistribution = value;
        SettingsService.Instance.Current.WuRepairBackupSoftwareDist = value;
        SettingsService.Instance.Save();
    }

    private CancellationTokenSource ResetCts()
    {
        try { _cts?.Dispose(); } catch { }
        _cts = new CancellationTokenSource();
        return _cts;
    }

    private static string FormatRepairOutcome(string label, int exitCode) => exitCode switch
    {
        0 => $"{label} 完成 (ExitCode=0)。",
        3010 => $"{label} 完成，需重新啟動電腦以套用修復 (ExitCode=3010)。",
        740 => $"{label} 失敗：需要系統管理員權限 (Error 740)。",
        unchecked((int)0x800F0831) => $"{label} 失敗 (0x800F0831 / CBS_E_STORE_CORRUPTION)：請掃描 CBS.log 找出缺失套件並從 Microsoft Update 目錄手動安裝。",
        unchecked((int)0x800F081F) => $"{label} 失敗 (0x800F081F)：找不到修復來源，請指定本機 WIM/ESD 映像來源。",
        -1 => $"{label} 已由使用者取消。",
        _ => $"{label} 回報錯誤 (ExitCode={exitCode})，請檢視下方終端機輸出。"
    };

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        await Task.WhenAll(
            RefreshWindowsUpdateStatusAsync(),
            ScanTempCategoriesAsync(),
            RefreshQuarantineVaultAsync());
    }

    [RelayCommand]
    public void ClearCleaningStatus()
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            CleaningStatusMessage = "";
            CleaningDetailMessage = "";
            CleaningProgressPercent = 0;
            IsCleaningIndeterminate = false;
            HasCleaningStatus = false;
        });
    }

    private void AppendOutput(string line)
    {
        StatusBarViewModel.Instance.ProcessOutputLine(line);
        _logBuffer.AppendLine(line);

        if (!string.IsNullOrWhiteSpace(line))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("▸") || trimmed.StartsWith("•") || trimmed.StartsWith("-") || trimmed.StartsWith("["))
            {
                var cleanText = trimmed.TrimStart('▸', '•', '-', ' ', '[').Trim();
                if (cleanText.Contains("]"))
                {
                    cleanText = cleanText.Substring(cleanText.IndexOf(']') + 1).Trim();
                }
                if (!string.IsNullOrWhiteSpace(cleanText))
                {
                    DispatcherHelper.RunOnUIThread(() =>
                    {
                        CleaningDetailMessage = cleanText;
                    });
                    StatusBarViewModel.Instance.UpdateOperationTitle(cleanText);
                }
            }
        }
    }

    [RelayCommand]
    private async Task RunOneClickRepairAsync()
    {
        if (IsRunning) return;
        DebugLogService.Instance.LogAction("系統健康 (SystemHealth)", "開始一鍵全自動系統修復 (DISM 三段 + SFC)");
        try
        {
            IsRunning = true;
            CurrentStatus = "正在執行 DISM (ScanHealth → CheckHealth → RestoreHealth) + SFC /scannow...";
            StatusBarViewModel.Instance.StartOperation("1-Click System Repair", CancelOperation);
            var cts = ResetCts();
            var result = await _repairService.RunFullDismSfcSequenceAsync(
                runScanHealth: true,
                runCheckHealth: true,
                runRestoreHealth: true,
                repairSource: null,
                runSfc: true,
                onOutput: AppendOutput,
                ct: cts.Token);
            CurrentStatus = result.Summary;
            StatusBarViewModel.Instance.CompleteOperation(result.Summary, success: result.Success);
            if (!result.Success && string.Equals(result.DetectedErrorCode, "0x800F0831", StringComparison.OrdinalIgnoreCase))
            {
                await ScanCbsAndUpdateHistoryAsync();
            }
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
        DebugLogService.Instance.LogAction("系統健康 (SystemHealth)", "啟動 SFC /scannow 系統檔案完整掃描與修復");
        try
        {
            IsRunning = true;
            CurrentStatus = "Running sfc /scannow...";
            StatusBarViewModel.Instance.StartOperation("sfc /scannow", CancelOperation);
            var cts = ResetCts();
            var code = await _repairService.RunSfcScanNowAsync(AppendOutput, cts.Token);
            var msg = FormatRepairOutcome("SFC /scannow", code);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: code == 0 || code == 3010);
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
            var cts = ResetCts();
            var code = await _repairService.RunSfcVerifyOnlyAsync(AppendOutput, cts.Token);
            var msg = FormatRepairOutcome("SFC /verifyonly", code);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: code == 0);
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
            var cts = ResetCts();
            var code = await _repairService.RunDismCheckHealthAsync(AppendOutput, cts.Token);
            var msg = FormatRepairOutcome("DISM /CheckHealth", code);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: code == 0);
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
            var cts = ResetCts();
            var code = await _repairService.RunDismScanHealthAsync(AppendOutput, cts.Token);
            var msg = FormatRepairOutcome("DISM /ScanHealth", code);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: code == 0);
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
        DebugLogService.Instance.LogAction("系統健康 (SystemHealth)", "執行 DISM RestoreHealth 線上映像還原修復");
        try
        {
            IsRunning = true;
            CurrentStatus = "Running dism /restorehealth...";
            StatusBarViewModel.Instance.StartOperation("dism /restorehealth", CancelOperation);
            var cts = ResetCts();
            var code = await _repairService.RunDismRestoreHealthAsync(AppendOutput, cts.Token);
            var msg = FormatRepairOutcome("DISM /RestoreHealth", code);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: code == 0 || code == 3010);
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
            var cts = ResetCts();
            var code = await _repairService.RunDismComponentCleanupAsync(AppendOutput, cts.Token);
            var msg = FormatRepairOutcome("DISM /StartComponentCleanup", code);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: code == 0 || code == 3010);
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
            var cts = ResetCts();
            var code = await _repairService.RunDismAnalyzeComponentStoreAsync(AppendOutput, cts.Token);
            var msg = FormatRepairOutcome("DISM /AnalyzeComponentStore", code);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: code == 0);
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
            var cts = ResetCts();
            var code = await _repairService.RunDismResetBaseAsync(AppendOutput, cts.Token);
            var msg = FormatRepairOutcome("DISM /ResetBase", code);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: code == 0 || code == 3010);
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
            var cts = ResetCts();
            var code = await _repairService.RunDismRestoreHealthWithSourceAsync(RepairSourcePath, AppendOutput, cts.Token);
            var msg = FormatRepairOutcome("DISM /RestoreHealth /Source", code);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: code == 0 || code == 3010);
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
            var cts = ResetCts();
            var src = string.IsNullOrWhiteSpace(FeatureSourcePath) ? null : FeatureSourcePath;
            var code = await _repairService.RunDismEnableFeatureAsync(FeatureName, src, AppendOutput, cts.Token);
            CurrentStatus = FormatRepairOutcome($"啟用功能 {FeatureName}", code);
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
            var cts = ResetCts();
            var code = await _repairService.RunDismDisableFeatureAsync(FeatureName, AppendOutput, cts.Token);
            CurrentStatus = FormatRepairOutcome($"停用功能 {FeatureName}", code);
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
            var cts = ResetCts();
            var code = await _repairService.RunDismRemovePackageAsync(PackageName, AppendOutput, cts.Token);
            CurrentStatus = FormatRepairOutcome($"移除套件 {PackageName}", code);
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
            var cts = ResetCts();
            var code = await _repairService.RunDismExportDriversAsync(DriverExportDir, AppendOutput, cts.Token);
            CurrentStatus = FormatRepairOutcome("驅動程式匯出", code);
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
            var cts = ResetCts();
            var code = await _repairService.RunDismOfflineRestoreHealthAsync(OfflineWinDir, AppendOutput, cts.Token);
            CurrentStatus = FormatRepairOutcome("離線 DISM 修復", code);
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
            var cts = ResetCts();
            var code = await _repairService.RunSfcOfflineAsync(OfflineBootDir, OfflineWinDir, AppendOutput, cts.Token);
            CurrentStatus = FormatRepairOutcome("離線 SFC 掃描", code);
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
            CurrentStatus = "正在執行自訂 Windows Update 與元件存放區修復流程...";
            StatusBarViewModel.Instance.StartOperation("修復 Windows Update 錯誤", CancelOperation);
            var cts = ResetCts();
            var (success, rebootRecommended, summary) = await _wuService.RunConfigurableRepairPipelineAsync(
                WuRepairOptions,
                _repairService,
                AppendOutput,
                cts.Token);
            CurrentStatus = summary;
            StatusBarViewModel.Instance.CompleteOperation(summary, success: success);
            await RefreshWindowsUpdateStatusAsync();

            if (!success)
            {
                await ScanCbsAndUpdateHistoryAsync();
            }
            else if (rebootRecommended && WuRepairOptions.PromptRebootAfterRepair)
            {
                await PromptRestartComputerAsync();
            }
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
            CurrentStatus = "正在重置 SoftwareDistribution 快取資料夾...";
            StatusBarViewModel.Instance.StartOperation("重建 SoftwareDistribution", CancelOperation);
            var cts = ResetCts();
            var code = await _wuService.ResetSoftwareDistributionFolderAsync(
                BackupSoftwareDist,
                AppendOutput,
                cts.Token,
                resetCatroot2: WuRepairOptions.ResetCatroot2WithCryptSvc);
            var msg = FormatRepairOutcome("SoftwareDistribution 重建", code);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: code == 0);
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
    public async Task ScanCbsAndUpdateHistoryAsync()
    {
        if (IsScanningCbs) return;
        try
        {
            IsScanningCbs = true;
            CbsScanSummary = "正在分析 C:\\Windows\\Logs\\CBS\\CBS.log 與 Windows 更新歷程...";
            AppendOutput($"[{DateTime.Now:HH:mm:ss}] 開始掃描 CBS.log 與 Windows 更新歷程 (尋找 0x800F0831 與失敗 KB)...");

            var cbsTask = _wuService.AnalyzeCbsLogAsync();
            var histTask = _wuService.GetFailedUpdateHistoryAsync();
            await Task.WhenAll(cbsTask, histTask);

            var (findings, cbsSummary) = await cbsTask;
            var failedHistory = await histTask;

            DispatcherHelper.RunOnUIThread(() =>
            {
                CbsFindings.Clear();
                foreach (var f in findings) CbsFindings.Add(f);
                HasCbsFindings = CbsFindings.Count > 0;

                FailedUpdateHistory.Clear();
                foreach (var h in failedHistory) FailedUpdateHistory.Add(h);
                HasFailedUpdateHistory = FailedUpdateHistory.Count > 0;

                WuInfo.FailedUpdateCount = failedHistory.Count;
                WuInfo.CbsCorruptionDetected = findings.Any(f =>
                    f.ErrorCode.Contains("0x800F0831", StringComparison.OrdinalIgnoreCase) ||
                    f.Category.Contains("0x800F0831", StringComparison.OrdinalIgnoreCase) ||
                    f.Category.Contains("缺失套件", StringComparison.OrdinalIgnoreCase));

                CbsScanSummary = $"{cbsSummary}｜更新歷程發現 {failedHistory.Count} 筆失敗/中止項目。";
            });

            AppendOutput($"[CBS 診斷] {cbsSummary} (失敗更新歷程: {failedHistory.Count} 筆)");
        }
        catch (Exception ex)
        {
            CbsScanSummary = $"掃描失敗: {ex.Message}";
            AppendOutput($"[ERROR] CBS / 更新歷程掃描失敗: {ex.Message}");
        }
        finally
        {
            IsScanningCbs = false;
        }
    }

    [RelayCommand]
    public void OpenCatalogForQuery(string? query)
    {
        try
        {
            var effective = string.IsNullOrWhiteSpace(query) ? ManualCatalogSearchQuery : query;
            WindowsUpdateRepairService.OpenMicrosoftUpdateCatalog(effective);
            AppendOutput($"[INFO] 已開啟 Microsoft Update 目錄搜尋: {(string.IsNullOrWhiteSpace(effective) ? "(首頁)" : effective)}");
        }
        catch (Exception ex)
        {
            AppendOutput($"[ERROR] 無法開啟瀏覽器: {ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenTroubleshootGuide()
    {
        try
        {
            WindowsUpdateRepairService.OpenMicrosoftTroubleshootGuide(SelectedWuPreset?.Code ?? "0x800F0831");
            AppendOutput("[INFO] 已開啟 Microsoft 官方 0x800F0831 / Windows Update 疑難排解說明頁面。");
        }
        catch (Exception ex)
        {
            AppendOutput($"[ERROR] 無法開啟官方說明: {ex.Message}");
        }
    }

    [RelayCommand]
    public void LaunchWuTroubleshooter()
    {
        try
        {
            WindowsUpdateRepairService.LaunchWindowsUpdateTroubleshooter();
            AppendOutput("[INFO] 已啟動 Windows 設定 → 疑難排解 (Windows Update 疑難排解員)。");
        }
        catch (Exception ex)
        {
            AppendOutput($"[ERROR] 無法啟動 Windows 更新疑難排解員: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task InstallLocalUpdatePackageAsync()
    {
        if (IsRunning) return;
        try
        {
            var filePath = await FilePickerHelper.PickSingleFileAsync(
                [".msu", ".cab"],
                "選擇從 Microsoft Update 目錄下載的更新套件 (.msu / .cab)");
            if (string.IsNullOrWhiteSpace(filePath)) return;

            IsRunning = true;
            IsWuUpdating = true;
            CurrentStatus = $"正在安裝本機更新套件: {Path.GetFileName(filePath)}...";
            StatusBarViewModel.Instance.StartOperation("安裝本機更新套件", CancelOperation);
            var cts = ResetCts();
            var (ok, exitCode, msg) = await _wuService.InstallLocalUpdatePackageAsync(filePath, AppendOutput, cts.Token);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: ok);
            await RefreshWindowsUpdateStatusAsync();
            if (ok && exitCode == 3010)
            {
                await PromptRestartComputerAsync();
            }
        }
        finally
        {
            IsRunning = false;
            IsWuUpdating = false;
        }
    }

    [RelayCommand]
    public async Task RestoreTrustedInstallerDefaultAsync()
    {
        if (IsRunning) return;
        try
        {
            IsRunning = true;
            var (ok, msg) = await _wuService.RestoreTrustedInstallerDefaultAsync(AppendOutput);
            CurrentStatus = msg;
            StatusBarViewModel.Instance.CompleteOperation(msg, success: ok);
            await RefreshWindowsUpdateStatusAsync();
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    public async Task PromptRestartComputerAsync()
    {
        bool confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "確認重新啟動電腦",
            "Windows Update / DISM / SFC 修復通常需要重新啟動電腦才能完成元件替換與待處理更新。\n\n確認後系統將於 15 秒內重新啟動，請先儲存未存檔的工作。",
            "立即排程 15 秒後重開機 (shutdown /r /t 15)");
        if (!confirmed) return;

        await ProcessHelper.RunProcessAsync("shutdown.exe", "/r /t 15 /c \"DiskMaster Pro Windows Update Repair Reboot\"");
        AppendOutput("[INFO] 已排程 15 秒後重新啟動電腦（可執行 shutdown /a 取消）。");
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

            // Anything irreversible (Windows.old rollback image, component-store logs, upgrade
            // payloads) needs an explicit extra confirmation, even though the user ticked it.
            var risky = TempCategories.Where(x => x.IsSelected && x.IsRisky).ToList();
            if (risky.Count > 0)
            {
                long riskyBytes = risky.Sum(r => r.SizeBytes);
                string names = string.Join("、", risky.Select(r => r.Name));
                bool confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
                    WindowHelper.GetXamlRoot(),
                    "確認清理不可復原項目",
                    $"下列項目一旦刪除即無法復原，或會影響已安裝的程式與系統回復能力：\n\n{names}\n\n" +
                    $"預估釋放約 {FormatBytes(riskyBytes)}。軟體安裝套件快取 (Package Cache) 已永久封鎖刪除。確定要繼續嗎？",
                    $"{risky.Count} 個不可復原項目");
                if (!confirmed)
                {
                    AppendOutput("[INFO] 已取消：使用者未確認高風險項目。");
                    return;
                }
            }

            DispatcherHelper.RunOnUIThread(() =>
            {
                IsRunning = true;
                IsCleaningTemp = true;
                IsCleaningIndeterminate = false;
                CleaningProgressPercent = 0;
                CleaningStatusMessage = "正在清理選取的系統暫存檔案...";
                CleaningDetailMessage = "正在初始化清理項目...";
                HasCleaningStatus = true;
                CurrentStatus = "正在清理選取的系統暫存檔案...";
            });
            StatusBarViewModel.Instance.StartOperation("清理系統暫存", CancelOperation);
            var cts = ResetCts();

            var (files, bytes, display) = await _tempCleanService.CleanSelectedCategoriesAsync(
                selectedIds,
                AppendOutput,
                cts.Token,
                (cur, total, name) =>
                {
                    DispatcherHelper.RunOnUIThread(() =>
                    {
                        CleaningProgressPercent = (double)cur / total * 100.0;
                        CleaningStatusMessage = $"正在清理: {name} ({cur}/{total})";
                    });
                    StatusBarViewModel.Instance.UpdateProgressExplicit((double)cur / total * 100.0);
                });

            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = $"暫存清理完成！釋放 {display}";
                CleaningStatusMessage = $"暫存清理完成！共釋放 {display}";
                CleaningDetailMessage = $"共刪除 {files:N0} 個檔案，磁碟空間已即時更新。";
                CleaningProgressPercent = 100;
                IsCleaningIndeterminate = false;
            });
            StatusBarViewModel.Instance.CompleteOperation($"暫存清理完成 (釋放 {display})");

            await ScanTempCategoriesAsync();
            await RefreshWindowsUpdateStatusAsync();
            await RefreshQuarantineVaultAsync();
        }
        catch (OperationCanceledException)
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = "暫存清理作業已由使用者取消。";
                CleaningStatusMessage = "暫存清理已由使用者取消。";
                CleaningDetailMessage = "已安全終止清理處理序。";
                IsCleaningIndeterminate = false;
            });
            StatusBarViewModel.Instance.CompleteOperation("Operation Cancelled.", success: false, notify: false);
            AppendOutput("\n[已取消] 暫存清理作業已由使用者終止。");
        }
        catch (Exception ex)
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = $"暫存清理發生錯誤: {ex.Message}";
                CleaningStatusMessage = $"清理發生錯誤: {ex.Message}";
                CleaningDetailMessage = ex.ToString();
                IsCleaningIndeterminate = false;
            });
            StatusBarViewModel.Instance.CompleteOperation($"暫存清理失敗: {ex.Message}", success: false);
            AppendOutput($"\n[錯誤] 暫存清理發生例外狀況: {ex.Message}");
            DebugLogService.Instance.Error("暫存清理失敗", ex, "SystemHealth");
        }
        finally
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsRunning = false;
                IsCleaningTemp = false;
            });
        }
    }

    [RelayCommand]
    public async Task CleanAllTempCategoriesAsync()
    {
        // Risky categories are never included in "clean all": they are destructive and must be
        // ticked individually (and confirmed) by the user.
        foreach (var c in TempCategories) c.IsSelected = !c.IsRisky;
        await CleanSelectedTempCategoriesAsync();
    }

    [RelayCommand]
    public void ToggleSelectAllTemp()
    {
        bool target = IsAllTempSelected;
        foreach (var c in TempCategories)
        {
            c.IsSelected = target && !c.IsRisky;
        }
    }

    [RelayCommand]
    public async Task CleanTempFilesOnlyAsync()
    {
        if (IsRunning || IsCleaningTemp) return;
        try
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsRunning = true;
                IsCleaningTemp = true;
                IsCleaningIndeterminate = true;
                CleaningStatusMessage = "正在清理系統與使用者暫存檔案 (%TEMP% & Windows Temp)...";
                CleaningDetailMessage = "正在刪除使用者與系統暫存目錄暫存檔案...";
                HasCleaningStatus = true;
                CurrentStatus = "正在清理系統與使用者暫存檔案 (%TEMP% & Windows Temp)...";
            });
            StatusBarViewModel.Instance.StartOperation("清理暫存檔案", CancelOperation);
            var cts = ResetCts();
            var (files, bytes, display) = await _tempCleanService.CleanTempFilesAsync(AppendOutput, cts.Token);
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = $"暫存檔案清理完成！釋放 {display}";
                CleaningStatusMessage = $"暫存檔案清理完成！釋放 {display}";
                CleaningDetailMessage = $"成功清除 {files:N0} 個暫存檔案。";
                CleaningProgressPercent = 100;
                IsCleaningIndeterminate = false;
            });
            StatusBarViewModel.Instance.CompleteOperation($"暫存清理完成 (釋放 {display})");
            await ScanTempCategoriesAsync();
        }
        catch (OperationCanceledException)
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = "暫存清理已由使用者取消。";
                CleaningStatusMessage = "暫存清理已由使用者取消。";
                CleaningDetailMessage = "已安全終止暫存清理。";
                IsCleaningIndeterminate = false;
            });
            StatusBarViewModel.Instance.CompleteOperation("Operation Cancelled.", success: false, notify: false);
            AppendOutput("\n[已取消] 暫存檔案清理已終止。");
        }
        catch (Exception ex)
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = $"清理暫存發生錯誤: {ex.Message}";
                CleaningStatusMessage = $"清理發生錯誤: {ex.Message}";
                CleaningDetailMessage = ex.ToString();
                IsCleaningIndeterminate = false;
            });
            StatusBarViewModel.Instance.CompleteOperation($"清理失敗: {ex.Message}", success: false);
            AppendOutput($"\n[錯誤] 暫存清理失敗: {ex.Message}");
            DebugLogService.Instance.Error("暫存清理失敗", ex, "SystemHealth");
        }
        finally
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsRunning = false;
                IsCleaningTemp = false;
            });
        }
    }

    [RelayCommand]
    public async Task CleanWindowsOldOnlyAsync()
    {
        if (IsRunning || IsCleaningTemp) return;

        bool confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "確認清理不可復原備份 (Windows.old)",
            "刪除 Windows.old 與系統升級安裝檔 ($WINDOWS.~BT) 後，將無法透過 Windows 回復功能降級回先前版本。確定要繼續嗎？",
            "Windows.old 系統回復備份");
        if (!confirmed)
        {
            AppendOutput("[INFO] 已取消：使用者未確認刪除 Windows.old。");
            return;
        }

        try
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsRunning = true;
                IsCleaningTemp = true;
                IsCleaningIndeterminate = true;
                CleaningStatusMessage = "正在清理舊版 Windows 備份目錄 (Windows.old)...";
                CleaningDetailMessage = "正在啟用 64 核心多執行緒極速清除 Windows.old...";
                HasCleaningStatus = true;
                CurrentStatus = "正在清理舊版 Windows 備份目錄 (Windows.old)...";
            });
            StatusBarViewModel.Instance.StartOperation("清理 Windows.old", CancelOperation);
            var cts = ResetCts();
            var (files, bytes, display) = await _tempCleanService.CleanWindowsOldAsync(AppendOutput, cts.Token);
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = $"Windows.old 清理完成！釋放 {display}";
                CleaningStatusMessage = $"Windows.old 清理完成！釋放 {display}";
                CleaningDetailMessage = $"成功清除 Windows.old 備份檔案 ({files:N0} 個檔案)。";
                CleaningProgressPercent = 100;
                IsCleaningIndeterminate = false;
            });
            StatusBarViewModel.Instance.CompleteOperation($"Windows.old 清理完成 (釋放 {display})");
            await ScanTempCategoriesAsync();
        }
        catch (OperationCanceledException)
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = "Windows.old 清理已由使用者取消。";
                CleaningStatusMessage = "Windows.old 清理已由使用者取消。";
                CleaningDetailMessage = "已安全終止 Windows.old 清理處理序。";
                IsCleaningIndeterminate = false;
            });
            StatusBarViewModel.Instance.CompleteOperation("Operation Cancelled.", success: false, notify: false);
            AppendOutput("\n[已取消] Windows.old 清理作業已終止。");
        }
        catch (Exception ex)
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = $"清理 Windows.old 發生錯誤: {ex.Message}";
                CleaningStatusMessage = $"清理發生錯誤: {ex.Message}";
                CleaningDetailMessage = ex.ToString();
                IsCleaningIndeterminate = false;
            });
            StatusBarViewModel.Instance.CompleteOperation($"清理失敗: {ex.Message}", success: false);
            AppendOutput($"\n[錯誤] Windows.old 清理失敗: {ex.Message}");
            DebugLogService.Instance.Error("Windows.old 清理失敗", ex, "SystemHealth");
        }
        finally
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsRunning = false;
                IsCleaningTemp = false;
            });
        }
    }

    [RelayCommand]
    public async Task CleanAppCacheOnlyAsync()
    {
        if (IsRunning || IsCleaningTemp) return;
        try
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsRunning = true;
                IsCleaningTemp = true;
                IsCleaningIndeterminate = true;
                CleaningStatusMessage = "正在清理應用程式與瀏覽器快取 (Edge/Chrome/Shader)...";
                CleaningDetailMessage = "正在檢索瀏覽器與 GPU Shader 快取目錄...";
                HasCleaningStatus = true;
                CurrentStatus = "正在清理應用程式與瀏覽器快取 (Edge/Chrome/Shader)...";
            });
            StatusBarViewModel.Instance.StartOperation("清理應用程式快取", CancelOperation);
            var cts = ResetCts();
            var (files, bytes, display) = await _tempCleanService.CleanAppCacheAsync(AppendOutput, cts.Token);
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = $"應用程式快取清理完成！釋放 {display}";
                CleaningStatusMessage = $"應用程式快取清理完成！釋放 {display}";
                CleaningDetailMessage = $"成功清理 {files:N0} 個應用程式快取檔案。";
                CleaningProgressPercent = 100;
                IsCleaningIndeterminate = false;
            });
            StatusBarViewModel.Instance.CompleteOperation($"App快取清理完成 (釋放 {display})");
            await ScanTempCategoriesAsync();
        }
        catch (OperationCanceledException)
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = "App 快取清理已由使用者取消。";
                CleaningStatusMessage = "App 快取清理已由使用者取消。";
                CleaningDetailMessage = "已安全終止快取清理作業。";
                IsCleaningIndeterminate = false;
            });
            StatusBarViewModel.Instance.CompleteOperation("Operation Cancelled.", success: false, notify: false);
            AppendOutput("\n[已取消] 應用程式快取清理作業已終止。");
        }
        catch (Exception ex)
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = $"清理 App 快取發生錯誤: {ex.Message}";
                CleaningStatusMessage = $"清理發生錯誤: {ex.Message}";
                CleaningDetailMessage = ex.ToString();
                IsCleaningIndeterminate = false;
            });
            StatusBarViewModel.Instance.CompleteOperation($"清理失敗: {ex.Message}", success: false);
            AppendOutput($"\n[錯誤] App 快取清理失敗: {ex.Message}");
            DebugLogService.Instance.Error("App 快取清理失敗", ex, "SystemHealth");
        }
        finally
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsRunning = false;
                IsCleaningTemp = false;
            });
        }
    }

    [RelayCommand]
    public async Task RunCleanmgrAsync()
    {
        if (IsRunning) return;
        try
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsRunning = true;
                CurrentStatus = "正在執行 Windows 原生磁碟清理工具...";
            });
            StatusBarViewModel.Instance.StartOperation("Windows 磁碟清理", CancelOperation);
            var cts = ResetCts();
            await _tempCleanService.RunCleanmgrAutomatedAsync(AppendOutput, cts.Token);
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = "Windows 磁碟清理完成。";
            });
            StatusBarViewModel.Instance.CompleteOperation("磁碟清理完成。");
            await ScanTempCategoriesAsync();
        }
        catch (OperationCanceledException)
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = "Windows 磁碟清理已取消。";
            });
            StatusBarViewModel.Instance.CompleteOperation("Operation Cancelled.", success: false, notify: false);
            AppendOutput("\n[已取消] 磁碟清理已由使用者終止。");
        }
        catch (Exception ex)
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                CurrentStatus = $"磁碟清理發生錯誤: {ex.Message}";
            });
            StatusBarViewModel.Instance.CompleteOperation($"磁碟清理失敗: {ex.Message}", success: false);
            AppendOutput($"\n[錯誤] 磁碟清理失敗: {ex.Message}");
            DebugLogService.Instance.Error("Cleanmgr 失敗", ex, "SystemHealth");
        }
        finally
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsRunning = false;
            });
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
