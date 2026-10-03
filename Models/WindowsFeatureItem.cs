using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class WindowsFeatureItem : ObservableObject
{
    [ObservableProperty] private string _featureName = "";
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private bool _isEnabled;

    public string StateBadge => IsEnabled ? "🟢 已啟用 (Enabled)" : "⚪ 已停用 (Disabled)";
    public string StateBadgeEn => IsEnabled ? "🟢 Enabled" : "⚪ Disabled";
    public string ActionButtonLabel => IsEnabled ? "❌ 停用功能" : "⚡ 一鍵啟用";
    public string ActionButtonLabelEn => IsEnabled ? "Disable" : "Enable";
}
