using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

public partial class NtfsPermissionsViewModel : ObservableObject
{
    private readonly NtfsPermissionService _ntfsService = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _targetPath = @"C:\";
    [ObservableProperty] private string _selectedPrincipal = "Administrators";
    [ObservableProperty] private string _selectedAccess = "F";
    [ObservableProperty] private string _selectedInheritance = "(OI)(CI)";
    [ObservableProperty] private bool _isRecursive = true;
    [ObservableProperty] private bool _continueOnError = true;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private string _statusMessage = "Ready to manage NTFS permissions";
    [ObservableProperty] private string _terminalOutput = "";
    [ObservableProperty] private NtfsFeatureSummary _currentNtfsSummary = new();
    [ObservableProperty] private bool _isSummaryLoading;
    [ObservableProperty] private string _aclBackupPath = @"C:\AclBackup.txt";

    public ObservableCollection<string> PrincipalOptions { get; } = new()
    {
        "Administrators",
        "SYSTEM",
        "Everyone",
        "Authenticated Users",
        "Users"
    };

    public ObservableCollection<string> AccessOptions { get; } = new()
    {
        "F",  // Full Control
        "M",  // Modify
        "RX", // Read & Execute
        "R",  // Read
        "W"   // Write
    };

    public ObservableCollection<string> InheritanceOptions { get; } = new()
    {
        "(OI)(CI)", // Subfolders and files
        "(CI)",     // Subfolders only
        "(OI)",     // Files only
        ""          // This folder only
    };

    public string GeneratedPreviewCommand
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TargetPath)) return "";
            var clean = TargetPath.TrimEnd('\\');
            var rec = IsRecursive ? " /T" : "";
            var err = ContinueOnError ? " /C" : "";
            return $"icacls \"{clean}\" /grant:r \"{SelectedPrincipal}\":{SelectedInheritance}({SelectedAccess}){rec}{err}";
        }
    }

    public NtfsPermissionsViewModel()
    {
        IsAdmin = AdminHelper.IsRunningAsAdmin();
        AppendOutput("NTFS Permissions & Ownership Engine Initialized.");
        if (!IsAdmin)
        {
            AppendOutput("[WARNING] Modifying system ACLs and taking ownership requires Administrator privileges.");
        }
    }

    partial void OnTargetPathChanged(string value) => OnPropertyChanged(nameof(GeneratedPreviewCommand));
    partial void OnSelectedPrincipalChanged(string value) => OnPropertyChanged(nameof(GeneratedPreviewCommand));
    partial void OnSelectedAccessChanged(string value) => OnPropertyChanged(nameof(GeneratedPreviewCommand));
    partial void OnSelectedInheritanceChanged(string value) => OnPropertyChanged(nameof(GeneratedPreviewCommand));
    partial void OnIsRecursiveChanged(bool value) => OnPropertyChanged(nameof(GeneratedPreviewCommand));
    partial void OnContinueOnErrorChanged(bool value) => OnPropertyChanged(nameof(GeneratedPreviewCommand));

    private void AppendOutput(string text)
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            if (TerminalOutput.Length > 200000)
            {
                TerminalOutput = TerminalOutput.Substring(TerminalOutput.Length - 100000);
            }
            TerminalOutput += $"{text}\n";
        });
    }

    [RelayCommand]
    private async Task ApplyTakeOwnershipAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(TargetPath)) return;
        try
        {
            IsRunning = true;
            StatusMessage = "Applying: Take Ownership & Unlock...";
            _cts = new CancellationTokenSource();
            await _ntfsService.ApplyPresetAsync(TargetPath, NtfsPresetType.TakeOwnershipAndUnlock, AppendOutput, _cts.Token);
            StatusMessage = "Take Ownership complete.";
            AudioFeedbackService.PlaySuccess();
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task ApplyGrantEveryoneAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(TargetPath)) return;
        try
        {
            IsRunning = true;
            StatusMessage = "Applying: Grant Everyone Full Control...";
            _cts = new CancellationTokenSource();
            await _ntfsService.ApplyPresetAsync(TargetPath, NtfsPresetType.GrantEveryoneFullControl, AppendOutput, _cts.Token);
            StatusMessage = "Grant Everyone complete.";
            AudioFeedbackService.PlaySuccess();
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task ApplyResetDefaultsAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(TargetPath)) return;
        try
        {
            IsRunning = true;
            StatusMessage = "Applying: Reset to Default Inheritance...";
            _cts = new CancellationTokenSource();
            await _ntfsService.ApplyPresetAsync(TargetPath, NtfsPresetType.ResetToDefaultInheritance, AppendOutput, _cts.Token);
            StatusMessage = "Reset permissions complete.";
            AudioFeedbackService.PlaySuccess();
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task ApplyStrictAdminOnlyAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(TargetPath)) return;
        try
        {
            IsRunning = true;
            StatusMessage = "Applying: Strict Administrators Only...";
            _cts = new CancellationTokenSource();
            await _ntfsService.ApplyPresetAsync(TargetPath, NtfsPresetType.StrictAdministratorsOnly, AppendOutput, _cts.Token);
            StatusMessage = "Strict Admin Only complete.";
            AudioFeedbackService.PlaySuccess();
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task RunCustomBatchApplyAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(TargetPath)) return;
        try
        {
            IsRunning = true;
            StatusMessage = "Running Custom icacls Batch Apply...";
            _cts = new CancellationTokenSource();
            await _ntfsService.GrantPermissionAsync(
                TargetPath,
                SelectedPrincipal,
                SelectedAccess,
                SelectedInheritance,
                IsRecursive,
                ContinueOnError,
                AppendOutput,
                _cts.Token);
            StatusMessage = "Custom batch apply complete.";
            AudioFeedbackService.PlaySuccess();
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    public async Task InspectNtfsFeaturesAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath)) return;
        try
        {
            IsSummaryLoading = true;
            StatusMessage = "Analyzing NTFS features and ACLs...";
            CurrentNtfsSummary = await _ntfsService.InspectNtfsFeaturesAsync(TargetPath);
            StatusMessage = $"NTFS 深度探測完成：{CurrentNtfsSummary.AclEntries.Count} 項 ACL 規則，{CurrentNtfsSummary.AlternateStreams.Count} 個替代資料流 (ADS)。";
            AppendOutput($"[NTFS INSPECT] Target: {TargetPath}");
            AppendOutput($"  Compressed: {CurrentNtfsSummary.IsCompressed}, Encrypted: {CurrentNtfsSummary.IsEncrypted}, Sparse: {CurrentNtfsSummary.IsSparse}, ReparsePoint: {CurrentNtfsSummary.IsReparsePoint}");
            AppendOutput($"  Hardlinks: {CurrentNtfsSummary.HardlinkCount}");
            AppendOutput($"  ACE entries: {CurrentNtfsSummary.AclEntries.Count}");
            AppendOutput($"  Alternate Streams: {CurrentNtfsSummary.AlternateStreams.Count}");
            AudioFeedbackService.PlaySuccess();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Inspect failed: {ex.Message}";
            AppendOutput($"[ERROR] {ex.Message}");
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsSummaryLoading = false;
        }
    }

    [RelayCommand]
    public async Task RemoveDenyAclsAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(TargetPath)) return;
        try
        {
            IsRunning = true;
            StatusMessage = "正在清除 DENY 拒絕存取規則...";
            var (ok, msg) = await _ntfsService.RemoveDenyAclsAsync(TargetPath);
            AppendOutput(msg);
            StatusMessage = ok ? "DENY 拒絕規則已清除。" : "清除 DENY 規則失敗。";
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await InspectNtfsFeaturesAsync();
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    public async Task UnblockZoneIdentifierAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(TargetPath)) return;
        try
        {
            IsRunning = true;
            StatusMessage = "正在解除網路下載鎖定 (Zone.Identifier)...";
            var (ok, msg) = await _ntfsService.UnblockZoneIdentifierAsync(TargetPath);
            AppendOutput(msg);
            StatusMessage = ok ? "已解除網路下載鎖定。" : "解除鎖定失敗。";
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await InspectNtfsFeaturesAsync();
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    public async Task BackupAclsAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(TargetPath)) return;
        try
        {
            IsRunning = true;
            var backupFile = string.IsNullOrWhiteSpace(AclBackupPath)
                ? Path.Combine(Path.GetDirectoryName(TargetPath) ?? @"C:\", "AclBackup.txt")
                : AclBackupPath;
            StatusMessage = $"正在備份 ACL 資訊至 {backupFile}...";
            var (ok, msg) = await _ntfsService.BackupAclsAsync(TargetPath, backupFile);
            AppendOutput(msg);
            StatusMessage = ok ? "ACL 備份完成。" : "ACL 備份失敗。";
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    public async Task RestoreAclsAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(TargetPath)) return;
        try
        {
            IsRunning = true;
            var backupFile = string.IsNullOrWhiteSpace(AclBackupPath)
                ? Path.Combine(Path.GetDirectoryName(TargetPath) ?? @"C:\", "AclBackup.txt")
                : AclBackupPath;
            StatusMessage = $"正在從 {backupFile} 還原 ACL...";
            var (ok, msg) = await _ntfsService.RestoreAclsAsync(TargetPath, backupFile);
            AppendOutput(msg);
            StatusMessage = ok ? "ACL 還原完成。" : "ACL 還原失敗。";
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await InspectNtfsFeaturesAsync();
        }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            AppendOutput("[INFO] Cancellation requested by user.");
            _cts.Cancel();
        }
    }

    [RelayCommand]
    private void ClearTerminal()
    {
        TerminalOutput = "";
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        AdminHelper.RestartAsAdmin();
    }
}
