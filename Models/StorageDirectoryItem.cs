using CommunityToolkit.Mvvm.ComponentModel;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Models;

public enum StorageSafetyLevel
{
    SafeToClean,
    CleanViaTool,
    SystemCore
}

public partial class StorageDirectoryItem : ObservableObject
{
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _nameZhTw = "";
    [ObservableProperty] private string _nameZhCn = "";
    [ObservableProperty] private string _nameEnUs = "";
    [ObservableProperty] private string _nameJaJp = "";

    [ObservableProperty] private string _descZhTw = "";
    [ObservableProperty] private string _descZhCn = "";
    [ObservableProperty] private string _descEnUs = "";
    [ObservableProperty] private string _descJaJp = "";

    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private int _itemCount;
    public int FileCount { get => ItemCount; set => ItemCount = value; }
    [ObservableProperty] private int _directoryCount;
    [ObservableProperty] private double _usedPercentage;
    [ObservableProperty] private string _percentageDisplay = "";
    [ObservableProperty] private bool _isCalculated;
    [ObservableProperty] private bool _isCalculating;
    [ObservableProperty] private bool _exists;
    [ObservableProperty] private StorageSafetyLevel _safetyLevel;
    [ObservableProperty] private string _actionType = "OpenExplorer";
    [ObservableProperty] private string _icon = "📁";

    public string DisplayName => LocalizationService.Instance.CurrentLanguage switch
    {
        "zh-CN" => NameZhCn,
        "en-US" => NameEnUs,
        "ja-JP" => NameJaJp,
        _ => NameZhTw
    };

    public string Description => DisplayDescription;

    public string SafetyRating => SafetyLevel switch
    {
        StorageSafetyLevel.SafeToClean => "SafeToPurge",
        StorageSafetyLevel.CleanViaTool => "CleanViaSystemTool",
        StorageSafetyLevel.SystemCore => "EssentialCore",
        _ => "EssentialCore"
    };

    public bool CanOpenExplorer => true;

    public bool CanSafeClean => SafetyLevel == StorageSafetyLevel.SafeToClean 
        || Id is "SoftwareDistribution" or "Hiberfil" or "UserTemp" or "WindowsBt" or "WindowsOld";

    public bool CanDismClean => Id == "WinSxS" || ActionType == "DismClean";

    public string DisplayDescription => LocalizationService.Instance.CurrentLanguage switch
    {
        "zh-CN" => DescZhCn,
        "en-US" => DescEnUs,
        "ja-JP" => DescJaJp,
        _ => DescZhTw
    };

    public string DisplaySafety => SafetyLevel switch
    {
        StorageSafetyLevel.SafeToClean => LocalizationService.Instance.CurrentLanguage switch
        {
            "zh-CN" => "🟢 可直接安全清理",
            "en-US" => "🟢 Safe to Purge",
            "ja-JP" => "🟢 安全に削除可能",
            _ => "🟢 可直接安全清理"
        },
        StorageSafetyLevel.CleanViaTool => LocalizationService.Instance.CurrentLanguage switch
        {
            "zh-CN" => "🟡 建议通过专用工具/DISM清理",
            "en-US" => "🟡 Clean via Tool / DISM",
            "ja-JP" => "🟡 ツール/DISM推奨",
            _ => "🟡 建議透過專用工具/DISM清理"
        },
        _ => LocalizationService.Instance.CurrentLanguage switch
        {
            "zh-CN" => "🔴 核心系统保护 (切勿直接删除)",
            "en-US" => "🔴 System Core (Do Not Delete)",
            "ja-JP" => "🔴 システム基幹 (削除不可)",
            _ => "🔴 核心系統保護 (切勿直接刪除)"
        }
    };

    public string DisplaySize
    {
        get
        {
            if (!Exists) return LocalizationService.Instance.CurrentLanguage switch
            {
                "zh-CN" => "不存在 / 未启用",
                "en-US" => "Not Present / Inactive",
                "ja-JP" => "存在しません / 無効",
                _ => "不存在 / 未啟用"
            };

            if (IsCalculating) return LocalizationService.Instance.CurrentLanguage switch
            {
                "zh-CN" => "计算中...",
                "en-US" => "Calculating...",
                "ja-JP" => "計算中...",
                _ => "計算中..."
            };

            if (SizeBytes >= 1024L * 1024L * 1024L)
            {
                return $"{(double)SizeBytes / (1024L * 1024L * 1024L):F2} GB";
            }
            if (SizeBytes >= 1024L * 1024L)
            {
                return $"{(double)SizeBytes / (1024L * 1024L):F1} MB";
            }
            if (SizeBytes >= 1024L)
            {
                return $"{(double)SizeBytes / 1024L:F0} KB";
            }
            return $"{SizeBytes} B";
        }
    }

    public string DisplayActionLabel => ActionType switch
    {
        "DismClean" => LocalizationService.Instance.CurrentLanguage switch
        {
            "zh-CN" => "⚡ 执行 DISM 元件清理",
            "en-US" => "⚡ Run DISM Cleanup",
            "ja-JP" => "⚡ DISM コンポーネント整理",
            _ => "⚡ 執行 DISM 元件清理"
        },
        "SafeClean" => LocalizationService.Instance.CurrentLanguage switch
        {
            "zh-CN" => "🧹 一键清除此目录",
            "en-US" => "🧹 Purge Directory",
            "ja-JP" => "🧹 ディレクトリを完全消去",
            _ => "🧹 一鍵清除此目錄"
        },
        "DisableHibernate" => LocalizationService.Instance.CurrentLanguage switch
        {
            "zh-CN" => "⚡ 关闭休眠并释放文件",
            "en-US" => "⚡ Disable & Purge Hiberfil",
            "ja-JP" => "⚡ 休止無効化・領域解放",
            _ => "⚡ 關閉休眠並釋放檔案"
        },
        _ => LocalizationService.Instance.CurrentLanguage switch
        {
            "zh-CN" => "📁 在资源管理器中打开",
            "en-US" => "📁 Open in Explorer",
            "ja-JP" => "📁 エクスプローラーで開く",
            _ => "📁 在檔案總管中開啟"
        }
    };

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DisplayDescription));
        OnPropertyChanged(nameof(DisplaySafety));
        OnPropertyChanged(nameof(DisplaySize));
        OnPropertyChanged(nameof(DisplayActionLabel));
    }
}
