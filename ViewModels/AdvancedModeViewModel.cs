using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

public partial class AdvancedModeViewModel : ObservableObject
{
    private readonly DiskPartService _diskPart = new();
    private readonly BootRepairService _bootRepair = new();

    // DiskPart tab
    [ObservableProperty] private string _diskPartScript = "list disk\r\nlist volume";
    [ObservableProperty] private string _diskPartOutput = "";

    // Boot Repair tab
    [ObservableProperty] private string _bootOutput = "";
    [ObservableProperty] private string _bcdEditCustomArgs = "";
    [ObservableProperty] private string _bcdBootCustomArgs = "";

    // BCDEdit tab
    [ObservableProperty] private BcdEntry? _selectedBcdEntry;
    [ObservableProperty] private string _espDriveLetter = "S:";
    [ObservableProperty] private string _bcdBackupPath = @"D:\bcd_backup";

    // General
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _fullLog = "";
    [ObservableProperty] private bool _isBootrecAvailable;
    [ObservableProperty] private bool _isBcdBootAvailable = true;

    public ObservableCollection<BcdEntry> BcdEntries { get; } = new();

    public AdvancedModeViewModel()
    {
        IsBootrecAvailable = BootRepairService.IsBootrecAvailable();
        IsBcdBootAvailable = BootRepairService.IsBcdBootAvailable();
    }

    private void AppendLog(string header, string message)
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            var separator = new string('=', 60);
            var entry = $"\n{separator}\n[{DateTime.Now:HH:mm:ss}] {header}\n{separator}\n{message}\n";
            if (FullLog.Length > 200000)
            {
                FullLog = FullLog.Substring(FullLog.Length - 100000);
            }
            FullLog += entry;
        });
    }

    // ════════════════════════════════════════
    //  DiskPart Tab
    // ════════════════════════════════════════

    [RelayCommand]
    private async Task ExecuteDiskPartScriptAsync()
    {
        if (string.IsNullOrWhiteSpace(DiskPartScript)) return;
        try
        {
            IsLoading = true;
            StatusMessage = "Executing DiskPart script...";
            var output = await _diskPart.RunCustomScriptAsync(DiskPartScript);
            DiskPartOutput = output;
            AppendLog("DiskPart Script", output);
            StatusMessage = "DiskPart script executed.";
            AudioFeedbackService.PlaySuccess();
        }
        catch (Exception ex)
        {
            DiskPartOutput = $"ERROR: {ex.Message}";
            StatusMessage = $"Error: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void InsertDiskPartTemplate(string template)
    {
        DiskPartScript = template switch
        {
            "ListAll" => "list disk\r\nlist volume",
            "CleanGPT" => "select disk [N]\r\nclean\r\nconvert gpt\r\ncreate partition efi size=260\r\nformat fs=fat32 quick\r\nassign letter=S\r\ncreate partition msr size=16\r\ncreate partition primary\r\nformat fs=ntfs quick\r\nassign letter=C",
            "CleanMBR" => "select disk [N]\r\nclean\r\nconvert mbr\r\ncreate partition primary\r\nformat fs=ntfs quick\r\nassign letter=C\r\nactive",
            "CreateUSB" => "select disk [N]\r\nclean\r\ncreate partition primary\r\nformat fs=fat32 quick\r\nassign letter=E\r\nactive",
            "WinInstall" => "select disk [N]\r\nclean\r\nconvert gpt\r\ncreate partition efi size=260\r\nformat fs=fat32 quick label=\"EFI\"\r\nassign letter=S\r\ncreate partition msr size=16\r\ncreate partition primary\r\nformat fs=ntfs quick label=\"Windows\"\r\nassign letter=W\r\ncreate partition primary\r\nformat fs=ntfs quick label=\"Recovery\"\r\nassign letter=R\r\nset id=\"de94bba4-06d1-4d40-a16a-bfd50179d6ac\"\r\ngpt attributes=0x8000000000000001",
            "StandardUEFI" => "select disk [N]\r\nclean\r\nconvert gpt\r\ncreate partition efi size=260\r\nformat quick fs=fat32 label=\"System\"\r\nassign letter=S\r\ncreate partition msr size=16\r\ncreate partition primary\r\nshrink minimum=1000\r\nformat quick fs=ntfs label=\"Windows\"\r\nassign letter=W\r\ncreate partition primary\r\nformat quick fs=ntfs label=\"Recovery\"\r\nset id=de94bba4-06d1-4d40-a16a-bfd50179d6ac\r\ngpt attributes=0x8000000000000001",
            "WinRERecovery" => "select disk [N]\r\ncreate partition primary size=1000\r\nformat quick fs=ntfs label=\"Recovery\"\r\nset id=de94bba4-06d1-4d40-a16a-bfd50179d6ac\r\ngpt attributes=0x8000000000000001",
            "FixSignatureCollision" => "select disk [N]\r\nuniqueid disk\r\nuniqueid disk id=12345678\r\nonline disk",
            "ExtendFS" => "select volume [N]\r\nextend filesystem",
            _ => DiskPartScript
        };
    }

    // ════════════════════════════════════════
    //  Boot Repair Tab
    // ════════════════════════════════════════

    [RelayCommand]
    private async Task BootrecFixMbrAsync()
    {
        IsLoading = true;
        StatusMessage = "Running bootrec /fixmbr...";
        var output = await _bootRepair.BootrecFixMbrAsync();
        BootOutput += $"\n[bootrec /fixmbr]\n{output}\n";
        AppendLog("bootrec /fixmbr", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task BootrecFixBootAsync()
    {
        IsLoading = true;
        StatusMessage = "Running bootrec /fixboot...";
        var output = await _bootRepair.BootrecFixBootAsync();
        BootOutput += $"\n[bootrec /fixboot]\n{output}\n";
        AppendLog("bootrec /fixboot", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task BootrecScanOsAsync()
    {
        IsLoading = true;
        StatusMessage = "Running bootrec /scanos...";
        var output = await _bootRepair.BootrecScanOsAsync();
        BootOutput += $"\n[bootrec /scanos]\n{output}\n";
        AppendLog("bootrec /scanos", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task BootrecRebuildBcdAsync()
    {
        IsLoading = true;
        StatusMessage = "Running bootrec /rebuildbcd...";
        var output = await _bootRepair.BootrecRebuildBcdAsync();
        BootOutput += $"\n[bootrec /rebuildbcd]\n{output}\n";
        AppendLog("bootrec /rebuildbcd", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task BcdBootDefaultAsync()
    {
        IsLoading = true;
        StatusMessage = "Running bcdboot...";
        var output = await _bootRepair.BcdBootAsync();
        BootOutput += $"\n[bcdboot]\n{output}\n";
        AppendLog("bcdboot", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task BcdBootCustomAsync()
    {
        if (string.IsNullOrWhiteSpace(BcdBootCustomArgs)) return;
        IsLoading = true;
        StatusMessage = "Running bcdboot (custom)...";
        var output = await _bootRepair.BcdBootCustomAsync(BcdBootCustomArgs);
        BootOutput += $"\n[bcdboot {BcdBootCustomArgs}]\n{output}\n";
        AppendLog($"bcdboot {BcdBootCustomArgs}", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task BootSectNt60Async()
    {
        IsLoading = true;
        StatusMessage = "Running bootsect /nt60...";
        var output = await _bootRepair.BootSectNt60Async();
        BootOutput += $"\n[bootsect /nt60]\n{output}\n";
        AppendLog("bootsect /nt60", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task BootSectNt52Async()
    {
        IsLoading = true;
        StatusMessage = "Running bootsect /nt52...";
        var output = await _bootRepair.BootSectNt52Async();
        BootOutput += $"\n[bootsect /nt52]\n{output}\n";
        AppendLog("bootsect /nt52", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ReagentcInfoAsync()
    {
        IsLoading = true;
        StatusMessage = "Running reagentc /info...";
        var output = await _bootRepair.ReagentcInfoAsync();
        BootOutput += $"\n[reagentc /info]\n{output}\n";
        AppendLog("reagentc /info", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ReagentcEnableAsync()
    {
        IsLoading = true;
        StatusMessage = "Running reagentc /enable...";
        var output = await _bootRepair.ReagentcEnableAsync();
        BootOutput += $"\n[reagentc /enable]\n{output}\n";
        AppendLog("reagentc /enable", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ReagentcDisableAsync()
    {
        IsLoading = true;
        StatusMessage = "Running reagentc /disable...";
        var output = await _bootRepair.ReagentcDisableAsync();
        BootOutput += $"\n[reagentc /disable]\n{output}\n";
        AppendLog("reagentc /disable", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ReagentcBootToReAsync()
    {
        IsLoading = true;
        StatusMessage = "Running reagentc /boottoore...";
        var output = await _bootRepair.ReagentcBootToReAsync();
        BootOutput += $"\n[reagentc /boottoore]\n{output}\n";
        AppendLog("reagentc /boottoore", output);
        StatusMessage = "Reboot into WinRE requested.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task MountEspAsync()
    {
        IsLoading = true;
        StatusMessage = $"Mounting ESP to {EspDriveLetter}...";
        var output = await _bootRepair.MountEspAsync(EspDriveLetter);
        BootOutput += $"\n[mountvol {EspDriveLetter} /s]\n{output}\n";
        AppendLog($"mountvol {EspDriveLetter} /s", output);
        StatusMessage = "ESP Mount completed.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task UnmountEspAsync()
    {
        IsLoading = true;
        StatusMessage = $"Unmounting ESP from {EspDriveLetter}...";
        var output = await _bootRepair.UnmountEspAsync(EspDriveLetter);
        BootOutput += $"\n[mountvol {EspDriveLetter} /d]\n{output}\n";
        AppendLog($"mountvol {EspDriveLetter} /d", output);
        StatusMessage = "ESP Unmount completed.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task BcdExportAsync()
    {
        if (string.IsNullOrWhiteSpace(BcdBackupPath)) return;
        IsLoading = true;
        StatusMessage = $"Exporting BCD to {BcdBackupPath}...";
        var output = await _bootRepair.BcdEditExportAsync(BcdBackupPath);
        BootOutput += $"\n[bcdedit /export {BcdBackupPath}]\n{output}\n";
        AppendLog("bcdedit /export", output);
        StatusMessage = "BCD exported.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task BcdImportAsync()
    {
        if (string.IsNullOrWhiteSpace(BcdBackupPath) || !File.Exists(BcdBackupPath)) return;
        IsLoading = true;
        StatusMessage = $"Importing BCD from {BcdBackupPath}...";
        var output = await _bootRepair.BcdEditImportAsync(BcdBackupPath);
        BootOutput += $"\n[bcdedit /import {BcdBackupPath}]\n{output}\n";
        AppendLog("bcdedit /import", output);
        StatusMessage = "BCD imported.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task BcdBootCleanAsync()
    {
        IsLoading = true;
        StatusMessage = "Running bcdboot /c (clean BCD rebuild)...";
        var output = await _bootRepair.BcdBootCleanAsync();
        BootOutput += $"\n[bcdboot C:\\Windows /s S: /f UEFI /c]\n{output}\n";
        AppendLog("bcdboot /c", output);
        StatusMessage = "Clean BCD rebuild completed.";
        IsLoading = false;
    }

    // ════════════════════════════════════════
    //  BCDEdit Tab
    // ════════════════════════════════════════

    [RelayCommand]
    private async Task RefreshBcdEntriesAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Enumerating BCD entries...";
            var (entries, rawOutput) = await _bootRepair.BcdEditEnumAsync();
            AppendLog("bcdedit /enum all", rawOutput);

            BcdEntries.Clear();
            foreach (var e in entries) BcdEntries.Add(e);

            StatusMessage = $"Found {entries.Count} BCD entries.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task BcdEditSetDefaultAsync()
    {
        if (SelectedBcdEntry == null) return;
        IsLoading = true;
        StatusMessage = $"Setting default to {SelectedBcdEntry.Identifier}...";
        var output = await _bootRepair.BcdEditSetDefaultAsync(SelectedBcdEntry.Identifier);
        AppendLog("bcdedit /default", output);
        StatusMessage = "Done.";
        IsLoading = false;
        await RefreshBcdEntriesAsync();
    }

    [RelayCommand]
    private async Task BcdEditDeleteEntryAsync()
    {
        if (SelectedBcdEntry == null) return;
        IsLoading = true;
        StatusMessage = $"Deleting {SelectedBcdEntry.Identifier}...";
        var output = await _bootRepair.BcdEditDeleteEntryAsync(SelectedBcdEntry.Identifier);
        AppendLog("bcdedit /delete", output);
        StatusMessage = "Done.";
        IsLoading = false;
        await RefreshBcdEntriesAsync();
    }

    [RelayCommand]
    private async Task BcdEditCustomAsync()
    {
        if (string.IsNullOrWhiteSpace(BcdEditCustomArgs)) return;
        IsLoading = true;
        StatusMessage = "Running bcdedit (custom)...";
        var output = await _bootRepair.BcdEditCustomAsync(BcdEditCustomArgs);
        BootOutput += $"\n[bcdedit {BcdEditCustomArgs}]\n{output}\n";
        AppendLog($"bcdedit {BcdEditCustomArgs}", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task AutoRepairBootAsync()
    {
        IsLoading = true;
        StatusMessage = "Running full auto boot repair...";
        var output = await _bootRepair.AutoRepairBootAsync();
        BootOutput += output;
        AppendLog("Auto Boot Repair", output);
        StatusMessage = "Auto boot repair complete.";
        IsLoading = false;
    }

    [ObservableProperty] private int _targetDiskNumber = 0;

    [RelayCommand]
    private async Task ClearReadOnlyAsync()
    {
        IsLoading = true;
        StatusMessage = $"Clearing Read-Only attribute on Disk {TargetDiskNumber}...";
        var output = await _diskPart.ClearDiskReadOnlyAsync(TargetDiskNumber);
        DiskPartOutput += $"\n[Clear ReadOnly Disk {TargetDiskNumber}]\n{output}\n";
        AppendLog($"Clear ReadOnly Disk {TargetDiskNumber}", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task SetDiskOnlineAsync()
    {
        IsLoading = true;
        StatusMessage = $"Bringing Disk {TargetDiskNumber} Online...";
        var output = await _diskPart.SetDiskOnlineOfflineAsync(TargetDiskNumber, true);
        DiskPartOutput += $"\n[Online Disk {TargetDiskNumber}]\n{output}\n";
        AppendLog($"Online Disk {TargetDiskNumber}", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task SetDiskOfflineAsync()
    {
        IsLoading = true;
        StatusMessage = $"Taking Disk {TargetDiskNumber} Offline...";
        var output = await _diskPart.SetDiskOnlineOfflineAsync(TargetDiskNumber, false);
        DiskPartOutput += $"\n[Offline Disk {TargetDiskNumber}]\n{output}\n";
        AppendLog($"Offline Disk {TargetDiskNumber}", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task RescanDisksAsync()
    {
        IsLoading = true;
        StatusMessage = "Rescanning storage devices...";
        var output = await _diskPart.RescanDisksAsync();
        DiskPartOutput += $"\n[Rescan]\n{output}\n";
        AppendLog("Rescan", output);
        StatusMessage = "Rescan complete.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task GetDiskUniqueIdAsync()
    {
        IsLoading = true;
        StatusMessage = $"Querying UniqueID on Disk {TargetDiskNumber}...";
        var output = await _diskPart.GetUniqueIdAsync(TargetDiskNumber);
        DiskPartOutput += $"\n[UniqueID Disk {TargetDiskNumber}]\n{output}\n";
        AppendLog($"UniqueID Disk {TargetDiskNumber}", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task SetBcdTimeoutPresetAsync(string secondsStr)
    {
        if (!int.TryParse(secondsStr, out var sec)) return;
        IsLoading = true;
        StatusMessage = $"Setting BCD timeout to {sec} seconds...";
        var output = await _bootRepair.BcdEditSetTimeoutAsync(sec);
        BootOutput += $"\n[bcdedit /timeout {sec}]\n{output}\n";
        AppendLog($"bcdedit /timeout {sec}", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task SetBcdSafeModePresetAsync(string mode)
    {
        IsLoading = true;
        StatusMessage = $"Configuring Safe Mode ({mode})...";
        var output = await _bootRepair.BcdEditSetSafeModeAsync(mode);
        BootOutput += $"\n[SafeMode: {mode}]\n{output}\n";
        AppendLog($"SafeMode {mode}", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ToggleHypervisorAsync(string enableStr)
    {
        var enable = bool.TryParse(enableStr, out var b) && b;
        IsLoading = true;
        StatusMessage = $"Configuring Hypervisor launch type ({(enable ? "Auto" : "Off")})...";
        var output = await _bootRepair.BcdEditSetHypervisorAsync(enable);
        BootOutput += $"\n[Hypervisor: {enable}]\n{output}\n";
        AppendLog($"Hypervisor {enable}", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ToggleTestSigningAsync(string enableStr)
    {
        var enable = bool.TryParse(enableStr, out var b) && b;
        IsLoading = true;
        StatusMessage = $"Configuring Test Signing ({(enable ? "On" : "Off")})...";
        var output = await _bootRepair.BcdEditSetTestSigningAsync(enable);
        BootOutput += $"\n[TestSigning: {enable}]\n{output}\n";
        AppendLog($"TestSigning {enable}", output);
        StatusMessage = "Done.";
        IsLoading = false;
    }

    [RelayCommand]
    private void ClearBootOutput()
    {
        BootOutput = "";
    }

    [RelayCommand]
    private void ClearDiskPartOutput()
    {
        DiskPartOutput = "";
    }

    [RelayCommand]
    private void ClearFullLog()
    {
        FullLog = "";
    }
}
