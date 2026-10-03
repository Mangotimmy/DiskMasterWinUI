using CommunityToolkit.Mvvm.ComponentModel;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Hardware device eligible or armed to wake Windows from sleep, with individual toggle capability.
/// </summary>
public partial class PowerWakeDeviceItem : ObservableObject
{
    [ObservableProperty]
    private string _deviceName = "";

    [ObservableProperty]
    private bool _isArmed;

    public string Category
    {
        get
        {
            var lower = DeviceName.ToLowerInvariant();
            var lang = LocalizationService.Instance.CurrentLanguage;
            if (lower.Contains("mouse") || lower.Contains("pointing") || lower.Contains("滑鼠") || lower.Contains("鼠标"))
                return lang switch { "zh-CN" => "鼠标", "en-US" => "Mouse", "ja-JP" => "マウス", _ => "滑鼠" };
            if (lower.Contains("keyboard") || lower.Contains("鍵盤") || lower.Contains("键盘"))
                return lang switch { "zh-CN" => "键盘", "en-US" => "Keyboard", "ja-JP" => "キーボード", _ => "鍵盤" };
            if (lower.Contains("ethernet") || lower.Contains("network") || lower.Contains("lan") || lower.Contains("wi-fi") || lower.Contains("wireless") || lower.Contains("網卡") || lower.Contains("网卡"))
                return lang switch { "zh-CN" => "网卡", "en-US" => "Network Adapter", "ja-JP" => "ネットワークカード", _ => "網路卡" };
            if (lower.Contains("usb") || lower.Contains("controller"))
                return lang switch { "zh-CN" => "USB 控制器", "en-US" => "USB Controller", "ja-JP" => "USB コントローラ", _ => "USB 控制器" };
            return lang switch { "zh-CN" => "系统硬件", "en-US" => "System Hardware", "ja-JP" => "システムハードウェア", _ => "系統硬體" };
        }
    }

    public string CategoryIcon
    {
        get
        {
            var lower = DeviceName.ToLowerInvariant();
            if (lower.Contains("mouse") || lower.Contains("pointing") || lower.Contains("滑鼠") || lower.Contains("鼠标")) return "🖱️";
            if (lower.Contains("keyboard") || lower.Contains("鍵盤") || lower.Contains("键盘")) return "⌨️";
            if (lower.Contains("ethernet") || lower.Contains("network") || lower.Contains("lan") || lower.Contains("wi-fi") || lower.Contains("wireless") || lower.Contains("網卡") || lower.Contains("网卡")) return "🌐";
            if (lower.Contains("usb") || lower.Contains("controller")) return "🔌";
            return "⚙️";
        }
    }
}
