using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Services;
using DiskMasterWinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DiskMasterWinUI.Pages;

public sealed partial class SystemPerformancePage : Page
{
    public SystemPerformanceViewModel ViewModel { get; } = new();

    public SystemPerformancePage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        TabReorderHelper.Attach(PerfTabView);
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;

        TabPerfMetrics.Header = lang switch
        {
            "zh-CN" => "📊 实时硬件资源 (typeperf 性能仪表)",
            "en-US" => "📊 Live Hardware Metrics (typeperf)",
            "ja-JP" => "📊 リアルタイムハードウェア負荷 (typeperf)",
            _ => "📊 即時硬體資源 (typeperf 效能儀表)"
        };

        TabProcessHunter.Header = lang switch
        {
            "zh-CN" => "🎯 进程猎手与强制终结 (Process Hunter & Tree Killer)",
            "en-US" => "🎯 Process Hunter & Tree Killer",
            "ja-JP" => "🎯 プロセスハンター＆強制終了 (Tree Killer)",
            _ => "🎯 進程獵手與強制終結 (Process Hunter & Tree Killer)"
        };

        TabSystemInfo.Header = lang switch
        {
            "zh-CN" => "📋 系统配置与补丁报告 (systeminfo)",
            "en-US" => "📋 System Profile & Hotfixes (systeminfo)",
            "ja-JP" => "📋 システム構成と更新プログラム (systeminfo)",
            _ => "📋 系統配置與修補程式報告 (systeminfo)"
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLanguage();
        await ViewModel.InitializeAsync();
    }
}
