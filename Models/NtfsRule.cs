using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public enum NtfsPresetType
{
    TakeOwnershipAndUnlock,
    GrantEveryoneFullControl,
    ResetToDefaultInheritance,
    StrictAdministratorsOnly
}

public partial class NtfsRule : ObservableObject
{
    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private string _principal = "Administrators";
    [ObservableProperty] private string _accessLevel = "F"; // F = Full Control, M = Modify, RX = Read & Exec, R = Read, W = Write
    [ObservableProperty] private string _inheritance = "(OI)(CI)"; // Object Inherit & Container Inherit
    [ObservableProperty] private bool _recursive = true;
    [ObservableProperty] private bool _continueOnError = true;

    public string GeneratedCommand
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TargetPath)) return "";
            var recFlag = Recursive ? " /T" : "";
            var errFlag = ContinueOnError ? " /C" : "";
            return $"icacls \"{TargetPath}\" /grant:r \"{Principal}\":{Inheritance}({AccessLevel}){recFlag}{errFlag}";
        }
    }
}
