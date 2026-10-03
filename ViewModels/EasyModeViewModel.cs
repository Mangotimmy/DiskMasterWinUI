using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;
using Windows.ApplicationModel.DataTransfer;

namespace DiskMasterWinUI.ViewModels;

public partial class EasyModeViewModel : ObservableObject
{
    private readonly NativeStorageService _storageService = new();
    private readonly DiskPartService _diskPart = new();
    private readonly BootRepairService _bootRepair = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _outputLog = "";
    [ObservableProperty] private DiskInfo? _selectedDisk;
    [ObservableProperty] private VolumeInfo? _selectedVolume;
    [ObservableProperty] private PartitionInfo? _selectedPartition;
    [ObservableProperty] private PartitionBlock? _selectedBlock;

    // Interactive Mouse Resize
    [ObservableProperty] private double _resizeSliderValue = 100.0;
    [ObservableProperty] private string _resizeTargetSizeDisplay = "";
    [ObservableProperty] private string _resizeDeltaDisplay = "";

    // Live Suggested Command
    [ObservableProperty] private string _suggestedCommandText = "select disk 0\r\nlist volume";

    // Format options
    [ObservableProperty] private string _formatFileSystem = "NTFS";
    [ObservableProperty] private string _formatLabel = "";
    [ObservableProperty] private bool _quickFormat = true;
    [ObservableProperty] private string _selectedClusterSize = "Default";
    public ObservableCollection<string> ClusterSizes { get; } = new() { "Default", "4096", "8192", "16384", "32768", "65536" };

    // Assign letter
    [ObservableProperty] private string _assignLetter = "";

    // Partition size
    [ObservableProperty] private string _partitionSizeMB = "";

    // Boot repair
    [ObservableProperty] private string _windowsDirectory = @"C:\Windows";

    public ObservableCollection<DiskInfo> Disks { get; } = new();
    public ObservableCollection<VolumeInfo> Volumes { get; } = new();
    public ObservableCollection<PartitionInfo> Partitions { get; } = new();

    public ObservableCollection<string> FileSystemOptions { get; } = new() { "NTFS", "FAT32", "exFAT" };
    public ObservableCollection<string> AvailableLetters { get; } = new();

    public EasyModeViewModel()
    {
        IsAdmin = AdminHelper.IsRunningAsAdmin();
        RefreshAvailableLetters();
    }

    private void AppendLog(string message)
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            if (OutputLog.Length > 200000)
            {
                OutputLog = OutputLog.Substring(OutputLog.Length - 100000);
            }
            OutputLog += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        });
    }

    private void RefreshAvailableLetters(string? currentLetter = null)
    {
        AvailableLetters.Clear();
        var usedDrives = System.IO.DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        if (!string.IsNullOrEmpty(currentLetter))
        {
            usedDrives.Remove(char.ToUpperInvariant(currentLetter[0]));
        }

        for (char c = 'A'; c <= 'Z'; c++)
        {
            if (!usedDrives.Contains(c))
                AvailableLetters.Add(c.ToString());
        }
    }

    [RelayCommand]
    private async Task OneClickHealthCheckAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在進行一鍵磁碟健康度與 S.M.A.R.T. 體檢...";
            MainWindow.CurrentInstance?.CompanionSay("正在為您進行全磁碟健康體檢與 S.M.A.R.T. 分析～");

            await RefreshDisksAsync();
            int targetDisk = SelectedDisk?.Number ?? 0;
            var smartService = new SmartReaderService();
            var temp = await smartService.GetDiskTemperatureCelsiusAsync(targetDisk);

            var tempText = temp.HasValue ? $"{temp.Value}°C" : "正常";
            StatusMessage = $"一鍵體檢完成！磁碟 {targetDisk} 狀態良好，工作溫度: {tempText}";
            MainWindow.CurrentInstance?.CompanionSay($"一鍵體檢完成！磁碟 {targetDisk} 健康良好，即時溫度 {tempText}。");
        }
        catch (Exception ex)
        {
            StatusMessage = $"體檢出錯: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshDisksAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            IsLoading = true;
            StatusMessage = "Refreshing disks...";

            // 1. High-Performance Native Query (~100-200ms)
            var (disks, volumes, nativeLog) = await _storageService.GetDisksAndVolumesAsync();

            if (disks.Count > 0)
            {
                Disks.Clear();
                foreach (var d in disks) Disks.Add(d);

                Volumes.Clear();
                foreach (var v in volumes) Volumes.Add(v);

                AppendLog(nativeLog);

                if (SelectedDisk == null && Disks.Count > 0)
                {
                    SelectedDisk = Disks[0];
                }
                else if (SelectedDisk != null)
                {
                    // Refresh visual blocks for currently selected disk
                    SelectedDisk.RefreshVisualBlocks(SelectedDisk.Partitions, Volumes);
                }

                sw.Stop();
                StatusMessage = $"Found {disks.Count} disk(s), {volumes.Count} volume(s) ({sw.ElapsedMilliseconds} ms)";
                return;
            }

            // 2. Fallback to DiskPart single-session batch query if native query had 0 disks
            AppendLog("[Info] Native query returned 0 disks, falling back to DiskPart batch query...");
            var (dpDisks, dpVolumes, dpOutput) = await _diskPart.ListDisksAndVolumesAsync();
            AppendLog("=== DiskPart Output ===\n" + dpOutput);

            Disks.Clear();
            foreach (var d in dpDisks) Disks.Add(d);

            Volumes.Clear();
            foreach (var v in dpVolumes) Volumes.Add(v);

            if (SelectedDisk == null && Disks.Count > 0)
            {
                SelectedDisk = Disks[0];
            }

            sw.Stop();
            StatusMessage = $"Found {dpDisks.Count} disk(s), {dpVolumes.Count} volume(s) ({sw.ElapsedMilliseconds} ms)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedDiskChanged(DiskInfo? value)
    {
        if (value != null)
        {
            if (value.Partitions.Count > 0)
            {
                Partitions.Clear();
                foreach (var p in value.Partitions) Partitions.Add(p);
                value.RefreshVisualBlocks(Partitions, Volumes);
                SelectDefaultBlock(value);
            }
            else
            {
                _ = LoadPartitionsAsync(value.Number);
            }
        }
        else
        {
            Partitions.Clear();
            SelectedBlock = null;
        }
        UpdateSuggestedCommand();
    }

    private async Task LoadPartitionsAsync(int diskNumber)
    {
        try
        {
            var (partitions, rawOutput) = await _diskPart.ListPartitionsAsync(diskNumber);
            AppendLog($"=== Partitions for Disk {diskNumber} ===\n" + rawOutput);

            Partitions.Clear();
            foreach (var p in partitions) Partitions.Add(p);

            SelectedDisk?.RefreshVisualBlocks(Partitions, Volumes);
            if (SelectedDisk != null)
            {
                SelectDefaultBlock(SelectedDisk);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR loading partitions: {ex.Message}");
        }
    }

    public bool IsResizeSliderEnabled => SelectedBlock != null && SelectedBlock.IsResizable && !SelectedBlock.IsUnallocated;

    public string SelectedBlockBadge => SelectedBlock == null
        ? "No Partition Selected"
        : SelectedBlock.IsUnallocated
            ? $"Unallocated Space ({SelectedBlock.SizeDisplay})"
            : $"Partition {SelectedBlock.PartitionNumber} [{(string.IsNullOrEmpty(SelectedBlock.DriveLetter) ? "No Letter" : SelectedBlock.DriveLetter + ":")}] — {SelectedBlock.SizeDisplay} — {(string.IsNullOrEmpty(SelectedBlock.FileSystem) ? SelectedBlock.TypeDescription : SelectedBlock.FileSystem)}";

    private void SelectDefaultBlock(DiskInfo disk)
    {
        if (disk.VisualBlocks.Count == 0) return;

        var defaultBlock = disk.VisualBlocks.FirstOrDefault(b => b.BlockType == PartitionBlockType.WindowsBoot)
            ?? disk.VisualBlocks.FirstOrDefault(b => b.BlockType == PartitionBlockType.PrimaryData && !b.IsUnallocated)
            ?? disk.VisualBlocks.FirstOrDefault(b => !b.IsUnallocated && b.IsResizable)
            ?? disk.VisualBlocks.FirstOrDefault();

        if (defaultBlock != null)
        {
            SelectBlock(defaultBlock);
        }
    }

    partial void OnSelectedBlockChanged(PartitionBlock? value)
    {
        OnPropertyChanged(nameof(SelectedBlockBadge));
        OnPropertyChanged(nameof(IsResizeSliderEnabled));
        if (value == null || SelectedDisk == null)
        {
            FormatLabel = "";
            RefreshAvailableLetters();
            AssignLetter = "";
            return;
        }

        foreach (var b in SelectedDisk.VisualBlocks)
        {
            b.IsSelected = (b == value);
        }

        // Sync with SelectedPartition
        if (value.PartitionNumber > 0)
        {
            SelectedPartition = Partitions.FirstOrDefault(p => p.Number == value.PartitionNumber);
        }
        else
        {
            SelectedPartition = null;
        }

        // Sync with SelectedVolume
        if (value.VolumeNumber >= 0)
        {
            SelectedVolume = Volumes.FirstOrDefault(v => v.Number == value.VolumeNumber);
        }
        else if (!string.IsNullOrEmpty(value.DriveLetter))
        {
            SelectedVolume = Volumes.FirstOrDefault(v => v.Letter.Equals(value.DriveLetter, StringComparison.OrdinalIgnoreCase));
        }

        // Sync Label, Letter and FileSystem with UI
        if (!value.IsUnallocated)
        {
            FormatLabel = value.Label ?? "";
            RefreshAvailableLetters(value.DriveLetter);
            AssignLetter = !string.IsNullOrEmpty(value.DriveLetter) ? value.DriveLetter : "";

            if (!string.IsNullOrEmpty(value.FileSystem))
            {
                var matched = FileSystemOptions.FirstOrDefault(f => f.Equals(value.FileSystem, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    FormatFileSystem = matched;
                }
            }
        }
        else
        {
            FormatLabel = "";
            RefreshAvailableLetters();
            AssignLetter = "";
        }

        ResizeSliderValue = 100.0;
        UpdateResizeDisplays();
        UpdateSuggestedCommand();
    }

    [RelayCommand]
    public void SelectBlock(PartitionBlock? block)
    {
        SelectedBlock = block;
    }

    partial void OnResizeSliderValueChanged(double value)
    {
        UpdateResizeDisplays();
        UpdateSuggestedCommand();
    }

    partial void OnFormatFileSystemChanged(string value) => UpdateSuggestedCommand();
    partial void OnFormatLabelChanged(string value) => UpdateSuggestedCommand();
    partial void OnQuickFormatChanged(bool value) => UpdateSuggestedCommand();
    partial void OnAssignLetterChanged(string value) => UpdateSuggestedCommand();

    private void UpdateResizeDisplays()
    {
        if (SelectedBlock == null)
        {
            ResizeTargetSizeDisplay = "N/A";
            ResizeDeltaDisplay = "";
            return;
        }

        if (SelectedBlock.IsUnallocated)
        {
            ResizeTargetSizeDisplay = SelectedBlock.SizeDisplay;
            ResizeDeltaDisplay = "未配置空間 (可直接建立分割區)";
            return;
        }

        if (!SelectedBlock.IsResizable || SelectedBlock.BlockType == PartitionBlockType.EfiSystem || SelectedBlock.BlockType == PartitionBlockType.Recovery)
        {
            ResizeTargetSizeDisplay = SelectedBlock.SizeDisplay;
            ResizeDeltaDisplay = "系統保護分割區 (不可縮減)";
            return;
        }

        var totalMb = ParseSizeToMb(SelectedBlock.SizeDisplay);
        if (totalMb <= 0)
        {
            ResizeTargetSizeDisplay = SelectedBlock.SizeDisplay;
            ResizeDeltaDisplay = "";
            return;
        }

        var targetMb = Math.Round(totalMb * (ResizeSliderValue / 100.0));
        var deltaMb = totalMb - targetMb;

        ResizeTargetSizeDisplay = FormatMb(targetMb);
        ResizeDeltaDisplay = deltaMb > 0
            ? $"Shrink by {FormatMb(deltaMb)}"
            : "No change (100% capacity)";
    }

    private void UpdateSuggestedCommand()
    {
        if (SelectedDisk == null)
        {
            SuggestedCommandText = "list disk";
            return;
        }

        var diskNum = SelectedDisk.Number;
        var sb = new StringBuilder();
        sb.AppendLine($"select disk {diskNum}");

        if (SelectedBlock != null)
        {
            if (SelectedBlock.IsUnallocated)
            {
                sb.AppendLine("create partition primary");
                sb.AppendLine("format fs=ntfs quick");
                sb.AppendLine("assign");
            }
            else
            {
                if (SelectedBlock.PartitionNumber > 0)
                {
                    sb.AppendLine($"select partition {SelectedBlock.PartitionNumber}");
                }

                if (SelectedVolume != null)
                {
                    sb.AppendLine($"select volume {SelectedVolume.Number}");
                }

                // If slider is less than 100%, suggest shrink
                if (ResizeSliderValue < 99.0)
                {
                    var totalMb = ParseSizeToMb(SelectedBlock.SizeDisplay);
                    var targetMb = Math.Round(totalMb * (ResizeSliderValue / 100.0));
                    var shrinkMb = Math.Max(10, (long)(totalMb - targetMb));
                    sb.AppendLine($"shrink desired={shrinkMb}");
                }
            }
        }

        SuggestedCommandText = sb.ToString().TrimEnd();
    }

    [RelayCommand]
    private async Task ExecuteSuggestedCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(SuggestedCommandText)) return;
        try
        {
            IsLoading = true;
            StatusMessage = "Executing suggested command in DiskPart...";
            var output = await _diskPart.RunCustomScriptAsync(SuggestedCommandText);
            AppendLog($"=== Execute Suggested Script ===\n{output}");
            StatusMessage = "Script executed. Refreshing...";
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CopySuggestedCommand()
    {
        var package = new DataPackage();
        package.SetText(SuggestedCommandText);
        Clipboard.SetContent(package);
        StatusMessage = "Suggested command copied to clipboard!";
    }

    [RelayCommand]
    private void ApplySizePreset(string preset)
    {
        if (string.Equals(preset, "Max", StringComparison.OrdinalIgnoreCase))
        {
            PartitionSizeMB = "";
            ResizeSliderValue = 100.0;
            return;
        }

        if (string.Equals(preset, "50%", StringComparison.OrdinalIgnoreCase))
        {
            ResizeSliderValue = 50.0;
            return;
        }

        if (int.TryParse(preset, out var gb))
        {
            PartitionSizeMB = (gb * 1024).ToString();

            if (SelectedBlock != null && !SelectedBlock.IsUnallocated)
            {
                var totalMb = ParseSizeToMb(SelectedBlock.SizeDisplay);
                if (totalMb > 0)
                {
                    var targetMb = gb * 1024.0;
                    var ratio = Math.Clamp((targetMb / totalMb) * 100.0, 10.0, 100.0);
                    ResizeSliderValue = ratio;
                }
            }
        }
    }

    [RelayCommand]
    private void ApplyLabelPreset(string label)
    {
        FormatLabel = label;
    }

    [RelayCommand]
    private void SelectDriveLetter(string letter)
    {
        AssignLetter = letter;
    }

    [RelayCommand]
    private async Task CleanDiskAsync()
    {
        if (SelectedDisk == null) return;

        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "清除磁碟 (Clean Disk)",
            $"即將抹除磁碟 {SelectedDisk.Number} 的所有分割區與資料，此操作無法復原！",
            SelectedDisk.DisplayName);
        if (!confirmed) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Cleaning Disk {SelectedDisk.Number}...";
            var output = await _diskPart.CleanDiskAsync(SelectedDisk.Number);
            AppendLog($"=== Clean Disk {SelectedDisk.Number} ===\n" + output);
            StatusMessage = "Disk cleaned. Refreshing...";
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task InitializeDiskGptAsync()
    {
        if (SelectedDisk == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Converting Disk {SelectedDisk.Number} to GPT...";
            var output = await _diskPart.InitializeDiskAsync(SelectedDisk.Number, true);
            AppendLog($"=== Convert to GPT Disk {SelectedDisk.Number} ===\n" + output);
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task InitializeDiskMbrAsync()
    {
        if (SelectedDisk == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Converting Disk {SelectedDisk.Number} to MBR...";
            var output = await _diskPart.InitializeDiskAsync(SelectedDisk.Number, false);
            AppendLog($"=== Convert to MBR Disk {SelectedDisk.Number} ===\n" + output);
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task CreatePartitionAsync()
    {
        if (SelectedDisk == null) return;
        try
        {
            IsLoading = true;
            int? sizeMB = null;
            if (int.TryParse(PartitionSizeMB, out var s) && s > 0) sizeMB = s;

            StatusMessage = $"Creating partition on Disk {SelectedDisk.Number}...";
            var output = await _diskPart.CreatePrimaryPartitionAsync(SelectedDisk.Number, sizeMB);
            AppendLog($"=== Create Partition on Disk {SelectedDisk.Number} ===\n" + output);
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task FormatVolumeAsync()
    {
        if (SelectedVolume == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Formatting Volume {SelectedVolume.Number} as {FormatFileSystem}...";
            string output;
            if (SelectedClusterSize != "Default" && int.TryParse(SelectedClusterSize, out var clusterSize))
            {
                output = await _diskPart.FormatWithClusterSizeAsync(SelectedVolume.Number, FormatFileSystem, clusterSize, FormatLabel, QuickFormat);
            }
            else
            {
                output = await _diskPart.FormatVolumeAsync(SelectedVolume.Number, FormatFileSystem, FormatLabel, QuickFormat);
            }
            AppendLog($"=== Format Volume {SelectedVolume.Number} ===\n" + output);
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task CreateStandardUefiLayoutAsync()
    {
        if (SelectedDisk == null) return;
        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "標準 UEFI 4分割區佈局 (Standard UEFI Layout)",
            $"即將清空磁碟 {SelectedDisk.Number} 並自動建立：\n1. EFI 系統開機區 (260 MB, FAT32)\n2. MSR 保留區 (16 MB)\n3. Windows 主系統區 (NTFS, 剩餘空間)\n4. WinRE 修復磁區 (1000 MB, 安全屬性)\n\n此操作將抹除磁碟上所有現存資料！",
            $"Disk {SelectedDisk.Number}");
        if (!confirmed) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Applying standard UEFI layout on Disk {SelectedDisk.Number}...";
            var output = await _diskPart.CreateStandardUefiLayoutAsync(SelectedDisk.Number);
            AppendLog($"=== Standard UEFI Layout on Disk {SelectedDisk.Number} ===\n" + output);
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task CreateRecoveryPartitionAsync()
    {
        if (SelectedDisk == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Creating WinRE recovery partition on Disk {SelectedDisk.Number}...";
            var output = await _diskPart.CreateRecoveryPartitionAsync(SelectedDisk.Number, 1000);
            AppendLog($"=== Create WinRE Recovery Partition ===\n" + output);
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ExtendFilesystemAsync()
    {
        if (SelectedVolume == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Extending filesystem on Volume {SelectedVolume.Number}...";
            var output = await _diskPart.ExtendFilesystemAsync(SelectedVolume.Number);
            AppendLog($"=== Extend Filesystem on Volume {SelectedVolume.Number} ===\n" + output);
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ShrinkQueryMaxAsync()
    {
        if (SelectedVolume == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Querying max shrink for Volume {SelectedVolume.Number}...";
            var output = await _diskPart.ShrinkQueryMaxAsync(SelectedVolume.Number);
            AppendLog($"=== Query Max Shrink for Volume {SelectedVolume.Number} ===\n" + output);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetVolumeLabel(string lpRootPathName, string? lpVolumeName);

    [RelayCommand]
    private async Task SetVolumeLabelAsync()
    {
        if (SelectedBlock == null || SelectedBlock.IsUnallocated) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"正在變更分割區標籤為 '{FormatLabel}'...";
            bool success = false;

            string letter = SelectedBlock.DriveLetter;
            if (string.IsNullOrEmpty(letter) && SelectedVolume != null)
            {
                letter = SelectedVolume.Letter;
            }

            if (!string.IsNullOrEmpty(letter))
            {
                string rootPath = $"{letter}:\\";
                success = SetVolumeLabel(rootPath, FormatLabel);
                if (!success)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c label {letter}: {FormatLabel}",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var p = Process.Start(psi);
                    if (p != null) await p.WaitForExitAsync();
                    success = true;
                }
            }
            else if (SelectedPartition != null && SelectedDisk != null)
            {
                var script = $"Get-Partition -DiskNumber {SelectedDisk.Number} -PartitionNumber {SelectedPartition.Number} | Get-Volume | Set-Volume -NewFileSystemLabel '{FormatLabel}'";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                if (p != null) await p.WaitForExitAsync();
                success = true;
            }

            SelectedBlock.Label = FormatLabel;
            if (SelectedVolume != null) SelectedVolume.Label = FormatLabel;
            AppendLog($"[Info] 成功更新標籤為: {FormatLabel}");
            StatusMessage = $"標籤已更新為 '{FormatLabel}'";
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"變更標籤失敗: {ex.Message}";
            AppendLog($"[Error] 變更標籤失敗: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AssignLetterAsync()
    {
        if (string.IsNullOrEmpty(AssignLetter)) return;
        try
        {
            IsLoading = true;
            string output = "";
            if (SelectedVolume != null)
            {
                StatusMessage = $"Assigning letter {AssignLetter}: to Volume {SelectedVolume.Number}...";
                output = await _diskPart.AssignLetterAsync(SelectedVolume.Number, AssignLetter[0]);
            }
            else if (SelectedDisk != null && SelectedPartition != null)
            {
                StatusMessage = $"Assigning letter {AssignLetter}: to Disk {SelectedDisk.Number} Partition {SelectedPartition.Number}...";
                output = await _diskPart.RunCustomScriptAsync($"select disk {SelectedDisk.Number}\r\nselect partition {SelectedPartition.Number}\r\nassign letter={AssignLetter[0]}");
            }
            else
            {
                return;
            }

            AppendLog($"=== Assign Letter ===\n" + output);
            if (SelectedBlock != null)
            {
                SelectedBlock.DriveLetter = AssignLetter;
            }
            RefreshAvailableLetters(AssignLetter);
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task RemoveLetterAsync()
    {
        try
        {
            IsLoading = true;
            string output = "";
            if (SelectedVolume != null)
            {
                StatusMessage = $"Removing drive letter from Volume {SelectedVolume.Number}...";
                output = await _diskPart.RemoveLetterAsync(SelectedVolume.Number);
            }
            else if (SelectedDisk != null && SelectedPartition != null)
            {
                StatusMessage = $"Removing drive letter from Disk {SelectedDisk.Number} Partition {SelectedPartition.Number}...";
                output = await _diskPart.RunCustomScriptAsync($"select disk {SelectedDisk.Number}\r\nselect partition {SelectedPartition.Number}\r\nremove");
            }
            else
            {
                return;
            }

            AppendLog($"=== Remove Letter ===\n" + output);
            if (SelectedBlock != null)
            {
                SelectedBlock.DriveLetter = "";
            }
            AssignLetter = "";
            RefreshAvailableLetters();
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ExtendVolumeAsync()
    {
        if (SelectedVolume == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Extending Volume {SelectedVolume.Number}...";
            var output = await _diskPart.ExtendVolumeAsync(SelectedVolume.Number);
            AppendLog($"=== Extend Volume ===\n" + output);
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task DetailDiskAsync()
    {
        if (SelectedDisk == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Getting details for Disk {SelectedDisk.Number}...";
            var output = await _diskPart.DetailDiskAsync(SelectedDisk.Number);
            AppendLog($"=== Detail Disk {SelectedDisk.Number} ===\n" + output);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task DetailVolumeAsync()
    {
        if (SelectedVolume == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Getting details for Volume {SelectedVolume.Number}...";
            var output = await _diskPart.DetailVolumeAsync(SelectedVolume.Number);
            AppendLog($"=== Detail Volume {SelectedVolume.Number} ===\n" + output);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task SetVolumeReadOnlyAsync()
    {
        if (SelectedVolume == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Setting ReadOnly on Volume {SelectedVolume.Number}...";
            var output = await _diskPart.SetVolumeAttributesAsync(SelectedVolume.Number, "readonly", true);
            AppendLog($"=== Set Volume {SelectedVolume.Number} ReadOnly ===\n" + output);
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ClearVolumeReadOnlyAsync()
    {
        if (SelectedVolume == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Clearing ReadOnly on Volume {SelectedVolume.Number}...";
            var output = await _diskPart.SetVolumeAttributesAsync(SelectedVolume.Number, "readonly", false);
            AppendLog($"=== Clear Volume {SelectedVolume.Number} ReadOnly ===\n" + output);
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task DeletePartitionAsync()
    {
        if (SelectedDisk == null) return;
        var partNum = SelectedBlock?.PartitionNumber ?? SelectedPartition?.Number ?? 0;
        if (partNum <= 0) return;

        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "刪除分割區 (Delete Partition)",
            $"即將刪除磁碟 {SelectedDisk.Number} 上的第 {partNum} 號分割區，該分區上的所有資料將永久遺失！",
            $"Disk {SelectedDisk.Number} Partition {partNum}");
        if (!confirmed) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Deleting Partition {partNum} on Disk {SelectedDisk.Number}...";
            var output = await _diskPart.DeletePartitionAsync(SelectedDisk.Number, partNum, true);
            AppendLog($"=== Delete Partition ===\n" + output);
            await RefreshDisksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task BootRepairAutoAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Running auto boot repair...";
            var output = await _bootRepair.AutoRepairBootAsync(WindowsDirectory);
            AppendLog(output);
            StatusMessage = "Boot repair complete. Check output log.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        AdminHelper.RestartAsAdmin();
    }

    [RelayCommand]
    private void ClearLog()
    {
        OutputLog = "";
    }

    private static double ParseSizeToMb(string sizeStr)
    {
        if (string.IsNullOrWhiteSpace(sizeStr)) return 0;
        var match = Regex.Match(sizeStr.Trim(), @"^([\d\.]+)\s*([A-Za-z]+)?");
        if (!match.Success) return 0;
        if (!double.TryParse(match.Groups[1].Value, out var val)) return 0;
        var unit = match.Groups[2].Value.ToUpperInvariant();
        return unit switch
        {
            "TB" => val * 1024.0 * 1024.0,
            "GB" => val * 1024.0,
            "MB" => val,
            "KB" => val / 1024.0,
            _ => val
        };
    }

    private static string FormatMb(double mb)
    {
        if (mb >= 1024.0 * 1024.0)
            return $"{mb / (1024.0 * 1024.0):F1} TB";
        if (mb >= 1024.0)
            return $"{mb / 1024.0:F1} GB";
        return $"{mb:F0} MB";
    }
}
