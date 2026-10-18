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
    private string _lastAppliedFontSignature = "";

    /// <summary>Frame that owns the hosted page while this window is alive (diagnostics only).</summary>
    public Frame ContentFrame => FloatingContentFrame;

    /// <summary>True when this window is currently the single parent of the hosted page.</summary>
    public bool IsHostingPage
    {
        get
        {
            try { return ReferenceEquals(FloatingContentFrame.Content, HostedPage); }
            catch { return false; }
        }
    }

    /// <summary>
    /// Releases the hosted page from this window's frame. Idempotent and safe after Close().
    /// </summary>
    public void DetachHostedPage()
    {
        try
        {
            if (ReferenceEquals(FloatingContentFrame.Content, HostedPage))
            {
                FloatingContentFrame.Content = null;
            }
        }
        catch { }
    }

    /// <summary>
    /// Detaches the page and closes the window WITHOUT raising DockRequested, so the caller
    /// (MainWindow) can re-parent the page exactly once into another host.
    /// </summary>
    public void DetachAndClose()
    {
        if (_isDocking) return;
        _isDocking = true;
        UnsubscribeEvents();
        DetachHostedPage();
        try { this.Close(); } catch { }
    }

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

        // Host the page. A Page may only ever have one parent, so first ask MainWindow to release it
        // from the central frame / dock panes / any other floating window it currently lives in.
        // Without this detach the assignment below throws
        // "Element is already the child of another element." / "Value does not fall within the expected range."
        try { MainWindow.CurrentInstance?.DetachPageFromAllHosts(page); } catch { }
        FloatingContentFrame.Content = page;
        UndoCutManager.Attach(RootContainer, () => HostedPage);

        // Apply font, scale & language
        ApplyFont();
        ApplyLanguage();
        UpdateFloatingViewportLayout();

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
        var settings = SettingsService.Instance.Current;
        var font = settings.CustomFontFamily;
        if (string.IsNullOrWhiteSpace(font)) font = "Segoe UI Variable";
        var scalePercent = settings.CustomFontScale > 0 ? settings.CustomFontScale : 100.0;
        string signature = font + "|" + scalePercent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        if (string.Equals(_lastAppliedFontSignature, signature, StringComparison.Ordinal)) return;
        _lastAppliedFontSignature = signature;

        double scaleFactor = scalePercent / 100.0;

        try
        {
            var ff = new FontFamily(font);
            FloatingContentFrame.FontFamily = ff;
            DockRightButton.FontFamily = ff;
            DockBottomButton.FontFamily = ff;
            DockBackButton.FontFamily = ff;
            WindowNoticeText.FontFamily = ff;

            FloatingContentFrame.FontSize = 14.0 * scaleFactor;

            if (this.Content is DependencyObject contentDep)
            {
                FontScaleHelper.ApplyFontAndScale(contentDep, ff, scaleFactor);
            }
            if (HostedPage != null)
            {
                FontScaleHelper.ApplyFontAndScale(HostedPage, ff, scaleFactor);
            }
        }
        catch { }
        UpdateFloatingViewportLayout();
    }

    private void UpdateFloatingViewportLayout()
    {
        if (FloatingScrollViewer == null || FloatingContentRoot == null) return;
        var viewportW = FloatingScrollViewer.ActualWidth;
        var viewportH = FloatingScrollViewer.ActualHeight;
        if (viewportW <= 0) return;

        int savedScale = SettingsService.Instance.Current.ScalePercent;
        double scaleFactor = (savedScale >= 80 && savedScale <= 150) ? savedScale / 100.0 : 1.0;
        var contentH = FloatingContentFrame?.ActualHeight ?? FloatingContentRoot.ActualHeight;
        var (layoutW, offsetX, bottomMargin, factor) = DisplayHelper.CalculateScaledViewportLayout(
            viewportW, viewportH, contentH, scaleFactor);

        FloatingContentRoot.Width = layoutW;
        FloatingContentRoot.MaxWidth = layoutW;
        if (FloatingContentFrame != null)
        {
            FloatingContentFrame.Width = layoutW;
            FloatingContentFrame.MaxWidth = layoutW;
        }
        FloatingScaleTransform.ScaleX = factor;
        FloatingScaleTransform.ScaleY = factor;
        FloatingContentRoot.Margin = new Thickness(offsetX, 0, 0, bottomMargin);
    }

    private void FloatingScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFloatingViewportLayout();
    }

    private void FloatingContentFrame_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFloatingViewportLayout();
    }

    private void FloatingContentRoot_BringIntoViewRequested(UIElement sender, BringIntoViewRequestedEventArgs args)
    {
        var r = args.TargetRect;
        if (!double.IsNaN(r.X) && r.X != 0)
        {
            args.TargetRect = new Windows.Foundation.Rect(0, r.Y, Math.Min(1.0, Math.Max(0.0, r.Width)), r.Height);
        }
    }

    private void DockRight_Click(object sender, RoutedEventArgs e)
    {
        if (_isDocking) return;
        _isDocking = true;
        UnsubscribeEvents();
        DetachHostedPage();
        DockToRightRequested?.Invoke(this);
        try { this.Close(); } catch { }
    }

    private void DockBottom_Click(object sender, RoutedEventArgs e)
    {
        if (_isDocking) return;
        _isDocking = true;
        UnsubscribeEvents();
        DetachHostedPage();
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
        DetachHostedPage();
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
