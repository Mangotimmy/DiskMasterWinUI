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

        if (DiskSelectorTitle != null)
        {
            DiskSelectorTitle.Text = LocalizationService.T("實體硬碟自動偵測與效能監控 (Task Manager Mode)",
                                                           "实体硬盘自动侦测与效能监控 (Task Manager Mode)",
                                                           "Physical Disks Auto-Detection & Metrics (Task Manager Mode)",
                                                           "物理ディスク自動検出と負荷監視 (Task Manager Mode)");
        }
        if (DiskSelectorHint != null)
        {
            DiskSelectorHint.Text = LocalizationService.T("點選硬碟以即時切換專屬使用率與歷程圖表",
                                                          "点击硬盘以实时切换专属使用率与历程图表",
                                                          "Click a disk to switch live usage & history chart",
                                                          "ディスクをクリックして使用率と推移グラフを切り替え");
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLanguage();
        UpdateOverlayButtonState();
        DiskMasterWinUI.Controls.PerformanceOverlayWindow.OverlayStateChanged += OnOverlayStateChanged;
        ViewModel.DiskHistoryUpdated += OnDiskHistoryUpdated;
        ViewModel.StartAutoRefresh();
        await ViewModel.InitializeAsync();
        RedrawDiskChart();
    }

    private void OnOverlayStateChanged()
    {
        DispatcherQueue.TryEnqueue(UpdateOverlayButtonState);
    }

    private void TogglePerformanceOverlay_Click(object sender, RoutedEventArgs e)
    {
        DebugLogService.Instance.LogAction("效能監控 (Performance)", "使用者點擊切換 FPS HUD 懸浮窗");
        DiskMasterWinUI.Controls.PerformanceOverlayWindow.Toggle();
        UpdateOverlayButtonState();
    }

    private void ToggleHudClickThrough_Click(object sender, RoutedEventArgs e)
    {
        DebugLogService.Instance.LogAction("效能監控 (Performance)", "使用者點擊切換 HUD 滑鼠穿透模式");
        DiskMasterWinUI.Controls.PerformanceOverlayWindow.ToggleClickThroughGlobal();
        UpdateOverlayButtonState();
    }

    private void OpenHudSettings_Click(object sender, RoutedEventArgs e)
    {
        DiskMasterWinUI.Controls.HudSettingsWindow.Toggle(DiskMasterWinUI.Controls.PerformanceOverlayWindow.CurrentInstance);
    }

    private void PageHudTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string theme)
        {
            var s = SettingsService.Instance.Current;
            s.HudTheme = theme;
            SettingsService.Instance.Save();
            DiskMasterWinUI.Controls.PerformanceOverlayWindow.CurrentInstance?.ApplyColorTheme(theme);
            DebugLogService.Instance.LogAction("效能監控 (Performance)", $"從主頁面切換 HUD 主題色彩為 [{theme}]");
        }
    }

    private void PageHudStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string style)
        {
            var s = SettingsService.Instance.Current;
            s.HudStyle = style;
            SettingsService.Instance.Save();
            DebugLogService.Instance.LogAction("效能監控 (Performance)", $"從主頁面切換 HUD 風格型態為 [{style}]");
        }
    }

    private void UpdateOverlayButtonState()
    {
        bool isHudOpen = DiskMasterWinUI.Controls.PerformanceOverlayWindow.CurrentInstance != null;
        if (OverlayToggleBtnText != null)
        {
            OverlayToggleBtnText.Text = isHudOpen
                ? "關閉效能懸浮窗 (HUD 開啟中)"
                : "置頂效能懸浮窗 (FPS HUD)";
        }

        if (HudClickThroughBtnText != null)
        {
            bool isCt = DiskMasterWinUI.Controls.PerformanceOverlayWindow.CurrentInstance?.IsClickThrough ?? SettingsService.Instance.Current.HudClickThrough;
            HudClickThroughBtnText.Text = isCt ? "穿透中 [Ctrl+Shift+H]" : "滑鼠穿透 [Ctrl+Shift+H]";
            if (HudClickThroughIcon != null)
            {
                HudClickThroughIcon.Glyph = isCt ? "\uE72E" : "\uE8C0"; // lock / transparent indicator
            }
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        DiskMasterWinUI.Controls.PerformanceOverlayWindow.OverlayStateChanged -= OnOverlayStateChanged;
        ViewModel.DiskHistoryUpdated -= OnDiskHistoryUpdated;
        ViewModel.StopAutoRefresh();
    }

    private void DiskChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawDiskChart();
    }

    private void OnDiskHistoryUpdated()
    {
        DispatcherQueue.TryEnqueue(RedrawDiskChart);
    }

    private void RedrawDiskChart()
    {
        if (DiskChartCanvas == null || DiskChartPolyline == null || DiskChartPolygon == null) return;

        double width = DiskChartCanvas.ActualWidth;
        double height = DiskChartCanvas.ActualHeight;
        if (width <= 10 || height <= 10) return;

        var history = ViewModel.DiskUsageHistory.ToArray();
        if (history.Length == 0) return;

        var polylinePoints = new Microsoft.UI.Xaml.Media.PointCollection();
        var polygonPoints = new Microsoft.UI.Xaml.Media.PointCollection();

        polygonPoints.Add(new Windows.Foundation.Point(0, height));

        double stepX = width / Math.Max(1, history.Length - 1);
        for (int i = 0; i < history.Length; i++)
        {
            double val = Math.Clamp(history[i], 0, 100);
            double x = i * stepX;
            double y = height - (val / 100.0 * height);

            var pt = new Windows.Foundation.Point(x, y);
            polylinePoints.Add(pt);
            polygonPoints.Add(pt);
        }

        polygonPoints.Add(new Windows.Foundation.Point(width, height));

        DiskChartPolyline.Points = polylinePoints;
        DiskChartPolygon.Points = polygonPoints;
    }
}
