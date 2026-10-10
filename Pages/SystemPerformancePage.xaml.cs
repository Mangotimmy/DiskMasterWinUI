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
            "zh-CN" => "📊 实时硬件效能",
            "en-US" => "📊 Live Hardware Metrics",
            "ja-JP" => "📊 リアルタイムハードウェア負荷",
            _ => "📊 即時硬體效能"
        };

        TabProcessHunter.Header = lang switch
        {
            "zh-CN" => "🎯 进程猎手与树状终结",
            "en-US" => "🎯 Process Hunter & Tree Killer",
            "ja-JP" => "🎯 プロセスハンター＆強制終了",
            _ => "🎯 進程獵手與樹狀終結"
        };

        TabSystemInfo.Header = lang switch
        {
            "zh-CN" => "📋 系统硬件与补丁报告",
            "en-US" => "📋 System Profile & Hotfixes",
            "ja-JP" => "📋 システム構成と更新プログラム",
            _ => "📋 系統硬體與修補報告"
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLanguage();
        ViewModel.StartAutoRefresh();
        await ViewModel.InitializeAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.StopAutoRefresh();
    }
}
