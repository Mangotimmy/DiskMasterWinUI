using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Services;
using WinRT.Interop;

namespace DiskMasterWinUI.Controls;

public sealed partial class FloatingTabWindow : Window
{
    public string TabTag { get; }
    public string TabHeader { get; }
    public Page HostedPage { get; }
    public event Action<FloatingTabWindow>? DockRequested;
    public event Action<FloatingTabWindow>? DockToRightRequested;
    public event Action<FloatingTabWindow>? DockToBottomRequested;

    private bool _isDocking = false;

    public FloatingTabWindow(string tag, string header, Page page, Windows.Graphics.PointInt32? initialPosition = null)
    {
        InitializeComponent();

        TabTag = tag;
        TabHeader = header;
        HostedPage = page;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(FloatingTitleBar);

        FloatingTitleBar.Title = $"DiskMaster Pro — {header}";
        this.Title = FloatingTitleBar.Title;

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(iconPath))
        {
            try { AppWindow.SetIcon(iconPath); } catch { }
        }

        // Host the page
        FloatingContentFrame.Content = page;

        // Apply font & language
        ApplyFont();
        ApplyLanguage();

        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
        SettingsService.Instance.SettingsChanged += ApplyFont;

        // Window size & position with robust multi-monitor DPI & resolution calculation
        try
        {
            var parentHwnd = MainWindow.CurrentInstance != null
                ? WindowNative.GetWindowHandle(MainWindow.CurrentInstance)
                : IntPtr.Zero;
            var bounds = DisplayHelper.CalculateFloatingWindowBounds(initialPosition, parentHwnd);
            AppWindow.MoveAndResize(bounds);
        }
        catch { }

        this.Closed += FloatingTabWindow_Closed;
    }

    public void ApplyLanguage()
    {
        FloatingTitleBar.Title = $"DiskMaster Pro — {TabHeader}";
        this.Title = FloatingTitleBar.Title;
        var loc = LocalizationService.Instance;
        WindowNoticeText.Text = loc.CurrentLanguage switch
        {
            "zh-CN" => "独立分页窗口",
            "en-US" => "Floating Tab Window",
            "ja-JP" => "独立タブウィンドウ",
            _ => "獨立分頁視窗"
        };
        DockRightText.Text = loc.CurrentLanguage switch
        {
            "zh-CN" => "◨ 停靠至右侧",
            "en-US" => "◨ Dock Right",
            "ja-JP" => "◨ 右にドック",
            _ => "◨ 停靠至右側"
        };
        DockBottomText.Text = loc.CurrentLanguage switch
        {
            "zh-CN" => "⬓ 停靠至底部",
            "en-US" => "⬓ Dock Bottom",
            "ja-JP" => "⬓ 下にドック",
            _ => "⬓ 停靠至底部"
        };
        DockBackText.Text = loc.CurrentLanguage switch
        {
            "zh-CN" => "嵌回主窗口",
            "en-US" => "Dock to Main Window",
            "ja-JP" => "メインウィンドウに戻す",
            _ => "嵌回主視窗"
        };
    }

    public void ApplyFont()
    {
        var font = SettingsService.Instance.Current.CustomFontFamily;
        if (!string.IsNullOrWhiteSpace(font))
        {
            try
            {
                var ff = new FontFamily(font);
                FloatingContentFrame.FontFamily = ff;
                DockRightButton.FontFamily = ff;
                DockBottomButton.FontFamily = ff;
                DockBackButton.FontFamily = ff;
                WindowNoticeText.FontFamily = ff;
            }
            catch { }
        }
    }

    private void DockRight_Click(object sender, RoutedEventArgs e)
    {
        if (_isDocking) return;
        _isDocking = true;
        UnsubscribeEvents();
        FloatingContentFrame.Content = null;
        DockToRightRequested?.Invoke(this);
        try { this.Close(); } catch { }
    }

    private void DockBottom_Click(object sender, RoutedEventArgs e)
    {
        if (_isDocking) return;
        _isDocking = true;
        UnsubscribeEvents();
        FloatingContentFrame.Content = null;
        DockToBottomRequested?.Invoke(this);
        try { this.Close(); } catch { }
    }

    private void DockBack_Click(object sender, RoutedEventArgs e)
    {
        PerformDock(closeWindow: true);
    }

    private void FloatingTabWindow_Closed(object sender, WindowEventArgs e)
    {
        if (!_isDocking)
        {
            PerformDock(closeWindow: false);
        }
    }

    private void UnsubscribeEvents()
    {
        LocalizationService.Instance.LanguageChanged -= ApplyLanguage;
        SettingsService.Instance.SettingsChanged -= ApplyFont;
    }

    public void PerformDock(bool closeWindow = true)
    {
        if (_isDocking) return;
        _isDocking = true;
        UnsubscribeEvents();

        // Remove page from frame before docking
        FloatingContentFrame.Content = null;
        DockRequested?.Invoke(this);

        if (closeWindow)
        {
            try
            {
                this.Close();
            }
            catch { }
        }
    }
}
