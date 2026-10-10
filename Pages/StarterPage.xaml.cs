using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.Services;
using DiskMasterWinUI.ViewModels;

namespace DiskMasterWinUI.Pages;

public sealed partial class StarterPage : Page
{
    public StarterViewModel ViewModel { get; } = new();

    public StarterPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;
        StarterHeroTitle.Text = lang switch
        {
            "zh-CN" => "DiskMaster 智能系统守护精灵 (Easy Mode)",
            "en-US" => "DiskMaster Intelligent System Guardian (Easy Mode)",
            "ja-JP" => "DiskMaster インテリジェントシステムガーディアン (Easy Mode)",
            _ => "DiskMaster 智慧系統守護精靈 (Easy Mode)"
        };
    }

    private bool _hasLoadedOnce;

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLanguage();
        if (!_hasLoadedOnce)
        {
            _hasLoadedOnce = true;
            await ViewModel.RefreshStatusCommand.ExecuteAsync(null);
        }
    }
}
