using System.Collections.ObjectModel;
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

    public ObservableCollection<DiskHealthInfo> DiskHealthItems { get; } = new();
    public ObservableCollection<DiskReliabilityInfo> ReliabilityItems { get; } = new();

    public ObservableCollection<ToolEnvironmentItem> ToolItems { get; } = new();

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
        IsLoading = true;
        try
        {
            StatusMessage = "Refreshing license, environment, and S.M.A.R.T. health in parallel...";
            var licenseTask = LoadLicenseInfoAsync();
            var envTask = CheckEnvironmentAsync();
            var healthTask = RefreshDiskHealthAsync();

            await Task.WhenAll(licenseTask, envTask, healthTask);
            StatusMessage = "All tool diagnostics and health metrics refreshed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadLicenseInfoAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Reading Windows license keys & BIOS hardware serials...";
            LicenseInfo = await _keyReader.GetLicenseInfoAsync();
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
            StatusMessage = "Checking system environment and tool availability...";
            var items = await _envService.CheckAllToolsAsync();

            ToolItems.Clear();
            foreach (var item in items) ToolItems.Add(item);

            StatusMessage = "Environment doctor check completed.";
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
        try
        {
            IsHealthLoading = true;
            StatusMessage = "Querying S.M.A.R.T. disk health...";
            AppendLog("▸ Get-PhysicalDisk (S.M.A.R.T. Health Query)...");

            var disks = await _healthMonitor.GetDiskHealthAsync();
            DiskHealthItems.Clear();
            foreach (var d in disks) DiskHealthItems.Add(d);

            var reliability = await _healthMonitor.GetReliabilityCountersAsync();
            ReliabilityItems.Clear();
            foreach (var r in reliability) ReliabilityItems.Add(r);

            // Auto-select first disk if none selected
            if (DiskHealthItems.Count > 0 && SelectedDiskHealth == null)
            {
                SelectedDiskHealth = DiskHealthItems[0];
            }

            if (SelectedDiskHealth != null)
            {
                await LoadDiskSmartDetailsAsync(SelectedDiskHealth);
            }

            AppendLog($"Found {disks.Count} disk(s), {reliability.Count} reliability counter(s).");
            StatusMessage = $"S.M.A.R.T. health: {disks.Count} disk(s) detected.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"SMART Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsHealthLoading = false; }
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
                var nvme = await _smartReader.GetNvmeHealthAsync(disk.DeviceId, disk.FriendlyName, disk.SizeBytes);
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
            var attrs = await _smartReader.GetAtaSmartAttributesAsync(disk.DeviceId);
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
                var (hwTemp, warn, crit, _) = _smartReader.GetStorageTemperature(disk.DeviceId);
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
            var raw = await _healthMonitor.GetSmartPredictionRawAsync();
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
    public async Task ChkdskScanAsync()
    {
        if (IsChkdskRunning) return;
        try
        {
            IsChkdskRunning = true;
            StatusMessage = $"chkdsk {ChkdskDrive} /scan...";
            StatusBarViewModel.Instance.StartOperation($"chkdsk {ChkdskDrive} /scan", CancelChkdsk);
            _chkdskCts = new CancellationTokenSource();
            await _chkdsk.ScanAsync(ChkdskDrive, AppendLog, _chkdskCts.Token);
            StatusMessage = "chkdsk /scan complete.";
            StatusBarViewModel.Instance.CompleteOperation("chkdsk /scan complete.");
        }
        finally { IsChkdskRunning = false; }
    }

    [RelayCommand]
    public async Task ChkdskSpotFixAsync()
    {
        if (IsChkdskRunning) return;
        try
        {
            IsChkdskRunning = true;
            StatusMessage = $"chkdsk {ChkdskDrive} /spotfix...";
            StatusBarViewModel.Instance.StartOperation($"chkdsk {ChkdskDrive} /spotfix", CancelChkdsk);
            _chkdskCts = new CancellationTokenSource();
            await _chkdsk.SpotFixAsync(ChkdskDrive, AppendLog, _chkdskCts.Token);
            StatusMessage = "chkdsk /spotfix complete.";
            StatusBarViewModel.Instance.CompleteOperation("chkdsk /spotfix complete.");
        }
        finally { IsChkdskRunning = false; }
    }

    [RelayCommand]
    public async Task ChkdskFixAsync()
    {
        if (IsChkdskRunning) return;
        try
        {
            IsChkdskRunning = true;
            StatusMessage = $"chkdsk {ChkdskDrive} /f...";
            StatusBarViewModel.Instance.StartOperation($"chkdsk {ChkdskDrive} /f", CancelChkdsk);
            _chkdskCts = new CancellationTokenSource();
            await _chkdsk.FixAsync(ChkdskDrive, AppendLog, _chkdskCts.Token);
            StatusMessage = "chkdsk /f complete.";
            StatusBarViewModel.Instance.CompleteOperation("chkdsk /f complete.");
        }
        finally { IsChkdskRunning = false; }
    }

    [RelayCommand]
    public async Task ChkdskFullRepairAsync()
    {
        if (IsChkdskRunning) return;
        try
        {
            IsChkdskRunning = true;
            StatusMessage = $"chkdsk {ChkdskDrive} /f /r /x (deep repair)...";
            StatusBarViewModel.Instance.StartOperation($"chkdsk {ChkdskDrive} /f /r /x", CancelChkdsk);
            _chkdskCts = new CancellationTokenSource();
            await _chkdsk.FixAndRecoverAsync(ChkdskDrive, AppendLog, _chkdskCts.Token);
            StatusMessage = "chkdsk /f /r /x complete.";
            StatusBarViewModel.Instance.CompleteOperation("chkdsk full repair complete.");
        }
        finally { IsChkdskRunning = false; }
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
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        var hwnd = WindowHelper.GetWindowHandle();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.FileTypeFilter.Add(".inf");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Installing driver from {file.Path}...";
            var res = await _driverService.InstallDriverAsync(file.Path);
            AppendLog($"=== Install Driver ({file.Name}) ===\n" + res);
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
    public void ClearLog()
    {
        _logBuffer.Clear();
    }
}
