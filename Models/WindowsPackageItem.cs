using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class WindowsPackageItem : ObservableObject
{
    [ObservableProperty] private string _packageIdentity = "";
    [ObservableProperty] private string _kbArticle = "";
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private string _releaseType = "";
    [ObservableProperty] private string _installTime = "";

    public string DisplayTitle => string.IsNullOrEmpty(KbArticle) ? PackageIdentity : $"{KbArticle} — {PackageIdentity}";
    public string Summary => $"{ReleaseType} | {State} | {InstallTime}";
}
