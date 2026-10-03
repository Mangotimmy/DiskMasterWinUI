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
