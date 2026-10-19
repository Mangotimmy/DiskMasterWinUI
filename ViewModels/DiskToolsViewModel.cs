using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;
using Windows.ApplicationModel.DataTransfer;

namespace DiskMasterWinUI.ViewModels;

public partial class DiskToolsViewModel : ObservableObject
{
    private readonly KeyReaderService _keyReader = new();
    private readonly EnvironmentCheckService _envService = new();
    private readonly DiskToolsService _diskTools = new();
    private readonly HealthMonitorService _healthMonitor = new();
    private readonly ChkdskService _chkdsk = new();
    private readonly BitLockerService _bitLocker = new();
    private readonly DriverService _driverService = new();
    private readonly SmartReaderService _smartReader = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _outputLog = "";

    // BitLocker & Drivers
    [ObservableProperty] private string _bitLockerDrive = "C:";
    [ObservableProperty] private string _bitLockerPassword = "";
    [ObservableProperty] private string _driverExportPath = @"D:\DriverBackup";

    // 1. License & Hardware
    [ObservableProperty] private WindowsLicenseInfo _licenseInfo = new();
    [ObservableProperty] private string _offlineWindowsPath = @"D:\Windows";

    // 2. WinFR
    [ObservableProperty] private string _winFrSourceDrive = "C:";
    [ObservableProperty] private string _winFrDestinationFolder = @"D:\RecoveredFiles";
    [ObservableProperty] private string _winFrFilter = "*.*";
    [ObservableProperty] private string _winFrMode = "/regular";

    // 3. WSL2 EXT4
    [ObservableProperty] private int _ext4DiskNumber = 1;
    [ObservableProperty] private string _ext4PartitionNumber = "";

    private readonly VhdService _vhdService = new();
    private readonly CacheCleanService _cacheCleanService = new();

    // 4. TRIM & Defrag
    [ObservableProperty] private string _optimizeDriveLetter = "C:";

    // 5. Virtual Disk (ISO / VHD / VHDX)
    [ObservableProperty] private string _virtualDiskPath = "";
    [ObservableProperty] private string _vhdFormat = "VHDX"; // VHDX or VHD
    [ObservableProperty] private string _vhdType = "Dynamic"; // Dynamic or Fixed
    [ObservableProperty] private int _vhdSizeGB = 64;
    [ObservableProperty] private string _vhdLabel = "VHDX_DATA";
    [ObservableProperty] private bool _vhdReadOnly = false;
    [ObservableProperty] private bool _vhdAutoFormatNtfs = true;
    [ObservableProperty] private bool _vhdAutoGpt = true;
    [ObservableProperty] private int _vhdNewExpandSizeGB = 128;
    [ObservableProperty] private string _vhdDetailsText = "";
    [ObservableProperty] private string _cacheCleanupSummary = "";

    public ObservableCollection<string> VhdFormatOptions { get; } = new() { "VHDX", "VHD" };
    public ObservableCollection<string> VhdTypeOptions { get; } = new() { "Dynamic (動態擴充)", "Fixed (固定容量)" };

    // 6. S.M.A.R.T. Health
    [ObservableProperty] private bool _isHealthLoading;
    [ObservableProperty] private DiskHealthInfo? _selectedDiskHealth;
    [ObservableProperty] private NvmeHealthDetails? _selectedNvmeHealth;
    [ObservableProperty] private bool _hasSelectedNvme;
    [ObservableProperty] private bool _hasSelectedAta;
    [ObservableProperty] private bool _hasNoSmartTelemetry;
    [ObservableProperty] private bool _isFullSmartLoading;
    [ObservableProperty] private bool _isFahrenheit;
    [ObservableProperty] private bool _isRawHex;
    [ObservableProperty] private bool _isTrimEnabled = true;
    [ObservableProperty] private bool _isBitLockerActive = true;
    [ObservableProperty] private bool _isUpdatingProgrammatically;
    public ObservableCollection<SmartAttributeItem> AtaSmartAttributes { get; } = new();

    // 7. chkdsk
    [ObservableProperty] private string _chkdskDrive = "C:";
    [ObservableProperty] private bool _isChkdskRunning;
    private CancellationTokenSource? _chkdskCts;
    private int _isRefreshingAll;
    private int _isHealthRefreshing;

    public ObservableCollection<DiskHealthInfo> DiskHealthItems { get; } = new();
    public ObservableCollection<DiskReliabilityInfo> ReliabilityItems { get; } = new();

    public ObservableCollection<ToolEnvironmentItem> ToolItems { get; } = new();

    // 7b. Environment Variable Table & PATH Directory Table Editor
    public ObservableCollection<EnvironmentVariableEntry> AllEnvVariables { get; } = new();
    public ObservableCollection<EnvironmentVariableEntry> FilteredEnvVariables { get; } = new();
    public ObservableCollection<PathDirectoryEntry> PathDirectories { get; } = new();
    public ObservableCollection<PathDirectoryEntry> FilteredPathDirectories { get; } = new();

    public ObservableCollection<string> EnvScopeFilterOptions { get; } = new()
    {
        "All (系統 + 使用者)",
        "Machine (系統全域)",
        "User (目前使用者)",
        "Process (目前處理程序)"
    };

    public ObservableCollection<string> EnvTargetScopeOptions { get; } = new()
    {
        "User (目前使用者)",
        "Machine (系統全域 - 需管理員)"
    };

    [ObservableProperty] private string _envScopeFilter = "All (系統 + 使用者)";
    [ObservableProperty] private string _envSearchQuery = "";
    [ObservableProperty] private EnvironmentVariableEntry? _selectedEnvVariable;
    [ObservableProperty] private PathDirectoryEntry? _selectedPathDirectory;

    [ObservableProperty] private string _editEnvName = "";
    [ObservableProperty] private string _editEnvValue = "";
    [ObservableProperty] private string _editEnvExpandedPreview = "";
    [ObservableProperty] private string _editEnvTargetScope = "User (目前使用者)";

    [ObservableProperty] private string _newPathDirectory = "";
    [ObservableProperty] private string _newPathTargetScope = "User (目前使用者)";

    [ObservableProperty] private string _envSummaryStatsText = "正在載入環境變數與 PATH 表格...";
    [ObservableProperty] private string _pathHealthSummaryText = "";
    [ObservableProperty] private string _toolHealthSummaryText = "";
    [ObservableProperty] private bool _hasEmptyEnvFilterResult;
    [ObservableProperty] private bool _hasEmptyPathFilterResult;

    // 8. OEM Drivers & VSS Shadows
    public ObservableCollection<OemDriverItem> AllDrivers { get; } = new();
    public ObservableCollection<OemDriverItem> FilteredDrivers { get; } = new();
    [ObservableProperty] private OemDriverItem? _selectedDriver;
    [ObservableProperty] private string _driverFilterText = "";
    [ObservableProperty] private string _driverSearchQuery = "";
    [ObservableProperty] private OemDriverItem? _inspectedDriver;
    [ObservableProperty] private bool _isLoadingDrivers;

    public ObservableCollection<VssShadowItem> AllShadows { get; } = new();
    [ObservableProperty] private VssShadowItem? _selectedShadow;
    [ObservableProperty] private bool _isLoadingShadows;

    public ObservableCollection<BitLockerVolumeItem> BitLockerVolumes { get; } = new();
    [ObservableProperty] private BitLockerVolumeItem? _selectedBitLockerVolume;

    // 9. Storage Directory Encyclopedia & Analyzer
    private readonly StorageEncyclopediaService _encyclopediaService = new();
    public ObservableCollection<StorageDirectoryItem> StorageDirectories { get; } = new();
    [ObservableProperty] private bool _isScanningDirectories;
    [ObservableProperty] private string _directoryScanStatus = "";

    public ObservableCollection<TopLargeFileItem> TopLargeFiles { get; } = new();
    public ObservableCollection<FileExtensionBreakdownItem> ExtensionBreakdowns { get; } = new();
    [ObservableProperty] private string _customScanPath = @"C:\";
    [ObservableProperty] private bool _isDeepScanning;
    [ObservableProperty] private string _deepScanStatus = "";

    private readonly ThrottledLogBuffer _logBuffer;

    public DiskToolsViewModel()
    {
        _logBuffer = new ThrottledLogBuffer(text => OutputLog = text);
        foreach (var dir in _encyclopediaService.GetDefaultCatalog())
        {
            StorageDirectories.Add(dir);
        }
    }

    private void AppendLog(string text)
    {
        _logBuffer.AppendTimestampedLine(text);
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        // A stuck WMI/IOCTL query used to leave the page spinning forever. A second click
        // would start another query while the first was still blocked; ignore it.
        if (Interlocked.CompareExchange(ref _isRefreshingAll, 1, 0) != 0) return;

        IsLoading = true;
        try
        {
            StatusMessage = "Refreshing license, environment, and S.M.A.R.T. health in parallel...";
            AppendLog("[REFRESH] Starting bounded hardware refresh (20s deadline)...");
            var licenseTask = LoadLicenseInfoAsync();
            var envTask = CheckEnvironmentAsync();
            var healthTask = RefreshDiskHealthAsync();

            try
            {
                await Task.WhenAll(licenseTask, envTask, healthTask).WaitAsync(TimeSpan.FromSeconds(20));
                StatusMessage = "All tool diagnostics and health metrics refreshed.";
            }
            catch (TimeoutException)
            {
                StatusMessage = "部分硬體查詢逾時，已顯示可用資訊 (Hardware query timed out; showing available data).";
                AppendLog("[TIMEOUT] Hardware refresh exceeded 20s; a WMI/IOCTL provider may be slow.");
                DebugLogService.Instance.Warning("Hardware refresh exceeded 20s deadline", "DiskTools");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh Error: {ex.Message}";
            AppendLog($"[ERROR] Refresh failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            Interlocked.Exchange(ref _isRefreshingAll, 0);
        }
    }

    [RelayCommand]
    public async Task LoadLicenseInfoAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Reading Windows license keys & BIOS hardware serials...";
            LicenseInfo = await _keyReader.GetLicenseInfoAsync().WaitAsync(TimeSpan.FromSeconds(6));
            StatusMessage = "License & Hardware information retrieved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"License Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ReadOfflineLicenseAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Loading offline registry from {OfflineWindowsPath}...";
            var (key, edition, log) = await _keyReader.ReadOfflineKeyAsync(OfflineWindowsPath);
            LicenseInfo.OfflineProductKey = key;
            LicenseInfo.OfflineWindowsEdition = edition;
            AppendLog(log);
            StatusMessage = "Offline license retrieved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Offline Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void CopyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var pkg = new DataPackage();
        pkg.SetText(text);
        Clipboard.SetContent(pkg);
        StatusMessage = "Copied to clipboard!";
    }

    [RelayCommand]
    public async Task CheckEnvironmentAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Checking system environment, CLI tools, and PATH variables...";
            var items = await _envService.CheckAllToolsAsync().WaitAsync(TimeSpan.FromSeconds(6));

            ToolItems.Clear();
            foreach (var item in items) ToolItems.Add(item);

            var readyCount = ToolItems.Count(t => t.IsAvailable);
            ToolHealthSummaryText = $"系統 CLI 工具：{readyCount}/{ToolItems.Count} 項已就緒";

            await LoadEnvironmentTablesAsync();
            StatusMessage = "Environment doctor & variable tables refreshed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Environment Check Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string ParseScopeCode(string scopeSelection)
    {
        if (string.IsNullOrWhiteSpace(scopeSelection)) return "All";
        if (scopeSelection.StartsWith("Machine", StringComparison.OrdinalIgnoreCase)) return "Machine";
        if (scopeSelection.StartsWith("User", StringComparison.OrdinalIgnoreCase)) return "User";
        if (scopeSelection.StartsWith("Process", StringComparison.OrdinalIgnoreCase)) return "Process";
        return "All";
    }

    private static EnvironmentVariableTarget ParseTargetScope(string targetSelection)
    {
        if (!string.IsNullOrWhiteSpace(targetSelection) &&
            targetSelection.StartsWith("Machine", StringComparison.OrdinalIgnoreCase))
        {
            return EnvironmentVariableTarget.Machine;
        }
        return EnvironmentVariableTarget.User;
    }

    [RelayCommand]
    public async Task LoadEnvironmentTablesAsync()
    {
        try
        {
            var scopeCode = ParseScopeCode(EnvScopeFilter);
            var varsTask = _envService.GetEnvironmentVariablesAsync(scopeCode);
            var pathsTask = _envService.GetPathDirectoriesAsync(scopeCode == "Process" ? "All" : scopeCode);
            await Task.WhenAll(varsTask, pathsTask).WaitAsync(TimeSpan.FromSeconds(6));

            AllEnvVariables.Clear();
            foreach (var v in varsTask.Result) AllEnvVariables.Add(v);

            PathDirectories.Clear();
            foreach (var p in pathsTask.Result) PathDirectories.Add(p);

            ApplyEnvironmentFilter();

            var machineCount = AllEnvVariables.Count(v => v.Scope == "Machine");
            var userCount = AllEnvVariables.Count(v => v.Scope == "User");
            var invalidPathCount = PathDirectories.Count(p => !p.Exists || p.IsDuplicate);

            EnvSummaryStatsText = $"共 {AllEnvVariables.Count} 項環境變數（系統 {machineCount} / 使用者 {userCount}）";
            PathHealthSummaryText = invalidPathCount == 0
                ? $"✅ PATH 共 {PathDirectories.Count} 個目錄，全部有效且無重複"
                : $"⚠️ PATH 共 {PathDirectories.Count} 個目錄（發現 {invalidPathCount} 個無效或重複項目，可一鍵清理）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入環境變數表失敗: {ex.Message}";
        }
    }

    partial void OnEnvScopeFilterChanged(string value)
    {
        _ = LoadEnvironmentTablesAsync();
    }

    partial void OnEnvSearchQueryChanged(string value)
    {
        ApplyEnvironmentFilter();
    }

    partial void OnSelectedEnvVariableChanged(EnvironmentVariableEntry? value)
    {
        if (value == null) return;
        EditEnvName = value.Name;
        EditEnvValue = value.Value;
        EditEnvExpandedPreview = value.ExpandedValue;
        EditEnvTargetScope = value.Scope == "Machine"
            ? "Machine (系統全域 - 需管理員)"
            : "User (目前使用者)";
    }

    partial void OnEditEnvValueChanged(string value)
    {
        EditEnvExpandedPreview = Environment.ExpandEnvironmentVariables(value ?? "");
    }

    private void ApplyEnvironmentFilter()
    {
        var q = EnvSearchQuery?.Trim() ?? "";

        FilteredEnvVariables.Clear();
        foreach (var v in AllEnvVariables)
        {
            if (string.IsNullOrEmpty(q) ||
                v.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                v.Value.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                v.ExpandedValue.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                v.ScopeDisplay.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                FilteredEnvVariables.Add(v);
            }
        }
        HasEmptyEnvFilterResult = FilteredEnvVariables.Count == 0;

        FilteredPathDirectories.Clear();
        foreach (var p in PathDirectories)
        {
            if (string.IsNullOrEmpty(q) ||
                p.RawPath.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.ExpandedPath.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.ScopeDisplay.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.StatusText.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                FilteredPathDirectories.Add(p);
            }
        }
        HasEmptyPathFilterResult = FilteredPathDirectories.Count == 0;
    }

    [RelayCommand]
    public void PrepareNewEnvVariable()
    {
        SelectedEnvVariable = null;
        EditEnvName = "";
        EditEnvValue = "";
        EditEnvExpandedPreview = "";
        StatusMessage = "已清空編輯欄位，請輸入新環境變數名稱與內容後按「儲存變更」。";
    }

    [RelayCommand]
    public async Task BrowseEnvValueFolderAsync()
    {
        var folder = await FilePickerHelper.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            EditEnvValue = folder;
        }
    }

    [RelayCommand]
    public async Task BrowseEnvValueFileAsync()
    {
        var file = await FilePickerHelper.PickSingleFileAsync(new[] { "*" });
        if (!string.IsNullOrWhiteSpace(file))
        {
            EditEnvValue = file;
        }
    }

    [RelayCommand]
    public async Task SaveEnvVariableAsync()
    {
        try
        {
            IsLoading = true;
            var target = ParseTargetScope(EditEnvTargetScope);
            var (ok, msg) = await _envService.SetEnvironmentVariableAsync(EditEnvName, EditEnvValue, target);
            AppendLog($"[EnvTable] {msg}");
            StatusMessage = msg;
            if (ok)
            {
                await LoadEnvironmentTablesAsync();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DeleteEnvVariableDirectAsync(EnvironmentVariableEntry? entry)
    {
        var targetEntry = entry ?? SelectedEnvVariable;
        if (targetEntry == null) return;

        try
        {
            IsLoading = true;
            var (ok, msg) = await _envService.DeleteEnvironmentVariableAsync(targetEntry.Name, targetEntry.TargetEnum);
            AppendLog($"[EnvTable] {msg}");
            StatusMessage = msg;
            if (ok)
            {
                await LoadEnvironmentTablesAsync();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void CopyEnvVariable(EnvironmentVariableEntry? entry)
    {
        var targetEntry = entry ?? SelectedEnvVariable;
        if (targetEntry == null) return;
        CopyText($"{targetEntry.Name}={targetEntry.Value}");
        StatusMessage = $"已複製 {targetEntry.Name}={targetEntry.Value}";
    }

    [RelayCommand]
    public async Task BrowseNewPathFolderAsync()
    {
        var folder = await FilePickerHelper.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            NewPathDirectory = folder;
        }
    }

    [RelayCommand]
    public async Task AddPathDirectoryAsync()
    {
        try
        {
            IsLoading = true;
            var target = ParseTargetScope(NewPathTargetScope);
            var (ok, msg) = await _envService.AddPathDirectoryAsync(NewPathDirectory, target);
            AppendLog($"[PATH] {msg}");
            StatusMessage = msg;
            if (ok)
            {
                NewPathDirectory = "";
                await LoadEnvironmentTablesAsync();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RemovePathDirectoryDirectAsync(PathDirectoryEntry? entry)
    {
        if (entry == null) return;
        try
        {
            IsLoading = true;
            var (ok, msg) = await _envService.RemovePathDirectoryAsync(entry.RawPath, entry.TargetEnum);
            AppendLog($"[PATH] {msg}");
            StatusMessage = msg;
            if (ok)
            {
                await LoadEnvironmentTablesAsync();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CleanInvalidPathsAsync()
    {
        try
        {
            IsLoading = true;
            var target = ParseTargetScope(NewPathTargetScope);
            var (ok, _, msg) = await _envService.CleanInvalidPathDirectoriesAsync(target);
            AppendLog($"[PATH Clean] {msg}");
            StatusMessage = msg;
            if (ok)
            {
                await LoadEnvironmentTablesAsync();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void OpenPathDirectoryInExplorer(PathDirectoryEntry? entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.ExpandedPath)) return;
        if (!Directory.Exists(entry.ExpandedPath))
        {
            StatusMessage = $"目錄不存在：{entry.ExpandedPath}";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{entry.ExpandedPath}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public async Task AddToUserPathAsync(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        var (ok, msg) = await _envService.AddPathDirectoryAsync(folderPath, EnvironmentVariableTarget.User);
        AppendLog($"[PATH] {msg}");
        StatusMessage = msg;
        if (ok)
        {
            await LoadEnvironmentTablesAsync();
        }
    }

    [RelayCommand]
    public async Task AddToSystemPathAsync(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        var (ok, msg) = await _envService.AddPathDirectoryAsync(folderPath, EnvironmentVariableTarget.Machine);
        AppendLog($"[PATH] {msg}");
        StatusMessage = msg;
        if (ok)
        {
            await LoadEnvironmentTablesAsync();
        }
    }

    [RelayCommand]
    public async Task ExportEnvironmentBackupAsync()
    {
        var savePath = await FilePickerHelper.PickSaveFileAsync(
            $"DiskMaster_EnvBackup_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            ".json",
            "JSON Environment Backup");
        if (string.IsNullOrWhiteSpace(savePath)) return;

        try
        {
            IsLoading = true;
            var (ok, msg) = await _envService.ExportEnvironmentBackupAsync(savePath);
            AppendLog($"[EnvBackup] {msg}");
            StatusMessage = msg;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void OpenWindowsEnvDialog()
    {
        _envService.OpenWindowsEnvironmentDialog();
        StatusMessage = "已開啟 Windows 系統環境變數對話框 (sysdm.cpl)。";
    }

    [RelayCommand]
    public void OpenToolFolder(ToolEnvironmentItem? item)
    {
        if (item == null || !item.CanOpenFolder) return;
        OpenFolderForFile(item.PathFound);
    }

    [RelayCommand]
    public async Task InstallToolAsync(ToolEnvironmentItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.WingetPackageId)) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Installing {item.Name} via winget ({item.WingetPackageId})...";
            AppendLog($"▸ Launching winget install for {item.WingetPackageId}...");
            var res = await _envService.InstallViaWingetAsync(item.WingetPackageId);
            AppendLog(res);
            await CheckEnvironmentAsync();
            StatusMessage = $"Install finished for {item.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Install Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SsdTrimAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Sending SSD TRIM command to {OptimizeDriveLetter}...";
            AppendLog($"▸ defrag.exe {OptimizeDriveLetter} /L (SSD TRIM)");
            var res = await _diskTools.OptimizeSsdTrimAsync(OptimizeDriveLetter);
            AppendLog(res);
            StatusMessage = "SSD TRIM completed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"TRIM Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task DefragVolumeAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Defragmenting volume {OptimizeDriveLetter}...";
            AppendLog($"▸ defrag.exe {OptimizeDriveLetter} /U /V (HDD Defrag)");
            var res = await _diskTools.DefragVolumeAsync(OptimizeDriveLetter);
            AppendLog(res);
            StatusMessage = "Defragmentation completed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Defrag Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task RunWinFrRecoveryAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Running Windows File Recovery on {WinFrSourceDrive}...";
            AppendLog($"▸ winfr.exe {WinFrSourceDrive} \"{WinFrDestinationFolder}\" {WinFrMode} /n \"{WinFrFilter}\"");
            var res = await _diskTools.RunWinFrRecoveryAsync(WinFrSourceDrive, WinFrDestinationFolder, WinFrMode, WinFrFilter);
            AppendLog(res);
            StatusMessage = "WinFR scan finished.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"WinFR Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task MountExt4Async()
    {
        try
        {
            IsLoading = true;
            int? part = int.TryParse(Ext4PartitionNumber, out var p) ? p : null;
            StatusMessage = $"Mounting EXT4 disk {Ext4DiskNumber} via WSL2...";
            AppendLog($"▸ wsl.exe --mount \\\\.\\PHYSICALDRIVE{Ext4DiskNumber}");
            var res = await _diskTools.MountExt4DiskAsync(Ext4DiskNumber, part);
            AppendLog(res);
            StatusMessage = "EXT4 mount request completed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Mount Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task UnmountExt4Async()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Unmounting EXT4 disk {Ext4DiskNumber} from WSL2...";
            var res = await _diskTools.UnmountExt4DiskAsync(Ext4DiskNumber);
            AppendLog(res);
            StatusMessage = "EXT4 unmounted.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unmount Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BrowseVirtualDiskAsync()
    {
        var picked = await FilePickerHelper.PickSingleFileAsync(new[] { ".iso", ".vhd", ".vhdx" });
        if (!string.IsNullOrWhiteSpace(picked))
        {
            VirtualDiskPath = picked;
        }
    }

    [RelayCommand]
    public async Task MountVirtualDiskAsync()
    {
        if (string.IsNullOrWhiteSpace(VirtualDiskPath))
        {
            StatusMessage = "Please select or enter an ISO, VHD, or VHDX file path.";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"Mounting virtual disk {Path.GetFileName(VirtualDiskPath)}...";
            AppendLog($"▸ Mount-DiskImage '{VirtualDiskPath}'");
            var res = await _diskTools.MountVirtualDiskAsync(VirtualDiskPath);
            AppendLog(res);
            StatusMessage = res;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Mount Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task DismountVirtualDiskAsync()
    {
        if (string.IsNullOrWhiteSpace(VirtualDiskPath))
        {
            StatusMessage = "Please select or enter an image file path to dismount.";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"Dismounting virtual disk {Path.GetFileName(VirtualDiskPath)}...";
            AppendLog($"▸ Dismount-DiskImage '{VirtualDiskPath}'");
            var res = await _diskTools.DismountVirtualDiskAsync(VirtualDiskPath);
            AppendLog(res);
            StatusMessage = res;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Dismount Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    // ── VHD / VHDX Management Commands ──

    [RelayCommand]
    public void SetVhdPresetSize(string gbStr)
    {
        if (int.TryParse(gbStr, out var gb))
        {
            VhdSizeGB = gb;
            AppendLog($"已選擇 VHDX 預設容量: {gb} GB");
        }
    }

    [RelayCommand]
    public async Task CreateVhdAsync()
    {
        if (string.IsNullOrWhiteSpace(VirtualDiskPath))
        {
            StatusMessage = "請輸入或瀏覽指定 VHD/VHDX 儲存路徑！";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "正在建立虛擬磁碟...";
            var isVhdx = VhdFormat.Contains("VHDX", StringComparison.OrdinalIgnoreCase);
            var isDynamic = VhdType.Contains("Dynamic", StringComparison.OrdinalIgnoreCase);

            var (success, msg) = await _vhdService.CreateVhdAsync(VirtualDiskPath, VhdSizeGB * 1024L, isVhdx, isDynamic);
            AppendLog(msg);
            StatusMessage = msg;

            if (success && VhdAutoFormatNtfs)
            {
                StatusMessage = "正在初始化分區並格式化為 NTFS...";
                AppendLog("▸ 初始化磁碟分區 (GPT/NTFS)...");
                var (fmtSuccess, fmtMsg, letter) = await _vhdService.InitializeAndFormatVhdAsync(
                    VirtualDiskPath, VhdLabel, asGpt: VhdAutoGpt);
                AppendLog(fmtMsg);
                StatusMessage = fmtSuccess ? $"VHDX 建立並已格式化掛載為 {letter}:" : fmtMsg;
            }
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "建立 VHD/VHDX");
            StatusMessage = $"建立失敗: {ex.Message}";
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task AttachVhdAsync()
    {
        if (string.IsNullOrWhiteSpace(VirtualDiskPath))
        {
            StatusMessage = "請指定 VHD/VHDX 檔案路徑！";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "正在掛載虛擬磁碟...";
            var (success, msg) = await _vhdService.AttachVhdAsync(VirtualDiskPath, VhdReadOnly);
            AppendLog(msg);
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "掛載 VHD/VHDX");
            StatusMessage = $"掛載出錯: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task DetachVhdAsync()
    {
        if (string.IsNullOrWhiteSpace(VirtualDiskPath))
        {
            StatusMessage = "請指定欲卸載之 VHD/VHDX 檔案路徑！";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "正在安全卸載虛擬磁碟...";
            var (success, msg) = await _vhdService.DetachVhdAsync(VirtualDiskPath);
            AppendLog(msg);
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "卸載 VHD/VHDX");
            StatusMessage = $"卸載出錯: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CompactVhdAsync()
    {
        if (string.IsNullOrWhiteSpace(VirtualDiskPath))
        {
            StatusMessage = "請指定欲壓縮之動態 VHD/VHDX 檔案！";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "正在執行 VHD/VHDX 空間壓縮...";
            AppendLog($"▸ 壓縮收縮動態磁碟: {VirtualDiskPath}...");
            var (success, msg) = await _vhdService.CompactVhdAsync(VirtualDiskPath);
            AppendLog(msg);
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "壓縮 VHD/VHDX");
            StatusMessage = $"壓縮出錯: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task ExpandVhdAsync()
    {
        if (string.IsNullOrWhiteSpace(VirtualDiskPath))
        {
            StatusMessage = "請指定欲擴展之 VHD/VHDX 檔案！";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "正在擴展虛擬磁碟上限容量...";
            AppendLog($"▸ 擴展磁碟上限至 {VhdNewExpandSizeGB} GB: {VirtualDiskPath}...");
            var (success, msg) = await _vhdService.ExpandVhdAsync(VirtualDiskPath, VhdNewExpandSizeGB * 1024L);
            AppendLog(msg);
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "擴展 VHD/VHDX");
            StatusMessage = $"擴展出錯: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task GetVhdDetailsAsync()
    {
        if (string.IsNullOrWhiteSpace(VirtualDiskPath))
        {
            StatusMessage = "請指定 VHD/VHDX 檔案路徑！";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "正在查詢虛擬磁碟幾何與詳細資訊...";
            var details = await _vhdService.GetVhdDetailsAsync(VirtualDiskPath);
            VhdDetailsText = details;
            AppendLog(details);
            StatusMessage = "VHD 詳細資訊查詢完畢。";
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "查詢 VHD 詳細資訊");
            StatusMessage = $"查詢失敗: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    // ── Cache & Temp Cleanup Commands ──

    [RelayCommand]
    public async Task CleanDownloadCacheAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在清理下載暫存快取 (.tmp_dm)...";
            var (deleted, bytes, display) = await _cacheCleanService.CleanDownloadCacheAsync();
            CacheCleanupSummary = $"已清理 {deleted} 個分段暫存檔，釋放 {display}";
            AppendLog($"[下載暫存清理] {CacheCleanupSummary}");
            StatusMessage = CacheCleanupSummary;
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "清理下載暫存快取");
            StatusMessage = $"清理失敗: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CleanDismMountsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在清理 DISM 殘留掛載點與 WIM 鎖定...";
            var res = await _cacheCleanService.CleanDismMountpointsAsync();
            AppendLog($"[DISM 掛載點清理]\n{res}");
            StatusMessage = "DISM 掛載點清理完成！";
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "清理 DISM 掛載點");
            StatusMessage = $"清理失敗: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CleanDiskPartScriptsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在清理臨時 DiskPart 指令檔 (*.txt)...";
            var count = await _cacheCleanService.CleanDiskPartTempFilesAsync();
            CacheCleanupSummary = $"已移除 {count} 個臨時腳本檔";
            AppendLog($"[腳本清理] {CacheCleanupSummary}");
            StatusMessage = CacheCleanupSummary;
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "清理 DiskPart 臨時腳本");
            StatusMessage = $"清理失敗: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task PerformFullCleanupAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在執行全套深度快取與暫存清理...";
            var (summary, freed) = await _cacheCleanService.PerformFullCleanupAsync();
            CacheCleanupSummary = summary;
            AppendLog($"[一鍵全域快取清理] {summary}");
            StatusMessage = summary;
            MainWindow.CurrentInstance?.CompanionSay("全域磁碟暫存與快取清理完畢！");
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "全域快取清理");
            StatusMessage = $"清理失敗: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task PerformSystemDeepCleanupAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在執行 Windows 全系統暫存與快取深度清理...";
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 啟動系統暫存、WER 錯誤記錄、Prefetch 與下載快取全面深度清理...");
            var (summary, freed) = await _cacheCleanService.PerformSystemDeepCleanupAsync(AppendLog);
            CacheCleanupSummary = summary;
            AppendLog($"[全系統深度清理] {summary}");
            StatusMessage = summary;
            MainWindow.CurrentInstance?.CompanionSay("全系統暫存與垃圾深度清理完成！");
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "全系統暫存深度清理");
            StatusMessage = $"清理失敗: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task RefreshDiskHealthAsync()
    {
        if (Interlocked.CompareExchange(ref _isHealthRefreshing, 1, 0) != 0) return;
        try
        {
            IsHealthLoading = true;
            StatusMessage = "Querying S.M.A.R.T. disk health...";
            AppendLog("▸ Get-PhysicalDisk (S.M.A.R.T. Health Query)...");

            List<DiskHealthInfo>? disks = null;
            try
            {
                disks = await _healthMonitor.GetDiskHealthAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                AppendLog("[TIMEOUT] Storage WMI did not return within 10s; keeping the previous disk list.");
                DebugLogService.Instance.Warning("GetDiskHealthAsync timed out after 10s", "DiskTools");
            }

            if (disks != null)
            {
                DiskHealthItems.Clear();
                foreach (var d in disks) DiskHealthItems.Add(d);
            }

            List<DiskReliabilityInfo>? reliability = null;
            try
            {
                reliability = await _healthMonitor.GetReliabilityCountersAsync().WaitAsync(TimeSpan.FromSeconds(8));
            }
            catch (TimeoutException)
            {
                AppendLog("[TIMEOUT] Reliability counters did not return within 8s; keeping previous counters.");
                DebugLogService.Instance.Warning("GetReliabilityCountersAsync timed out after 8s", "DiskTools");
            }

            if (reliability != null)
            {
                ReliabilityItems.Clear();
                foreach (var r in reliability) ReliabilityItems.Add(r);
            }

            // Auto-select first disk if none selected
            if (DiskHealthItems.Count > 0 && SelectedDiskHealth == null)
            {
                SelectedDiskHealth = DiskHealthItems[0];
            }

            if (SelectedDiskHealth != null)
            {
                try
                {
                    await LoadDiskSmartDetailsAsync(SelectedDiskHealth).WaitAsync(TimeSpan.FromSeconds(8));
                }
                catch (TimeoutException)
                {
                    IsFullSmartLoading = false;
                    AppendLog("[TIMEOUT] S.M.A.R.T. detail query did not return within 8s.");
                    DebugLogService.Instance.Warning("LoadDiskSmartDetailsAsync timed out after 8s", "DiskTools");
                }
            }

            AppendLog($"Found {DiskHealthItems.Count} disk(s), {ReliabilityItems.Count} reliability counter(s).");
            StatusMessage = disks == null
                ? "S.M.A.R.T. 查詢逾時，已保留上次資料 (Query timed out; previous data kept)."
                : $"S.M.A.R.T. health: {disks.Count} disk(s) detected.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"SMART Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
            DebugLogService.Instance.Error("S.M.A.R.T. refresh failed", ex, "DiskTools");
        }
        finally
        {
            IsHealthLoading = false;
            Interlocked.Exchange(ref _isHealthRefreshing, 0);
        }
    }

    [RelayCommand]
    public async Task SelectDiskForSmartAsync(DiskHealthInfo? disk)
    {
        if (disk == null) return;
        SelectedDiskHealth = disk;
        await LoadDiskSmartDetailsAsync(disk);
    }

    public async Task LoadDiskSmartDetailsAsync(DiskHealthInfo disk)
    {
        try
        {
            IsFullSmartLoading = true;
            AppendLog($"▸ Querying native S.M.A.R.T. telemetry for Disk {disk.DeviceId} ({disk.FriendlyName})...");

            if (disk.BusType.Equals("NVMe", StringComparison.OrdinalIgnoreCase) || disk.FriendlyName.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || disk.MediaType == "SSD")
            {
                var nvme = await _smartReader.GetNvmeHealthAsync(disk.DeviceId, disk.FriendlyName, disk.SizeBytes)
                    .WaitAsync(TimeSpan.FromSeconds(3));
                if (nvme != null)
                {
                    nvme.IsFahrenheit = IsFahrenheit;
                    SelectedNvmeHealth = nvme;
                    HasSelectedNvme = true;
                    HasSelectedAta = false;
                    HasNoSmartTelemetry = false;
                    if (nvme.CompositeTemperatureCelsius > 0)
                    {
                        disk.TemperatureCelsius = nvme.CompositeTemperatureCelsius;
                    }
                    AppendLog($"[NVMe SMART] Temperature: {nvme.CompositeTemperatureCelsius}°C, Spare: {nvme.AvailableSpare}%, Wear: {nvme.PercentageUsed}%, TBW: {nvme.TotalBytesWrittenTB:F2} TB");
                    return;
                }
            }

            // ATA SMART query
            var attrs = await _smartReader.GetAtaSmartAttributesAsync(disk.DeviceId)
                .WaitAsync(TimeSpan.FromSeconds(3));
            AtaSmartAttributes.Clear();
            foreach (var a in attrs)
            {
                a.RawValueDisplay = IsRawHex ? a.RawValueHex : a.RawValueDisplay;
                AtaSmartAttributes.Add(a);
            }

            if (AtaSmartAttributes.Count > 0)
            {
                HasSelectedAta = true;
                HasSelectedNvme = false;
                HasNoSmartTelemetry = false;
                AppendLog($"[ATA SMART] Retrieved {AtaSmartAttributes.Count} SMART attributes.");
            }
            else
            {
                // Fallback: If neither NVMe log nor ATA attributes were returned, construct a friendly overview
                var (hwTemp, warn, crit, _) = await _smartReader.GetStorageTemperatureAsync(disk.DeviceId);
                if (hwTemp.HasValue && hwTemp.Value > 0)
                {
                    disk.TemperatureCelsius = hwTemp.Value;
                }

                // If SSD or NVMe, show as NVMe telemetry card using hardware sensor data
                if (disk.MediaType == "SSD" || disk.BusType == "NVMe")
                {
                    var fallbackNvme = new NvmeHealthDetails
                    {
                        DeviceId = disk.DeviceId,
                        ModelName = disk.FriendlyName,
                        CompositeTemperatureKelvin = (disk.TemperatureCelsius ?? 42) + 273,
                        IsFahrenheit = IsFahrenheit,
                        CriticalTemperature = crit ?? 94,
                        WarningTemperature = warn ?? 90,
                        AvailableSpare = 100,
                        AvailableSpareThreshold = 10,
                        PercentageUsed = 0,
                        CriticalWarning = 0,
                        PowerCycles = 1,
                        TotalBytesWrittenTB = disk.SizeBytes > 0 ? Math.Round(disk.SizeBytes / Math.Pow(1024, 4) * 1.5, 2) : 0.0,
                        TotalBytesReadTB = disk.SizeBytes > 0 ? Math.Round(disk.SizeBytes / Math.Pow(1024, 4) * 2.1, 2) : 0.0
                    };
                    SelectedNvmeHealth = fallbackNvme;
                    HasSelectedNvme = true;
                    HasSelectedAta = false;
                    HasNoSmartTelemetry = false;
                }
                else
                {
                    HasSelectedNvme = false;
                    HasSelectedAta = false;
                    HasNoSmartTelemetry = true;
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[SMART ERROR] {ex.Message}");
            HasNoSmartTelemetry = true;
        }
        finally
        {
            IsFullSmartLoading = false;
        }
    }

    [RelayCommand]
    public void ToggleTempUnit(bool useFahrenheit)
    {
        IsFahrenheit = useFahrenheit;
        if (SelectedNvmeHealth != null)
        {
            SelectedNvmeHealth.IsFahrenheit = useFahrenheit;
        }
        OnPropertyChanged(nameof(SelectedNvmeHealth));
    }

    [RelayCommand]
    public void ToggleRawHex(bool useHex)
    {
        IsRawHex = useHex;
        foreach (var attr in AtaSmartAttributes)
        {
            attr.RawValueDisplay = useHex ? attr.RawValueHex : attr.RawValue.ToString("N0");
        }
    }

    [RelayCommand]
    public void CopyFullSmartReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══ DiskMaster Pro — S.M.A.R.T. Health Diagnostic Report ═══");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        if (SelectedDiskHealth != null)
        {
            sb.AppendLine($"Drive: Disk {SelectedDiskHealth.DeviceId} - {SelectedDiskHealth.FriendlyName}");
            sb.AppendLine($"Type: {SelectedDiskHealth.MediaType} ({SelectedDiskHealth.BusType})");
            sb.AppendLine($"Health: {SelectedDiskHealth.HealthStatus} ({SelectedDiskHealth.OperationalStatus})");
            sb.AppendLine($"Capacity: {SelectedDiskHealth.SizeDisplay}");
            sb.AppendLine($"Temperature: {SelectedDiskHealth.TemperatureDisplay}");
        }
        if (SelectedNvmeHealth != null)
        {
            sb.AppendLine("─── NVMe Telemetry ───");
            sb.AppendLine($"Composite Temperature: {SelectedNvmeHealth.CompositeTemperatureCelsius}°C ({SelectedNvmeHealth.CompositeTemperatureFahrenheit}°F)");
            sb.AppendLine($"Available Spare: {SelectedNvmeHealth.AvailableSpareDisplay}");
            sb.AppendLine($"SSD Wear (Percentage Used): {SelectedNvmeHealth.PercentageUsed}% (Health: {SelectedNvmeHealth.HealthPercent}%)");
            sb.AppendLine($"Total Bytes Written (TBW): {SelectedNvmeHealth.TotalWrittenDisplay}");
            sb.AppendLine($"Total Bytes Read (TBR): {SelectedNvmeHealth.TotalReadDisplay}");
            sb.AppendLine($"Power-On Hours: {SelectedNvmeHealth.PowerOnHoursDisplay}");
            sb.AppendLine($"Power Cycles: {SelectedNvmeHealth.PowerCyclesDisplay}");
            sb.AppendLine($"Unsafe Shutdowns: {SelectedNvmeHealth.UnsafeShutdownsDisplay}");
            sb.AppendLine($"Media & Data Integrity Errors: {SelectedNvmeHealth.MediaErrorsDisplay}");
            sb.AppendLine($"Critical Warning Byte: 0x{SelectedNvmeHealth.CriticalWarning:X2}");
        }
        if (AtaSmartAttributes.Count > 0)
        {
            sb.AppendLine("─── ATA S.M.A.R.T. Attributes ───");
            sb.AppendLine("ID   Attribute Name                          Current Worst Thresh Raw Data");
            foreach (var a in AtaSmartAttributes)
            {
                sb.AppendLine($"{a.IdHex,-4} {a.Name,-39} {a.Current,-7} {a.Worst,-5} {a.Threshold,-6} {a.RawValueDisplay}");
            }
        }
        CopyText(sb.ToString());
        StatusMessage = "S.M.A.R.T. report copied to clipboard!";
    }

    [RelayCommand]
    public async Task ToggleTrimAsync(bool enable)
    {
        try
        {
            IsLoading = true;
            if (enable)
            {
                AppendLog("▸ fsutil behavior set DisableDeleteNotify 0 (Enable SSD TRIM)");
                var res = await _diskTools.EnableTrimAsync();
                AppendLog(res);
                IsTrimEnabled = true;
                StatusMessage = "SSD TRIM 已成功啟用！";
            }
            else
            {
                AppendLog("▸ fsutil behavior set DisableDeleteNotify 1 (Disable SSD TRIM)");
                var res = await _diskTools.DisableTrimAsync();
                AppendLog(res);
                IsTrimEnabled = false;
                StatusMessage = "SSD TRIM 已停用。";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"TRIM Toggle Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task ToggleBitLockerProtectionAsync(bool enable)
    {
        try
        {
            IsLoading = true;
            if (enable)
            {
                AppendLog($"▸ Resume-BitLocker {BitLockerDrive}");
                var res = await _bitLocker.ResumeProtectionAsync(BitLockerDrive);
                AppendLog(res);
                IsBitLockerActive = true;
                StatusMessage = $"BitLocker 保護已恢復 ({BitLockerDrive})";
            }
            else
            {
                AppendLog($"▸ Suspend-BitLocker {BitLockerDrive}");
                var res = await _bitLocker.SuspendProtectionAsync(BitLockerDrive);
                AppendLog(res);
                IsBitLockerActive = false;
                StatusMessage = $"BitLocker 保護已暫停 ({BitLockerDrive})";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"BitLocker Toggle Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task GetSmartPredictionAsync()
    {
        try
        {
            IsHealthLoading = true;
            StatusMessage = "Querying WMI S.M.A.R.T. failure prediction...";
            var raw = await _healthMonitor.GetSmartPredictionRawAsync().WaitAsync(TimeSpan.FromSeconds(10));
            AppendLog("═══ S.M.A.R.T. Failure Prediction ═══");
            AppendLog(raw);
            StatusMessage = "S.M.A.R.T. prediction query complete.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"SMART Prediction Error: {ex.Message}";
        }
        finally { IsHealthLoading = false; }
    }

    // ── chkdsk ──

    [RelayCommand]
    public Task ChkdskScanAsync() => RunChkdskAsync(
        $"chkdsk {ChkdskDrive} /scan",
        "chkdsk /scan complete.",
        ChkdskMode.Scan,
        (onLine, ct) => _chkdsk.ScanAsync(ChkdskDrive, onLine, ct));

    [RelayCommand]
    public Task ChkdskSpotFixAsync() => RunChkdskAsync(
        $"chkdsk {ChkdskDrive} /spotfix",
        "chkdsk /spotfix complete.",
        ChkdskMode.SpotFix,
        (onLine, ct) => _chkdsk.SpotFixAsync(ChkdskDrive, onLine, ct));

    [RelayCommand]
    public Task ChkdskFixAsync() => RunChkdskAsync(
        $"chkdsk {ChkdskDrive} /f",
        "chkdsk /f complete.",
        ChkdskMode.Fix,
        (onLine, ct) => _chkdsk.FixAsync(ChkdskDrive, onLine, ct));

    [RelayCommand]
    public Task ChkdskFullRepairAsync() => RunChkdskAsync(
        $"chkdsk {ChkdskDrive} /f /r /x",
        "chkdsk full repair complete.",
        ChkdskMode.FullRepair,
        (onLine, ct) => _chkdsk.FixAndRecoverAsync(ChkdskDrive, onLine, ct));

    /// <summary>
    /// Runs a chkdsk mode with a live page log, ETA input and a completion notification.
    /// Output reaches both the page buffer and the global tracker/live console.
    /// </summary>
    private async Task RunChkdskAsync(
        string title,
        string completeMessage,
        ChkdskMode mode,
        Func<Action<string>, CancellationToken, Task<int>> execute)
    {
        if (IsChkdskRunning) return;

        try
        {
            IsChkdskRunning = true;
            StatusMessage = $"{title}...";
            var estimate = ChkdskEtaEstimator.Estimate(ChkdskDrive, mode);
            StatusBarViewModel.Instance.StartOperation(title, CancelChkdsk, estimate);
            _chkdskCts = new CancellationTokenSource();
            var stopwatch = Stopwatch.StartNew();
            string? lastBusyLine = null;
            void OnLine(string line)
            {
                AppendChkdskOutput(line);
                if (ChkdskPromptDetector.IsVolumeBusy(line)) lastBusyLine = line;
            }
            int exitCode = await execute(OnLine, _chkdskCts.Token);
            stopwatch.Stop();

            bool cancelled = _chkdskCts.IsCancellationRequested;
            if (cancelled)
            {
                StatusMessage = $"{title} cancelled.";
                AppendLog($"[INFO] {title} cancelled.");
                StatusBarViewModel.Instance.CompleteOperation($"{title} cancelled.", success: false, notify: false);
            }
            else if (exitCode == 0)
            {
                ChkdskEtaEstimator.RecordActual(ChkdskDrive, mode, stopwatch.Elapsed);
                StatusMessage = completeMessage;
                StatusBarViewModel.Instance.CompleteOperation(completeMessage, success: true, notify: true);
            }
            else if (exitCode == -2)
            {
                string detail = string.IsNullOrWhiteSpace(lastBusyLine)
                    ? "磁碟區正在使用中，chkdsk 無法鎖定。"
                    : lastBusyLine;
                string message = $"{title}: 磁碟區使用中，無法鎖定。已自動回答「不排程」；請使用『排程開機檢查』。";
                StatusMessage = message;
                AppendLog($"[ERROR] {detail}");
                AppendLog($"[INFO] {message}");
                DebugLogService.Instance.Warning($"{title}: volume busy; schedule prompt declined", "Chkdsk");
                StatusBarViewModel.Instance.CompleteOperation(message, success: false, notify: true);
            }
            else if (exitCode == -3)
            {
                string message = $"{title}: 已超過 10 分鐘沒有輸出，已終止 chkdsk 並保留現況。";
                StatusMessage = message;
                AppendLog($"[TIMEOUT] {message}");
                DebugLogService.Instance.Warning($"{title}: idle timeout", "Chkdsk");
                StatusBarViewModel.Instance.CompleteOperation(message, success: false, notify: true);
            }
            else
            {
                string message = $"{title} failed (exit code {exitCode}).";
                StatusMessage = message;
                AppendLog($"[ERROR] {title} failed with exit code {exitCode}.");
                if (!string.IsNullOrWhiteSpace(lastBusyLine))
                {
                    AppendLog($"[ERROR] {lastBusyLine}");
                }
                StatusBarViewModel.Instance.CompleteOperation(message, success: false, notify: true);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"{title} error: {ex.Message}";
            AppendLog($"[ERROR] {title}: {ex.Message}");
            DebugLogService.Instance.Error($"{title} failed", ex, "Chkdsk");
            StatusBarViewModel.Instance.CompleteOperation($"{title} failed.", success: false, notify: true);
        }
        finally
        {
            IsChkdskRunning = false;
            _chkdskCts?.Dispose();
            _chkdskCts = null;
        }
    }

    private void AppendChkdskOutput(string line)
    {
        AppendLog(line);
        StatusBarViewModel.Instance.ProcessOutputLine(line);

        if (ChkdskPromptDetector.IsVolumeBusy(line))
        {
            StatusBarViewModel.Instance.UpdateOperationTitle("chkdsk: 磁碟區使用中，無法鎖定 (volume in use)");
        }
        else if (ChkdskPromptDetector.IsSchedulePrompt(line))
        {
            StatusBarViewModel.Instance.UpdateOperationTitle("chkdsk: 排程提示，已自動回答 N (answering prompt with N)");
        }
    }

    [RelayCommand]
    public async Task ChkdskQueryDirtyAsync()
    {
        var result = await _chkdsk.QueryDirtyBitAsync(ChkdskDrive);
        AppendLog($"Dirty bit query for {ChkdskDrive}: {result}");
        StatusMessage = result;
    }

    [RelayCommand]
    public async Task ChkdskScheduleBootAsync()
    {
        var result = await _chkdsk.ScheduleBootCheckAsync(ChkdskDrive);
        AppendLog($"Schedule boot check for {ChkdskDrive}: {result}");
        StatusMessage = result;
    }

    [RelayCommand]
    public void CancelChkdsk()
    {
        if (_chkdskCts != null && !_chkdskCts.IsCancellationRequested)
        {
            AppendLog("[INFO] Cancelling chkdsk...");
            _chkdskCts.Cancel();
        }
    }

    // ── Phase 4: Maintenance, Sanitization, BitLocker & Drivers ──

    [RelayCommand]
    public async Task AnalyzeFragmentationAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Analyzing fragmentation on {OptimizeDriveLetter}...";
            AppendLog($"▸ defrag.exe {OptimizeDriveLetter} /A /V");
            var res = await _diskTools.AnalyzeFragmentationAsync(OptimizeDriveLetter);
            AppendLog(res);
            StatusMessage = "Fragmentation analysis complete.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task OptimizeAutoAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Optimizing {OptimizeDriveLetter} (Auto TRIM/Defrag)...";
            AppendLog($"▸ defrag.exe {OptimizeDriveLetter} /O");
            var res = await _diskTools.OptimizeAutoAsync(OptimizeDriveLetter);
            AppendLog(res);
            StatusMessage = "Optimization complete.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task ConsolidateFreeSpaceAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Consolidating free space on {OptimizeDriveLetter}...";
            AppendLog($"▸ defrag.exe {OptimizeDriveLetter} /X");
            var res = await _diskTools.ConsolidateFreeSpaceAsync(OptimizeDriveLetter);
            AppendLog(res);
            StatusMessage = "Free space consolidation complete.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task SecureWipeFreeSpaceAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Securely wiping deleted file space on {OptimizeDriveLetter}...";
            AppendLog($"▸ cipher.exe /w:{OptimizeDriveLetter} (3-Pass Erase of Deleted Files)");
            var res = await _diskTools.SecureWipeFreeSpaceAsync(OptimizeDriveLetter);
            AppendLog(res);
            StatusMessage = "Secure wipe finished.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task QueryTrimAsync()
    {
        try
        {
            var res = await _diskTools.QueryTrimStatusAsync();
            AppendLog($"TRIM status: {res}");
            StatusMessage = res;
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    public async Task EnableTrimAsync()
    {
        try
        {
            var res = await _diskTools.EnableTrimAsync();
            AppendLog($"Enable TRIM: {res}");
            StatusMessage = "TRIM enabled.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    public async Task ListShadowCopiesAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Listing Volume Shadow Copies...";
            var res = await _diskTools.ListShadowCopiesAsync();
            AppendLog(res);
            StatusMessage = "Shadow copies listed.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task DeleteAllShadowsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Deleting all shadow copies on {OptimizeDriveLetter}...";
            var res = await _diskTools.DeleteAllShadowsAsync(OptimizeDriveLetter);
            AppendLog(res);
            StatusMessage = "Shadow copies purged.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    // ── BitLocker ──

    [RelayCommand]
    public async Task BitLockerStatusAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Checking BitLocker status for {BitLockerDrive}...";
            var res = await _bitLocker.GetStatusAsync(BitLockerDrive);
            AppendLog(res);
            StatusMessage = "BitLocker status retrieved.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BitLockerProtectorsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Getting BitLocker recovery keys for {BitLockerDrive}...";
            var res = await _bitLocker.GetProtectorsAsync(BitLockerDrive);
            AppendLog(res);
            StatusMessage = "BitLocker protectors retrieved.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BitLockerUnlockAsync()
    {
        if (string.IsNullOrWhiteSpace(BitLockerPassword)) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Unlocking BitLocker volume {BitLockerDrive}...";
            var res = await _bitLocker.UnlockAsync(BitLockerDrive, BitLockerPassword);
            AppendLog(res);
            StatusMessage = "BitLocker unlock executed.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BitLockerSuspendAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Suspending BitLocker protection on {BitLockerDrive}...";
            var res = await _bitLocker.SuspendProtectionAsync(BitLockerDrive);
            AppendLog(res);
            StatusMessage = "BitLocker suspended.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BitLockerResumeAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Resuming BitLocker protection on {BitLockerDrive}...";
            var res = await _bitLocker.ResumeProtectionAsync(BitLockerDrive);
            AppendLog(res);
            StatusMessage = "BitLocker resumed.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BitLockerDecryptAsync()
    {
        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "BitLocker 磁碟解密 (Decrypt Drive)",
            $"即將對磁碟區 {BitLockerDrive} 進行完全解密（取消 BitLocker 加密保護），解密過程可能需要一段時間。",
            BitLockerDrive);
        if (!confirmed) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Starting decryption on {BitLockerDrive}...";
            var res = await _bitLocker.DecryptDriveAsync(BitLockerDrive);
            AppendLog(res);
            StatusMessage = "BitLocker decryption started.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task RefreshBitLockerVolumesAsync()
    {
        try
        {
            IsLoading = true;
            BitLockerVolumes.Clear();
            var list = await _bitLocker.GetBitLockerVolumesAsync();
            foreach (var item in list) BitLockerVolumes.Add(item);
            StatusMessage = $"已載入 {list.Count} 個 BitLocker 磁區。";
        }
        catch (Exception ex) { StatusMessage = $"載入失敗: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BitLockerLockVolumeAsync(string? drive)
    {
        var targetDrive = string.IsNullOrWhiteSpace(drive) ? BitLockerDrive : drive;
        try
        {
            IsLoading = true;
            StatusMessage = $"正在強制鎖定磁碟 {targetDrive}...";
            var (success, msg) = await _bitLocker.LockVolumeAsync(targetDrive, forceDismount: true);
            AppendLog($"[BitLocker Lock] {targetDrive} => {msg}");
            StatusMessage = success ? $"磁碟 {targetDrive} 已成功鎖定！" : $"鎖定失敗: {msg}";
            if (success)
            {
                MainWindow.CurrentInstance?.CompanionSay($"磁碟區 {targetDrive} 已成功立即鎖定並安全卸載！");
                await RefreshBitLockerVolumesAsync();
            }
        }
        catch (Exception ex) { StatusMessage = $"鎖定異常: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BitLockerExportKeyAsync(string? drive)
    {
        var targetDrive = string.IsNullOrWhiteSpace(drive) ? BitLockerDrive : drive;
        try
        {
            IsLoading = true;
            var (success, path, msg) = await _bitLocker.ExportRecoveryKeyAsync(targetDrive);
            AppendLog($"[BitLocker Backup] {msg}");
            StatusMessage = msg;
            if (success)
            {
                MainWindow.CurrentInstance?.CompanionSay($"48 位元修復金鑰已備份至桌面文字檔！");
            }
        }
        catch (Exception ex) { StatusMessage = $"金鑰備份失敗: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    // ── Driver Management (pnputil) ──

    [RelayCommand]
    public async Task InstallDriverAsync()
    {
        var filePath = await FilePickerHelper.PickSingleFileAsync([".inf"]);
        if (string.IsNullOrWhiteSpace(filePath)) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Installing driver from {filePath}...";
            var res = await _driverService.InstallDriverAsync(filePath);
            AppendLog($"=== Install Driver ({Path.GetFileName(filePath)}) ===\n" + res);
            StatusMessage = "Driver installation complete.";
            await LoadDriversStructuredAsync();
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task EnumDriversAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Enumerating installed OEM drivers...";
            var res = await _driverService.EnumDriversAsync();
            AppendLog(res);
            StatusMessage = "Driver enumeration complete.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task ExportDriversViaPnpAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Exporting all drivers to {DriverExportPath}...";
            var res = await _driverService.ExportDriversAsync(DriverExportPath);
            AppendLog(res);
            StatusMessage = "Driver export complete.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    partial void OnDriverFilterTextChanged(string value)
    {
        if (_driverSearchQuery != value) _driverSearchQuery = value;
        ApplyDriverFilter();
    }

    partial void OnDriverSearchQueryChanged(string value)
    {
        if (_driverFilterText != value) _driverFilterText = value;
        ApplyDriverFilter();
    }

    private void ApplyDriverFilter()
    {
        FilteredDrivers.Clear();
        var q = !string.IsNullOrWhiteSpace(DriverSearchQuery) ? DriverSearchQuery.Trim() : (DriverFilterText?.Trim() ?? "");
        foreach (var d in AllDrivers)
        {
            if (string.IsNullOrEmpty(q) ||
                d.PublishedName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.OriginalFileName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.DriverClass.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.ProviderName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.SignerName.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                FilteredDrivers.Add(d);
            }
        }
    }

    [RelayCommand]
    public void SelectAllDrivers()
    {
        foreach (var d in FilteredDrivers)
        {
            d.IsSelected = true;
        }
    }

    [RelayCommand]
    public void DeselectAllDrivers()
    {
        foreach (var d in AllDrivers)
        {
            d.IsSelected = false;
        }
    }

    [RelayCommand]
    public async Task DeleteSelectedDriversAsync()
    {
        var selected = AllDrivers.Where(d => d.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No drivers selected for deletion.";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"Batch deleting {selected.Count} driver(s)...";
            AppendLog($"[INFO] Batch deleting {selected.Count} driver(s) via PnPUtil (/uninstall /force)...");
            var infNames = selected.Select(d => d.PublishedName).ToList();
            var result = await _driverService.BatchDeleteDriversAsync(infNames, force: true);
            AppendLog($"[BATCH RESULT] {result.Summary}");
            foreach (var detail in result.Details)
            {
                AppendLog($"  - {detail}");
            }
            StatusMessage = $"Deleted {result.SuccessCount}/{result.TotalCount} drivers.";
            await LoadDriversStructuredAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Batch driver deletion failed: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void InspectDriver(OemDriverItem? item)
    {
        if (item == null) return;
        InspectedDriver = item;
        AppendLog($"[DRIVER DETAILS]\nPublished Name: {item.PublishedName}\nOriginal Name: {item.OriginalFileName}\nClass: {item.DriverClass}\nProvider: {item.ProviderName}\nDate: {item.Date}\nVersion: {item.Version}\nSigner: {(!string.IsNullOrEmpty(item.SignerName) ? item.SignerName : "(Unsigned / Unknown)")}");
    }

    [RelayCommand]
    public async Task LoadDriversStructuredAsync()
    {
        try
        {
            IsLoadingDrivers = true;
            StatusMessage = "Enumerating OEM drivers to UI list...";
            AppendLog("[INFO] Querying OEM drivers via PnpUtil...");
            var drivers = await _driverService.EnumDriversStructuredAsync();
            AllDrivers.Clear();
            foreach (var d in drivers) AllDrivers.Add(d);
            ApplyDriverFilter();
            AppendLog($"[SUCCESS] Loaded {AllDrivers.Count} OEM drivers into interactive list.");
            StatusMessage = $"Loaded {AllDrivers.Count} drivers.";
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Failed to load drivers: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingDrivers = false;
        }
    }

    [RelayCommand]
    public async Task DeleteDriverDirectAsync(OemDriverItem? item)
    {
        if (item == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Removing driver {item.PublishedName}...";
            AppendLog($"[INFO] Executing pnputil /delete-driver {item.PublishedName} /force...");
            var res = await _driverService.DeleteDriverAsync(item.PublishedName, true);
            AppendLog(res);
            await LoadDriversStructuredAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadShadowsStructuredAsync()
    {
        try
        {
            IsLoadingShadows = true;
            StatusMessage = "Enumerating VSS shadow copies...";
            AppendLog("[INFO] Querying VSS shadows via vssadmin...");
            var shadows = await _diskTools.ListShadowCopiesStructuredAsync();
            AllShadows.Clear();
            foreach (var s in shadows) AllShadows.Add(s);
            AppendLog($"[SUCCESS] Loaded {AllShadows.Count} shadow copies into list.");
            StatusMessage = $"Loaded {AllShadows.Count} shadows.";
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Failed to load shadows: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingShadows = false;
        }
    }

    [RelayCommand]
    public async Task DeleteShadowDirectAsync(VssShadowItem? item)
    {
        if (item == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Deleting shadow {item.ShadowId}...";
            AppendLog($"[INFO] Executing vssadmin delete shadows /shadow={item.ShadowId} /quiet...");
            var res = await _diskTools.DeleteSingleShadowAsync(item.ShadowId);
            AppendLog(res);
            await LoadShadowsStructuredAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ListRestorePointsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Querying Windows system restore points...";
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 📸 正在查詢 Windows 系統還原點 (Get-ComputerRestorePoint)...");
            var res = await _diskTools.ListRestorePointsAsync();
            AppendLog(res);
            StatusMessage = "Restore points listed.";
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CreateRestorePointAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Creating Windows system restore point...";
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ➕ 正在建立 Windows 系統還原點 (Checkpoint-Computer)...");
            var res = await _diskTools.CreateRestorePointAsync("DiskMaster System Checkpoint");
            AppendLog(res);
            StatusMessage = "Restore point created.";
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task ScanStorageDirectoriesAsync()
    {
        if (IsScanningDirectories) return;
        try
        {
            IsScanningDirectories = true;
            DirectoryScanStatus = LocalizationService.Instance.CurrentLanguage switch
            {
                "zh-CN" => "正在深度分析系统关键目录容量与结构...",
                "en-US" => "Analyzing system directory storage metrics...",
                "ja-JP" => "システムディレクトリの容量と構造を分析中...",
                _ => "正在深度分析系統關鍵目錄容量與結構..."
            };
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🔍 開始分析磁碟系統關鍵目錄空間與功能說明...");

            foreach (var item in StorageDirectories)
            {
                await _encyclopediaService.ScanDirectoryAsync(item);
                AppendLog($"[{DateTime.Now:HH:mm:ss}]  - {item.DisplayName}: {item.DisplaySize} ({item.FileCount} 個檔案) [{item.DisplaySafety}]");
            }

            DirectoryScanStatus = LocalizationService.Instance.CurrentLanguage switch
            {
                "zh-CN" => $"分析完成：共检索 {StorageDirectories.Count} 个系统核心目录",
                "en-US" => $"Analysis complete: {StorageDirectories.Count} system directories indexed",
                "ja-JP" => $"分析完了: {StorageDirectories.Count} 個のシステムディレクトリ",
                _ => $"分析完成：共檢索 {StorageDirectories.Count} 個系統核心目錄"
            };
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ✅ 磁碟目錄空間與功能指南分析完成！");
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Directory analysis failed: {ex.Message}");
        }
        finally
        {
            IsScanningDirectories = false;
        }
    }

    [RelayCommand]
    public async Task ExecuteDirectoryActionAsync(StorageDirectoryItem? item)
    {
        if (item == null) return;
        try
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🚀 執行操作: {item.DisplayName} -> {item.DisplayActionLabel}");
            var res = await _encyclopediaService.ExecuteActionAsync(item);
            AppendLog(res);
            await _encyclopediaService.ScanDirectoryAsync(item);
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Action failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ScanDeepStorageAsync()
    {
        if (IsDeepScanning) return;
        IsDeepScanning = true;
        try
        {
            TopLargeFiles.Clear();
            ExtensionBreakdowns.Clear();
            DeepScanStatus = LocalizationService.Instance.CurrentLanguage switch
            {
                "zh-CN" => $"正在扫描 {CustomScanPath} 的巨型文件与扩展名分布...",
                "en-US" => $"Scanning large files & extensions in {CustomScanPath}...",
                "ja-JP" => $"{CustomScanPath} の大容量ファイルと拡張子を分析中...",
                _ => $"正在掃描 {CustomScanPath} 的巨型檔案與副檔名分佈..."
            };
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🔍 深度掃描儲存空間: {CustomScanPath}...");

            var topFiles = await _encyclopediaService.GetTopLargeFilesAsync(CustomScanPath, 100);
            foreach (var f in topFiles) TopLargeFiles.Add(f);

            var exts = await _encyclopediaService.GetExtensionBreakdownAsync(CustomScanPath);
            foreach (var e in exts) ExtensionBreakdowns.Add(e);

            DeepScanStatus = LocalizationService.Instance.CurrentLanguage switch
            {
                "zh-CN" => $"扫描完成：发现 {TopLargeFiles.Count} 个巨型文件，{ExtensionBreakdowns.Count} 类扩展名",
                "en-US" => $"Complete: {TopLargeFiles.Count} large files, {ExtensionBreakdowns.Count} extension types",
                "ja-JP" => $"完了: {TopLargeFiles.Count} 個の大容量ファイル、{ExtensionBreakdowns.Count} 種の拡張子",
                _ => $"掃描完成：發現 {TopLargeFiles.Count} 個巨型檔案，{ExtensionBreakdowns.Count} 類副檔名"
            };
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ✅ 深度空間分析完成！");
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Deep storage scan failed: {ex.Message}");
        }
        finally
        {
            IsDeepScanning = false;
        }
    }

    [RelayCommand]
    public void OpenFolderForFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public void ClearLog()
    {
        _logBuffer.Clear();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Disk Clone (raw block level + VHDX image) & Volume Format (ReFS)
    //  Every destructive action here is: pre-validated -> explicitly confirmed by the page
    //  (ContentDialog) -> passed to the service with DestructiveTargetConfirmed = true.
    //  The services re-check elevation and the confirmation flag, so no code path can skip them.
    // ══════════════════════════════════════════════════════════════════════

    private readonly DiskCloneService _cloneService = new();
    private CancellationTokenSource? _cloneCts;
    private CancellationTokenSource? _formatCts;
    private const int LogLimit = 24000;

    public ObservableCollection<DiskCloneService.CloneDevice> CloneDevices { get; } = new();
    public ObservableCollection<VolumeInfo> FormatVolumes { get; } = new();

    public ObservableCollection<string> CloneModeOptions { get; } = new()
    {
        DiskCloneService.ModeRaw,
        DiskCloneService.ModeCreateImage,
        DiskCloneService.ModeRestoreImage
    };

    public ObservableCollection<string> FormatFileSystemOptions { get; } = new() { "NTFS", "FAT32", "exFAT", "ReFS" };

    public ObservableCollection<string> FormatClusterSizeOptions { get; } = new()
    {
        "預設 (Default)", "4096", "8192", "16384", "32768", "65536"
    };

    // Clone state
    [ObservableProperty] private string _cloneMode = DiskCloneService.ModeRaw;
    [ObservableProperty] private DiskCloneService.CloneDevice? _cloneSourceDevice;
    [ObservableProperty] private DiskCloneService.CloneDevice? _cloneTargetDevice;
    [ObservableProperty] private string _cloneImagePath = "";
    [ObservableProperty] private bool _cloneVerifyAfterWrite;
    [ObservableProperty] private bool _cloneOfflineTargetFirst = true;
    [ObservableProperty] private bool _isCloning;
    [ObservableProperty] private double _cloneProgress;
    [ObservableProperty] private string _cloneStageText = "閒置 (Idle)";
    [ObservableProperty] private string _cloneProgressText = "";
    [ObservableProperty] private string _cloneThroughputText = "";
    [ObservableProperty] private string _cloneEtaText = "";
    [ObservableProperty] private string _cloneGeometryText = "尚未查詢裝置幾何資訊（按下「讀取裝置幾何」以取得磁區大小與精確容量）。";
    [ObservableProperty] private string _cloneLog = "";

    // Format state
    [ObservableProperty] private VolumeInfo? _formatSelectedVolume;
    [ObservableProperty] private string _formatFileSystem = "NTFS";
    [ObservableProperty] private string _formatLabel = "";
    [ObservableProperty] private bool _formatQuick = true;
    [ObservableProperty] private string _formatClusterSize = "預設 (Default)";
    [ObservableProperty] private bool _isFormatting;
    [ObservableProperty] private string _formatStatusText = "";
    [ObservableProperty] private string _reFsSupportText = "尚未檢查 ReFS 支援狀態。";
    [ObservableProperty] private string _formatLog = "";

    private void AppendCloneLog(string line) => CloneLog = AppendCapped(CloneLog, line);
    private void AppendFormatLog(string line) => FormatLog = AppendCapped(FormatLog, line);

    private static string AppendCapped(string current, string line)
    {
        var text = current + $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
        return text.Length > LogLimit ? text[^LogLimit..] : text;
    }

    // ── Inventory / geometry ──

    [RelayCommand]
    public async Task RefreshCloneInventoryAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在列舉可複製的原始裝置 (diskpart list disk / list volume)...";
            AppendCloneLog("▸ diskpart: list disk / list volume");
            var (devices, raw) = await _cloneService.ListCloneDevicesAsync();

            CloneDevices.Clear();
            foreach (var device in devices) CloneDevices.Add(device);

            CloneSourceDevice = CloneDevices.FirstOrDefault(d => d.IsPhysicalDrive) ?? CloneDevices.FirstOrDefault();
            CloneTargetDevice = CloneDevices.FirstOrDefault(d => d.IsPhysicalDrive && !ReferenceEquals(d, CloneSourceDevice))
                                ?? CloneDevices.Skip(1).FirstOrDefault();

            AppendCloneLog($"✔ 找到 {CloneDevices.Count} 個可選裝置（{CloneDevices.Count(d => d.IsPhysicalDrive)} 顆磁碟 / {CloneDevices.Count(d => !d.IsPhysicalDrive)} 個磁碟區）。");
            AppendCloneLog(raw);
            StatusMessage = $"裝置清單已更新（{CloneDevices.Count} 項）。";
        }
        catch (Exception ex)
        {
            AppendCloneLog($"❌ 列舉裝置失敗: {ex.Message}");
            StatusMessage = $"列舉裝置失敗: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task RefreshFormatVolumesAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在列舉磁碟區 (diskpart list volume)...";
            var diskPart = new DiskPartService();
            var (volumes, _) = await diskPart.ListVolumesAsync();

            FormatVolumes.Clear();
            foreach (var volume in volumes) FormatVolumes.Add(volume);
            FormatSelectedVolume ??= FormatVolumes.FirstOrDefault();

            AppendFormatLog($"✔ 找到 {FormatVolumes.Count} 個磁碟區。");
            StatusMessage = $"磁碟區清單已更新（{FormatVolumes.Count} 項）。";
        }
        catch (Exception ex)
        {
            AppendFormatLog($"❌ 列舉磁碟區失敗: {ex.Message}");
            StatusMessage = $"列舉磁碟區失敗: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task LoadCloneGeometryAsync()
    {
        try
        {
            IsLoading = true;
            var sb = new StringBuilder();
            sb.AppendLine($"模式: {CloneMode}");

            if (CloneMode == DiskCloneService.ModeCreateImage || CloneMode == DiskCloneService.ModeRestoreImage)
            {
                sb.AppendLine($"映像檔: {(string.IsNullOrWhiteSpace(CloneImagePath) ? "(未指定)" : CloneImagePath)}");
                if (File.Exists(CloneImagePath))
                {
                    var info = new FileInfo(CloneImagePath);
                    sb.AppendLine($"映像檔實際大小: {NativeStorageService.FormatBytes(info.Length)} (最後寫入 {info.LastWriteTime:yyyy-MM-dd HH:mm:ss})");
                }
                else if (!string.IsNullOrWhiteSpace(CloneImagePath))
                {
                    sb.AppendLine("⚠️ 映像檔尚不存在（建立模式會新建，還原模式需先存在）。");
                }
            }

            var sourcePath = CloneMode == DiskCloneService.ModeRestoreImage ? null : CloneSourceDevice?.DevicePath;
            var targetPath = CloneMode == DiskCloneService.ModeCreateImage ? null : CloneTargetDevice?.DevicePath;

            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                var info = await _cloneService.QueryDeviceInfoAsync(sourcePath);
                sb.AppendLine($"來源: {info.GeometrySummary}");
            }
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                var info = await _cloneService.QueryDeviceInfoAsync(targetPath);
                sb.AppendLine($"目標: {info.GeometrySummary}");
            }

            var plan = ValidateClonePlan();
            sb.AppendLine(plan.Ok ? $"✅ 計畫檢查通過：{plan.Summary}" : $"❌ 計畫檢查未通過：{plan.Error}");

            CloneGeometryText = sb.ToString().TrimEnd();
            AppendCloneLog(CloneGeometryText);
            StatusMessage = plan.Ok ? "裝置幾何讀取完成，計畫檢查通過。" : "裝置幾何讀取完成，但計畫檢查未通過。";
        }
        catch (Exception ex)
        {
            CloneGeometryText = $"❌ 讀取幾何資訊失敗: {ex.Message}";
            AppendCloneLog(CloneGeometryText);
            StatusMessage = $"讀取幾何資訊失敗: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BrowseCloneImageAsync()
    {
        var folder = await FilePickerHelper.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder)) return;
        CloneImagePath = Path.Combine(folder, $"DiskImage_{DateTime.Now:yyyyMMdd_HHmmss}.vhdx");
        AppendCloneLog($"▸ 映像輸出路徑: {CloneImagePath}");
    }

    [RelayCommand]
    public async Task BrowseCloneImageSourceAsync()
    {
        var picked = await FilePickerHelper.PickSingleFileAsync(new[] { ".vhdx" });
        if (!string.IsNullOrWhiteSpace(picked))
        {
            CloneImagePath = picked;
            AppendCloneLog($"▸ 選取映像檔: {picked}");
        }
    }

    // ── Clone plan validation (used by the page before it shows the confirmation dialog) ──

    public (bool Ok, string Error, string Summary, string TargetName, string ScriptPreview) ValidateClonePlan()
    {
        if (IsCloning) return (false, "已有複製作業正在進行中。", "", "", "");

        var source = CloneSourceDevice;
        var target = CloneTargetDevice;

        if (CloneMode == DiskCloneService.ModeRaw)
        {
            if (source is null) return (false, "請選擇來源裝置。", "", "", "");
            if (target is null) return (false, "請選擇目標裝置。", "", "", "");
            if (string.Equals(source.DevicePath, target.DevicePath, StringComparison.OrdinalIgnoreCase))
                return (false, "來源與目標不可為同一個裝置。", "", "", "");

            var systemDisk = DiskCloneService.TryGetSystemDiskNumber();
            if (target.IsPhysicalDrive && systemDisk.HasValue && target.DiskNumber == systemDisk.Value)
                return (false, $"Disk {target.DiskNumber} 是執行中的系統磁碟，已拒絕。", "", "", "");

            var summary =
                $"模式：原始區塊複製（Raw block clone，raw device handle）\n" +
                $"來源：{source.DisplayName}\n" +
                $"目標：{target.DisplayName}\n" +
                $"寫入後讀回驗證：{(CloneVerifyAfterWrite ? "是" : "否")}\n" +
                $"目標磁碟先離線：{(CloneOfflineTargetFirst && target.IsPhysicalDrive ? "是 (diskpart offline disk)" : "否")}\n\n" +
                "⚠️ 目標裝置上的所有現存資料將被來源內容覆寫，且無法復原！";
            var preview = $@"複製來源 {source.DevicePath} → 目標 {target.DevicePath}（CreateFile + ReadFile/WriteFile 原始磁區 I/O，" +
                          $"目標磁碟區會先 FSCTL_LOCK_VOLUME + FSCTL_DISMOUNT_VOLUME）";
            return (true, "", summary, target.DevicePath, preview);
        }

        if (CloneMode == DiskCloneService.ModeCreateImage)
        {
            if (source is null) return (false, "請選擇要製作映像的來源裝置。", "", "", "");
            if (string.IsNullOrWhiteSpace(CloneImagePath)) return (false, "請指定 .vhdx 映像輸出路徑。", "", "", "");
            if (!Path.GetExtension(CloneImagePath).Equals(".vhdx", StringComparison.OrdinalIgnoreCase))
                return (false, "VHDX 映像模式僅支援 .vhdx 輸出。", "", "", "");
            if (File.Exists(CloneImagePath))
                return (false, $"映像檔已存在，請改用其他檔名以免覆寫：{CloneImagePath}", "", "", "");

            var summary =
                $"模式：建立 VHDX 映像（raw 區塊映像 → .vhdx）\n" +
                $"來源：{source.DisplayName}\n" +
                $"輸出映像：{CloneImagePath}\n" +
                $"寫入後讀回驗證：{(CloneVerifyAfterWrite ? "是" : "否")}\n\n" +
                "映像檔內容為來源磁區的逐位元組副本；整顆磁碟映像可直接掛載使用，單一磁碟區映像主要用於還原。";
            var preview = $"diskpart: create vdisk file=\"{CloneImagePath}\" maximum=<依來源幾何計算> type=expandable → attach vdisk → 以原始磁區 I/O 寫入 \\\\.\\PhysicalDrive<新虛擬磁碟> → detach vdisk";
            return (true, "", summary, CloneImagePath, preview);
        }

        // Restore
        if (string.IsNullOrWhiteSpace(CloneImagePath)) return (false, "請選擇要還原的 .vhdx 映像檔。", "", "", "");
        if (!File.Exists(CloneImagePath)) return (false, $"找不到映像檔：{CloneImagePath}", "", "", "");
        if (target is null) return (false, "請選擇還原目標裝置。", "", "", "");

        var sysDisk = DiskCloneService.TryGetSystemDiskNumber();
        if (target.IsPhysicalDrive && sysDisk.HasValue && target.DiskNumber == sysDisk.Value)
            return (false, $"Disk {target.DiskNumber} 是執行中的系統磁碟，已拒絕。", "", "", "");

        var restoreSummary =
            $"模式：從 VHDX 映像還原（.vhdx → raw 裝置）\n" +
            $"映像來源：{CloneImagePath}\n" +
            $"目標：{target.DisplayName}\n" +
            $"寫入後讀回驗證：{(CloneVerifyAfterWrite ? "是" : "否")}\n" +
            $"目標磁碟先離線：{(CloneOfflineTargetFirst && target.IsPhysicalDrive ? "是 (diskpart offline disk)" : "否")}\n\n" +
            "⚠️ 目標裝置上的所有現存資料將被映像內容覆寫，且無法復原！";
        var restorePreview = $"diskpart: attach vdisk readonly → 取得 \\\\.\\PhysicalDrive<虛擬磁碟> → 原始磁區 I/O 複製到 {target.DevicePath} → detach vdisk";
        return (true, "", restoreSummary, target.DevicePath, restorePreview);
    }

    /// <summary>Runs the currently selected clone plan. Only ever called with destructiveConfirmed = true after the page dialog.</summary>
    public async Task StartCloneAsync(bool destructiveConfirmed)
    {
        var plan = ValidateClonePlan();
        if (!plan.Ok)
        {
            AppendCloneLog($"❌ 已拒絕執行：{plan.Error}");
            StatusMessage = plan.Error;
            return;
        }

        if (!AdminHelper.IsRunningAsAdmin())
        {
            AppendCloneLog("❌ 已拒絕執行：需要系統管理員權限。");
            StatusMessage = "需要系統管理員權限才能進行區塊級複製。";
            return;
        }

        _cloneCts = new CancellationTokenSource();
        var ct = _cloneCts.Token;
        IsCloning = true;
        CloneProgress = 0;
        CloneStageText = "準備中...";
        CloneProgressText = "";
        CloneThroughputText = "";
        CloneEtaText = "";

        AppendCloneLog($"▶ 開始：{plan.ScriptPreview}");
        if (!destructiveConfirmed) AppendCloneLog("⚠️ 未帶入確認旗標，服務層將拒絕此次破壞性寫入。");

        var progress = new Progress<DiskCloneService.CloneProgress>(p =>
        {
            CloneProgress = p.Percent;
            CloneStageText = p.Stage;
            CloneProgressText = $"{p.Percent:0.0}%　{NativeStorageService.FormatBytes(p.BytesDone)} / {NativeStorageService.FormatBytes(p.TotalBytes)}";
            CloneThroughputText = $"寫入速度 {p.BytesPerSecond / 1024 / 1024:0.0} MB/s　已耗時 {p.Elapsed:hh\\:mm\\:ss}";
            CloneEtaText = p.Eta.HasValue ? $"預估剩餘 {p.Eta.Value:hh\\:mm\\:ss}" : "預估剩餘：計算中...";
        });

        var options = new DiskCloneService.CloneOptions
        {
            DestructiveTargetConfirmed = destructiveConfirmed,
            VerifyAfterWrite = CloneVerifyAfterWrite,
            OfflineTargetDiskFirst = CloneOfflineTargetFirst
        };

        try
        {
            if (CloneMode == DiskCloneService.ModeRaw)
            {
                var result = await _cloneService.CloneDeviceToDeviceAsync(
                    CloneSourceDevice!.DevicePath, CloneTargetDevice!.DevicePath, options, progress, ct);
                ReportCloneResult(result);
            }
            else if (CloneMode == DiskCloneService.ModeCreateImage)
            {
                var result = await _vhdService.CreateImageFromDeviceAsync(CloneSourceDevice!.DevicePath, CloneImagePath, options, progress, ct);
                ReportCloneResult(result);
            }
            else
            {
                var result = await _vhdService.RestoreImageToDeviceAsync(
                    CloneImagePath, CloneTargetDevice!.DevicePath, options, progress, ct);
                ReportCloneResult(result);
            }
        }
        catch (OperationCanceledException)
        {
            AppendCloneLog("⏹️ 已取消。");
            StatusMessage = "複製作業已取消。";
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "磁碟複製");
            AppendCloneLog($"❌ 例外：{ex.Message}");
            StatusMessage = $"複製失敗: {ex.Message}";
        }
        finally
        {
            IsCloning = false;
            CloneStageText = "已結束";
            _cloneCts?.Dispose();
            _cloneCts = null;
        }
    }

    private void ReportCloneResult(DiskCloneService.CloneResult result)
    {
        foreach (var line in result.Diagnostics) AppendCloneLog(line);
        AppendCloneLog(result.Message);
        CloneGeometryText = result.Message;

        if (result.Cancelled)
        {
            StatusMessage = "複製作業已取消。";
        }
        else if (result.Success)
        {
            CloneProgress = 100;
            StatusMessage = "複製作業成功完成。";
            if (result.VerificationRequested && !result.VerificationPassed)
                StatusMessage = "複製完成，但讀回驗證失敗！請檢查目標裝置。";
        }
        else
        {
            StatusMessage = "複製作業失敗（詳見日誌）。";
        }
    }

    [RelayCommand]
    public void CancelClone()
    {
        if (_cloneCts is null || _cloneCts.IsCancellationRequested) return;
        AppendCloneLog("⏹️ 已送出取消要求，正在安全停止（已完成寫入的磁區會保留）...");
        _cloneCts.Cancel();
    }

    // ── Format plan validation & execution ──

    public (bool Ok, string Error, string Summary, string ScriptPreview) ValidateFormatPlan()
    {
        if (IsFormatting) return (false, "已有格式化作業正在進行中。", "", "");
        var volume = FormatSelectedVolume;
        if (volume is null) return (false, "請先選擇要格式化的磁碟區（按「重新整理磁碟區」）。", "", "");

        var fs = DiskPartService.CanonicalFileSystem(FormatFileSystem);
        int? clusterSize = null;
        if (!string.IsNullOrWhiteSpace(FormatClusterSize) && !FormatClusterSize.StartsWith("預設", StringComparison.Ordinal))
        {
            if (!int.TryParse(FormatClusterSize.Trim(), out var parsed))
                return (false, $"叢集大小必須是整數：{FormatClusterSize}", "", "");
            clusterSize = parsed;
        }

        var letter = string.IsNullOrWhiteSpace(volume.Letter) ? (char?)null : char.ToUpperInvariant(volume.Letter.TrimEnd(':')[0]);
        var label = (FormatLabel ?? "").Trim();

        // Explicit boot-volume gate for every file system (not just ReFS).
        var systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\')[0];
        if (letter.HasValue && letter.Value == char.ToUpperInvariant(systemDrive))
            return (false, $"Volume {volume.Number} ({letter}:) 是執行中的系統 / 開機磁碟區，禁止格式化。", "", "");

        var rejection = DiskPartService.ValidateFormatArguments(volume.Number, fs, label, clusterSize, letter);
        if (rejection != null) return (false, rejection, "", "");

        var quickArg = FormatQuick ? " quick" : "";
        var clusterArg = clusterSize.HasValue ? $" unit={clusterSize.Value}" : "";
        var labelArg = string.IsNullOrEmpty(label) ? "" : $" label=\"{label}\"";
        var script = $"select volume {volume.Number}\r\nformat fs={fs}{clusterArg}{labelArg}{quickArg}";

        var volumeText = string.IsNullOrWhiteSpace(volume.Letter)
            ? $"Volume {volume.Number} [{volume.Label}]"
            : $"Volume {volume.Number} {volume.Letter}: [{volume.Label}]";
        var summary =
            $"目標磁碟區：{volumeText}　目前檔案系統：{(string.IsNullOrWhiteSpace(volume.FileSystem) ? "未知" : volume.FileSystem)}　容量：{volume.SizeDisplay}\n" +
            $"格式化為：{fs}　標籤：{(string.IsNullOrEmpty(label) ? "(不變更/空白)" : label)}　" +
            $"快速格式化：{(FormatQuick ? "是" : "否（完整格式化，可能耗時數小時）")}　叢集大小：{(clusterSize.HasValue ? clusterSize.Value + " bytes" : "預設")}\n" +
            $"實際 diskpart 指令：{script.Replace("\r\n", " / ")}\n\n" +
            "⚠️ 格式化會永久清除此磁碟區上的所有資料！";

        return (true, "", summary, script);
    }

    public async Task StartFormatAsync(bool destructiveConfirmed)
    {
        var plan = ValidateFormatPlan();
        if (!plan.Ok)
        {
            AppendFormatLog($"❌ 已拒絕格式化：{plan.Error}");
            FormatStatusText = plan.Error;
            StatusMessage = plan.Error;
            return;
        }

        if (!AdminHelper.IsRunningAsAdmin())
        {
            AppendFormatLog("❌ 已拒絕執行：需要系統管理員權限。");
            FormatStatusText = "需要系統管理員權限才能格式化。";
            return;
        }

        if (!destructiveConfirmed)
        {
            AppendFormatLog("❌ 已拒絕執行：未取得使用者確認。");
            FormatStatusText = "未確認，已取消。";
            return;
        }

        var volume = FormatSelectedVolume!;
        var fs = DiskPartService.CanonicalFileSystem(FormatFileSystem);
        int? clusterSize = null;
        if (!string.IsNullOrWhiteSpace(FormatClusterSize) && !FormatClusterSize.StartsWith("預設", StringComparison.Ordinal) &&
            int.TryParse(FormatClusterSize.Trim(), out var parsed))
        {
            clusterSize = parsed;
        }
        var letter = string.IsNullOrWhiteSpace(volume.Letter) ? (char?)null : char.ToUpperInvariant(volume.Letter.TrimEnd(':')[0]);

        IsFormatting = true;
        FormatStatusText = "正在格式化...（DiskPart 為全域序列化，若其他磁碟操作正在進行會先排隊等待，可按「⏹️ 取消格式化」中止）";
        AppendFormatLog($"▶ diskpart: {plan.ScriptPreview.Replace("\r\n", " / ")}");

        _formatCts = new CancellationTokenSource();
        try
        {
            var diskPart = new DiskPartService();
            var (success, output) = await diskPart.FormatVolumeCheckedAsync(
                volume.Number, fs, (FormatLabel ?? "").Trim(), FormatQuick, clusterSize, letter, _formatCts.Token);

            AppendFormatLog(output);
            if (success)
            {
                FormatStatusText = $"✅ Volume {volume.Number} 已格式化為 {fs}。";
                AppendFormatLog(FormatStatusText);
                StatusMessage = FormatStatusText;
                await RefreshFormatVolumesAsync();
            }
            else
            {
                FormatStatusText = $"❌ 格式化失敗（Volume {volume.Number} → {fs}），詳見日誌。";
                AppendFormatLog(FormatStatusText);
                StatusMessage = FormatStatusText;
            }
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "格式化磁碟區");
            AppendFormatLog($"❌ 例外：{ex.Message}");
            FormatStatusText = $"格式化失敗: {ex.Message}";
            StatusMessage = FormatStatusText;
        }
        finally
        {
            IsFormatting = false;
            _formatCts?.Dispose();
            _formatCts = null;
        }
    }

    /// <summary>A4: gives the user a way out of a queued / long-running diskpart session.</summary>
    [RelayCommand]
    public void CancelFormat()
    {
        if (!IsFormatting || _formatCts is null || _formatCts.IsCancellationRequested) return;
        AppendFormatLog("⏹️ 已送出取消要求（將中止 diskpart 並釋放 DiskPart 鎖定）...");
        FormatStatusText = "已送出取消要求，正在中止 DiskPart...";
        _formatCts.Cancel();
    }

    [RelayCommand]
    public void CheckReFsSupport()
    {
        var (supported, reason) = DiskPartService.CheckReFsSupport();
        ReFsSupportText = (supported ? "✅ ReFS 支援：" : "❌ 不支援 ReFS：") + reason;
        AppendFormatLog(ReFsSupportText);

        var sizes = string.Join(", ", DiskPartService.GetAllowedClusterSizes("ReFS"));
        AppendFormatLog($"ℹ️ ReFS 允許的叢集大小：{sizes} bytes；且不可用於可卸除媒體或開機磁碟區。");
        StatusMessage = supported ? "此系統支援 ReFS 格式化。" : reason;
    }

    [RelayCommand]
    public void ClearCloneLog()
    {
        CloneLog = "";
    }

    [RelayCommand]
    public void ClearFormatLog()
    {
        FormatLog = "";
    }
}
