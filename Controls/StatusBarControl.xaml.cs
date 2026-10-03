using Microsoft.UI.Xaml.Controls;
using DiskMasterWinUI.ViewModels;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Controls;

public sealed partial class StatusBarControl : UserControl
{
    public StatusBarViewModel ViewModel => StatusBarViewModel.Instance;

    public StatusBarControl()
    {
        InitializeComponent();
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
    }

    public void ApplyLanguage()
    {
        bool isZh = LocalizationService.Instance.IsChinese;
        StopBtn.Content = isZh ? "⏹️ 停止" : "⏹️ Stop";
    }
}
