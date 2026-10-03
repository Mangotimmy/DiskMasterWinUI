using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Models;

public class PowerPlanItem
{
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsUltimate => Name.Contains("終極") || Name.Contains("卓越") || Name.Contains("Ultimate") || Guid.Equals("e9a42b02-d5df-448d-aa00-03f14749eb61", System.StringComparison.OrdinalIgnoreCase);

    public string DisplayName => Name;

    public string StatusBadge
    {
        get
        {
            var lang = LocalizationService.Instance.CurrentLanguage;
            if (IsActive)
            {
                return lang switch
                {
                    "zh-CN" => "🟢 当前激活",
                    "en-US" => "🟢 Active",
                    "ja-JP" => "🟢 アクティブ",
                    _ => "🟢 作用中"
                };
            }
            return lang switch
            {
                "zh-CN" => "⚪ 未激活",
                "en-US" => "⚪ Inactive",
                "ja-JP" => "⚪ 非アクティブ",
                _ => "⚪ 未作用"
            };
        }
    }

    public string TypeBadge
    {
        get
        {
            var lang = LocalizationService.Instance.CurrentLanguage;
            if (IsUltimate)
            {
                return lang switch
                {
                    "zh-CN" => "⚡ 卓越性能",
                    "en-US" => "⚡ Ultimate",
                    "ja-JP" => "⚡ 究極",
                    _ => "⚡ 終極效能"
                };
            }
            if (Name.Contains("高效能") || Name.Contains("高性能") || Name.Contains("High"))
            {
                return lang switch
                {
                    "zh-CN" => "🚀 高性能",
                    "en-US" => "🚀 High Perf",
                    "ja-JP" => "🚀 高性能",
                    _ => "🚀 高效能"
                };
            }
            return lang switch
            {
                "zh-CN" => "🔋 标准",
                "en-US" => "🔋 Standard",
                "ja-JP" => "🔋 標準",
                _ => "🔋 標準"
            };
        }
    }
}
