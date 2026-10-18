using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DiskMasterWinUI.ViewModels;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Controls;

public sealed partial class StatusBarControl : UserControl
{
    public StatusBarViewModel ViewModel => StatusBarViewModel.Instance;

    private bool _isLanguageSubscribed;

    public StatusBarControl()
    {
        InitializeComponent();
        ApplyLanguage();
        EnsureLanguageSubscription();
        Loaded += (_, _) => EnsureLanguageSubscription();
        Unloaded += (_, _) => ReleaseLanguageSubscription();
    }

    private void EnsureLanguageSubscription()
    {
        if (_isLanguageSubscribed) return;
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
        _isLanguageSubscribed = true;
    }

    private void ReleaseLanguageSubscription()
    {
        if (!_isLanguageSubscribed) return;
        LocalizationService.Instance.LanguageChanged -= ApplyLanguage;
        _isLanguageSubscribed = false;
    }

    private void ConsoleBtn_Click(object sender, RoutedEventArgs e)
    {
        DebugLogService.Instance.LogAction("使用者介面 (UI)", "點擊底部列開啟即時主控台");
        LiveConsoleWindow.Toggle();
    }

    public void ApplyLanguage()
    {
        var loc = LocalizationService.Instance;
        StopBtn.Content = loc["Stop"];
        ConsoleButtonText.Text = LocalizationService.T("主控台", "控制台", "Console", "コンソール");
        ToolTipService.SetToolTip(ConsoleBtn, LocalizationService.T(
            "開啟即時主控台迷你視窗 [Ctrl+Shift+K]",
            "打开实时控制台迷你窗口 [Ctrl+Shift+K]",
            "Open the live console mini-window [Ctrl+Shift+K]",
            "ライブコンソールを開く [Ctrl+Shift+K]"));
    }
}
