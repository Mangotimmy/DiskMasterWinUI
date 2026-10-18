using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

public partial class BootManagerViewModel : ObservableObject
{
    private readonly BcdManagerService _bcdService = new();
    private readonly WinReRecoveryService _winReService = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _searchFilterText = "";
    [ObservableProperty] private BootEntryDetail? _selectedEntry;
    [ObservableProperty] private string _editDescriptionText = "";
    [ObservableProperty] private int _timeoutSeconds = 30;
    [ObservableProperty] private bool _isModernMenuPolicy = true;

    // WinRE Recovery Environment Manager
    [ObservableProperty] private WinReStatusInfo _winReStatus = new();
    [ObservableProperty] private string _winReOperationStatus = "準備就緒";
    [ObservableProperty] private int _winReDiskNumber = 0;
    [ObservableProperty] private int _winRePartitionSizeMB = 1000;
    [ObservableProperty] private string _winReCustomImageLocation = "";

    // Build New Boot Partition Wizard
    [ObservableProperty] private string _newBootSourcePath = @"C:\Windows";
    [ObservableProperty] private int _newBootDiskNumber = 0;
    [ObservableProperty] private int _newBootSizeMB = 260;
    [ObservableProperty] private string _newBootFirmware = "UEFI";
    [ObservableProperty] private string _newBootLetter = "S:";
    [ObservableProperty] private bool _newBootRemoveLetter = false;
    [ObservableProperty] private string _newBootLog = "";
    [ObservableProperty] private bool _isBuildingBoot;

    public ObservableCollection<BootEntryDetail> AllEntries { get; } = new();
    public ObservableCollection<BootEntryDetail> DisplayEntries { get; } = new();
    public ObservableCollection<string> DiscoveredWindows { get; } = new();
    public ObservableCollection<string> FirmwareOptions { get; } = new() { "UEFI", "BIOS", "ALL" };

    public BootManagerViewModel()
    {
    }

    [RelayCommand]
    public async Task RefreshEntriesAsync()
    {
        DebugLogService.Instance.LogAction("開機管理 (BootManager)", "重新整理 BCD 開機項目清單");
        try
        {
            IsLoading = true;
            StatusMessage = "Enumerating BCD boot entries...";
            var (entries, raw) = await _bcdService.EnumBootEntriesAsync();

            AllEntries.Clear();
            foreach (var e in entries) AllEntries.Add(e);

            ApplyFilter();

            if (DisplayEntries.Count > 0 && SelectedEntry == null)
            {
                SelectedEntry = DisplayEntries[0];
            }

            StatusMessage = $"Loaded {AllEntries.Count} BCD entries.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ScanWindowsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Scanning for offline and unlisted Windows installations...";
            var found = await _bcdService.ScanInstalledWindowsAsync();

            DiscoveredWindows.Clear();
            foreach (var path in found) DiscoveredWindows.Add(path);

            StatusMessage = $"Found {found.Count} Windows installations across all disks.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        DisplayEntries.Clear();
        var q = SearchFilterText.Trim();

        foreach (var e in AllEntries)
        {
            if (string.IsNullOrEmpty(q) ||
                e.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Identifier.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Device.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                DisplayEntries.Add(e);
            }
        }
    }

    partial void OnSelectedEntryChanged(BootEntryDetail? value)
    {
        if (value != null)
        {
            EditDescriptionText = value.Description;
        }
    }

    [RelayCommand]
    public async Task SetDefaultEntryAsync()
    {
        if (SelectedEntry == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Setting {SelectedEntry.Identifier} as default...";
            var res = await _bcdService.SetDefaultEntryAsync(SelectedEntry.Identifier);
            StatusMessage = "Set as default: " + res.Trim();
            await RefreshEntriesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task SaveDescriptionAsync()
    {
        if (SelectedEntry == null || string.IsNullOrWhiteSpace(EditDescriptionText)) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Renaming {SelectedEntry.Identifier} to '{EditDescriptionText}'...";
            var res = await _bcdService.SetDescriptionAsync(SelectedEntry.Identifier, EditDescriptionText);
            StatusMessage = "Renamed: " + res.Trim();
            await RefreshEntriesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task DeleteEntryAsync()
    {
        if (SelectedEntry == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Deleting entry {SelectedEntry.Identifier}...";
            var res = await _bcdService.DeleteEntryAsync(SelectedEntry.Identifier);
            StatusMessage = "Deleted: " + res.Trim();
            await RefreshEntriesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CloneEntryAsync()
    {
        if (SelectedEntry == null) return;
        try
        {
            IsLoading = true;
            var newDesc = SelectedEntry.Description + " (Copy)";
            StatusMessage = $"Cloning entry to '{newDesc}'...";
            var res = await _bcdService.CloneEntryAsync(SelectedEntry.Identifier, newDesc);
            StatusMessage = "Cloned: " + res.Trim();
            await RefreshEntriesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task ApplyGlobalSettingsAsync()
    {
        try
        {
            IsLoading = true;
            await _bcdService.SetTimeoutAsync(TimeoutSeconds);
            await _bcdService.SetBootMenuPolicyAsync(IsModernMenuPolicy);
            StatusMessage = $"Global boot settings applied: {TimeoutSeconds}s, {(IsModernMenuPolicy ? "Standard" : "Legacy")}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task AddToBcdAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Adding {path} to BCD...";
            var res = await _bcdService.AddWindowsToBcdAsync(path);
            StatusMessage = "Result: " + res.Trim();
            await RefreshEntriesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BuildNewBootPartitionAsync()
    {
        try
        {
            IsBuildingBoot = true;
            NewBootLog = "";
            StatusMessage = "Building new boot partition...";

            var config = new NewBootConfig
            {
                SourceWindowsPath = NewBootSourcePath,
                TargetDiskNumber = NewBootDiskNumber,
                PartitionSizeMB = NewBootSizeMB,
                FirmwareType = NewBootFirmware,
                TargetPartitionLetter = NewBootLetter,
                RemoveLetterAfterBuild = NewBootRemoveLetter,
                CreateNewPartition = true
            };

            var result = await _bcdService.BuildNewBootPartitionAsync(config, msg =>
            {
                DispatcherHelper.RunOnUIThread(() =>
                {
                    NewBootLog += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
                });
            });

            StatusMessage = "New boot partition build complete!";
            await RefreshEntriesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Build Error: {ex.Message}";
            NewBootLog += $"\n[ERROR] {ex.Message}\n";
        }
        finally
        {
            IsBuildingBoot = false;
        }
    }

    private readonly BootRepairService _bootRepair = new();
    [ObservableProperty] private string _espMountLetter = "S:";

    [RelayCommand]
    public async Task MountEspAsync()
    {
        DebugLogService.Instance.LogAction("開機管理 (BootManager)", $"掛載 ESP 分割區至 {EspMountLetter}");
        try
        {
            IsLoading = true;
            StatusMessage = $"Mounting ESP to {EspMountLetter}...";
            var output = await _bootRepair.MountEspAsync(EspMountLetter);
            StatusMessage = $"ESP Mounted to {EspMountLetter}. {output}".Trim();
            await RefreshEntriesAsync();
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task UnmountEspAsync()
    {
        DebugLogService.Instance.LogAction("開機管理 (BootManager)", $"卸載 ESP 分割區 {EspMountLetter}");
        try
        {
            IsLoading = true;
            StatusMessage = $"Unmounting ESP from {EspMountLetter}...";
            var output = await _bootRepair.UnmountEspAsync(EspMountLetter);
            StatusMessage = $"ESP Unmounted. {output}".Trim();
            await RefreshEntriesAsync();
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BcdExportAsync()
    {
        var suggestedName = $"BCD_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.bcd";
        var filePath = await FilePickerHelper.PickSaveFileAsync(suggestedName, ".bcd", "BCD Backup File");
        if (string.IsNullOrWhiteSpace(filePath)) return;

        DebugLogService.Instance.LogAction("開機管理 (BootManager)", $"匯出並備份 BCD 開機組態至 {filePath}");
        try
        {
            IsLoading = true;
            StatusMessage = $"Exporting BCD to {filePath}...";
            var output = await _bootRepair.BcdEditExportAsync(filePath);
            StatusMessage = "BCD exported successfully.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BcdImportAsync()
    {
        var filePath = await FilePickerHelper.PickSingleFileAsync([".bcd", "*"]);
        if (string.IsNullOrWhiteSpace(filePath)) return;

        DebugLogService.Instance.LogAction("開機管理 (BootManager)", $"還原 BCD 開機組態: {filePath}");
        try
        {
            IsLoading = true;
            StatusMessage = $"Importing BCD from {filePath}...";
            var output = await _bootRepair.BcdEditImportAsync(filePath);
            StatusMessage = "BCD imported successfully.";
            await RefreshEntriesAsync();
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    // ══════════════════════════════════════════════════════════════
    // Safe Boot & MSConfig Boot Options
    // ══════════════════════════════════════════════════════════════

    [ObservableProperty] private SafeBootConfig _safeBootConfig = new();
    [ObservableProperty] private string _safeBootMode = "Normal";
    [ObservableProperty] private bool _noGuiBoot;
    [ObservableProperty] private bool _bootLog;
    [ObservableProperty] private bool _baseVideo;
    [ObservableProperty] private bool _sos;
    [ObservableProperty] private bool _testSigning;
    [ObservableProperty] private bool _noIntegrityChecks;
    [ObservableProperty] private bool _hypervisorEnabled = true;
    [ObservableProperty] private string _safeBootOperationStatus = "";

    [RelayCommand]
    public async Task LoadSafeBootConfigAsync()
    {
        try
        {
            IsLoading = true;
            SafeBootOperationStatus = "讀取開機 BCD 安全設定中...";
            SafeBootConfig = await _bcdService.GetSafeBootConfigAsync();
            SafeBootMode = SafeBootConfig.SafeBootMode;
            NoGuiBoot = SafeBootConfig.NoGuiBoot;
            BootLog = SafeBootConfig.BootLog;
            BaseVideo = SafeBootConfig.BaseVideo;
            Sos = SafeBootConfig.Sos;
            TestSigning = SafeBootConfig.TestSigning;
            NoIntegrityChecks = SafeBootConfig.NoIntegrityChecks;
            HypervisorEnabled = !SafeBootConfig.HypervisorLaunchType.Equals("off", StringComparison.OrdinalIgnoreCase);
            SafeBootOperationStatus = $"目前安全開機模式: {SafeBootMode}";
        }
        catch (Exception ex)
        {
            SafeBootOperationStatus = $"讀取失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ApplySafeBootConfigAsync()
    {
        try
        {
            IsLoading = true;
            SafeBootOperationStatus = "正在寫入 BCD 安全開機設定...";

            var config = new SafeBootConfig
            {
                SafeBootMode = SafeBootMode,
                NoGuiBoot = NoGuiBoot,
                BootLog = BootLog,
                BaseVideo = BaseVideo,
                Sos = Sos,
                TestSigning = TestSigning,
                NoIntegrityChecks = NoIntegrityChecks,
                HypervisorLaunchType = HypervisorEnabled ? "auto" : "off"
            };

            var result = await _bcdService.ApplySafeBootConfigAsync(config);
            DebugLogService.Instance.LogAction("開機管理 (BootManager)", $"套用安全開機設定 (模式: {SafeBootMode})");
            SafeBootConfig = config;
            SafeBootOperationStatus = "安全開機與啟動旗標設定已成功生效！";
            AudioFeedbackService.PlaySuccess();
        }
        catch (Exception ex)
        {
            SafeBootOperationStatus = $"寫入失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ═══ TPM 2.0 & Win11 Security LabConfig Bypass ═══
    private readonly TpmService _tpmService = new();
    [ObservableProperty] private TpmStatusInfo _tpmStatus = new();
    [ObservableProperty] private bool _isWin11BypassEnabled;
    [ObservableProperty] private string _tpmOperationStatus = "就緒";

    [RelayCommand]
    public async Task LoadTpmStatusAsync()
    {
        try
        {
            IsLoading = true;
            TpmOperationStatus = "正在探測硬體 TPM 2.0 與安全性狀態...";
            TpmStatus = await _tpmService.GetTpmStatusAsync();
            IsWin11BypassEnabled = TpmStatus.IsWin11BypassActive;
            TpmOperationStatus = TpmStatus.StatusSummary;
        }
        catch (Exception ex)
        {
            TpmOperationStatus = $"TPM 查詢失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ToggleWin11BypassAsync()
    {
        try
        {
            IsLoading = true;
            bool targetState = !IsWin11BypassEnabled;
            TpmOperationStatus = targetState ? "正在寫入 LabConfig 繞過登錄檔..." : "正在還原 LabConfig 預設設定...";
            var (ok, msg) = await _tpmService.SetWin11RequirementBypassAsync(targetState);
            if (ok)
            {
                IsWin11BypassEnabled = targetState;
                TpmOperationStatus = msg;
                AudioFeedbackService.PlaySuccess();
            }
            else
            {
                TpmOperationStatus = msg;
                AudioFeedbackService.PlayError();
            }
            await LoadTpmStatusAsync();
        }
        catch (Exception ex)
        {
            TpmOperationStatus = $"操作失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void OpenTpmMsc()
    {
        _tpmService.OpenTpmManagementConsole();
    }

    [RelayCommand]
    public async Task RefreshWinReStatusAsync()
    {
        try
        {
            IsLoading = true;
            WinReOperationStatus = "正在探測 Windows RE (WinRE) 修復環境狀態...";
            WinReStatus = await _winReService.GetStatusAsync();
            WinReOperationStatus = WinReStatus.IsEnabled ? "Windows RE 已啟用" : "Windows RE 目前為停用狀態";
        }
        catch (Exception ex)
        {
            WinReOperationStatus = $"WinRE 查詢失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task EnableWinReAsync()
    {
        try
        {
            IsLoading = true;
            WinReOperationStatus = "正在啟用 Windows RE (reagentc /enable)...";
            var (ok, msg) = await _winReService.EnableWinReAsync();
            WinReOperationStatus = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await RefreshWinReStatusAsync();
        }
        catch (Exception ex)
        {
            WinReOperationStatus = $"啟用失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DisableWinReAsync()
    {
        try
        {
            IsLoading = true;
            WinReOperationStatus = "正在停用 Windows RE (reagentc /disable)...";
            var (ok, msg) = await _winReService.DisableWinReAsync();
            WinReOperationStatus = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await RefreshWinReStatusAsync();
        }
        catch (Exception ex)
        {
            WinReOperationStatus = $"停用失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task BootToWinReAsync()
    {
        try
        {
            IsLoading = true;
            WinReOperationStatus = "正在設定重開機導向 WinRE 修復模式 (reagentc /boottore)...";
            var (ok, msg) = await _winReService.BootToRecoveryEnvironmentAsync();
            WinReOperationStatus = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        }
        catch (Exception ex)
        {
            WinReOperationStatus = $"設定開機修復失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SetReimagePathAsync()
    {
        if (string.IsNullOrWhiteSpace(WinReCustomImageLocation)) return;
        try
        {
            IsLoading = true;
            WinReOperationStatus = $"正在重新配置 WinRE 映像路徑: {WinReCustomImageLocation}...";
            var (ok, msg) = await _winReService.SetReimagePathAsync(WinReCustomImageLocation);
            WinReOperationStatus = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await RefreshWinReStatusAsync();
        }
        catch (Exception ex)
        {
            WinReOperationStatus = $"配置失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CreateDedicatedRecoveryPartitionAsync()
    {
        try
        {
            IsLoading = true;
            WinReOperationStatus = $"正在磁碟 {WinReDiskNumber} 建立 {WinRePartitionSizeMB} MB 專用修復分割區...";
            var (ok, msg) = await _winReService.CreateDedicatedRecoveryPartitionAsync(WinReDiskNumber, WinRePartitionSizeMB);
            WinReOperationStatus = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await RefreshWinReStatusAsync();
        }
        catch (Exception ex)
        {
            WinReOperationStatus = $"建立修復磁區失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsLoading = false;
        }
    }
}

