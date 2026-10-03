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

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _searchFilterText = "";
    [ObservableProperty] private BootEntryDetail? _selectedEntry;
    [ObservableProperty] private string _editDescriptionText = "";
    [ObservableProperty] private int _timeoutSeconds = 30;
    [ObservableProperty] private bool _isModernMenuPolicy = true;

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
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        var hwnd = WindowHelper.GetWindowHandle();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("BCD Backup File", new List<string> { ".bcd" });
        picker.SuggestedFileName = $"BCD_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.bcd";

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Exporting BCD to {file.Path}...";
            var output = await _bootRepair.BcdEditExportAsync(file.Path);
            StatusMessage = "BCD exported successfully.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BcdImportAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        var hwnd = WindowHelper.GetWindowHandle();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.FileTypeFilter.Add(".bcd");
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Importing BCD from {file.Path}...";
            var output = await _bootRepair.BcdEditImportAsync(file.Path);
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
            SafeBootConfig = config;
            SafeBootOperationStatus = "安全開機與啟動旗標設定已成功生效！";
        }
        catch (Exception ex)
        {
            SafeBootOperationStatus = $"寫入失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
