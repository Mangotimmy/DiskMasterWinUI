using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;
using WinRT.Interop;

namespace DiskMasterWinUI.Controls;

public sealed partial class PerformanceOverlayWindow : Window
{
    public static PerformanceOverlayWindow? CurrentInstance { get; private set; }

    private readonly SystemPerformanceService _perfService = new();
    private readonly DispatcherTimer _metricsTimer = new();
    private readonly Stopwatch _fpsStopwatch = new();
    private readonly Stopwatch _frameStopwatch = new();
    private readonly Queue<double> _recentFrameDurations = new();

    private int _frameCount;
    private int _lastFps = 60;
    private int _lastLow1PctFps = 60;
    private double _lastFrameMs = 16.6;

    private bool _isCompact;
    private bool _isInitialized = false;
    private IntPtr _hwnd;
    private OverlappedPresenter? _presenter;

    // Logical DIP dimensions
    private const int ExpandedLogicalWidth = 360;
    private const int ExpandedLogicalHeight = 236;
    private const int CompactLogicalWidth = 420;
    private const int CompactLogicalHeight = 72;
    private const int BarLogicalWidth = 720;
    private const int BarLogicalHeight = 44;
    private const int BadgeLogicalWidth = 320;
    private const int BadgeLogicalHeight = 44;

    /// <summary>
    /// Computes the physical pixel client dimensions for the FPS HUD window, honoring both minimum
    /// logical DIP bounds and actual measured XAML content size scaled by the monitor's DPI factor.
    /// Backward-compatible with Tier 1 test harness reflection call.
    /// </summary>
    public static Windows.Graphics.SizeInt32 CalculateDpiAwareHudClientSize(
        bool isCompact,
        double dpiScale,
        double measuredWidthDip = 0,
        double measuredHeightDip = 0)
    {
        double scale = Math.Clamp(dpiScale, 1.0, 4.0);
        double baseW = isCompact ? CompactLogicalWidth : ExpandedLogicalWidth;
        double baseH = isCompact ? CompactLogicalHeight : ExpandedLogicalHeight;

        double logicalW = Math.Max(baseW, measuredWidthDip);
        double logicalH = Math.Max(baseH, measuredHeightDip);

        int targetW = (int)Math.Ceiling(logicalW * scale);
        int targetH = (int)Math.Ceiling(logicalH * scale);
        return new Windows.Graphics.SizeInt32(targetW, targetH);
    }

    public PerformanceOverlayWindow()
    {
        InitializeComponent();
        CurrentInstance = this;

        _hwnd = WindowNative.GetWindowHandle(this);
        ConfigureAlwaysOnTopWindow();

        // 1. Live Render FPS & ms Delay Tracking
        _fpsStopwatch.Start();
        _frameStopwatch.Start();
        CompositionTarget.Rendering += OnCompositionRendering;

        // 2. Metrics Update Timer (1.0s cadence)
        _metricsTimer.Interval = TimeSpan.FromSeconds(1.0);
        _metricsTimer.Tick += async (_, _) => await RefreshOverlayMetricsAsync();
        _metricsTimer.Start();

        // Apply saved HUD settings
        LoadSavedSettings();
        _isInitialized = true;
        ApplyMetricVisibility();

        // Immediate first sample
        _ = RefreshOverlayMetricsAsync();

        this.Closed += (_, _) =>
        {
            _metricsTimer.Stop();
            _fpsStopwatch.Stop();
            _frameStopwatch.Stop();
            CompositionTarget.Rendering -= OnCompositionRendering;
            if (ReferenceEquals(CurrentInstance, this))
            {
                CurrentInstance = null;
            }
        };
    }

    private void LoadSavedSettings()
    {
        try
        {
            var s = SettingsService.Instance.Current;

            // Opacity
            double op = s.HudOpacity > 0 ? s.HudOpacity : 0.85;
            ApplyOpacity(op);
            if (HudOpacitySlider != null) HudOpacitySlider.Value = (int)Math.Round(op * 100);

            // Style (Default is "Bar")
            string style = string.IsNullOrEmpty(s.HudStyle) ? "Bar" : s.HudStyle;
            ApplyHudStyle(style);
            if (HudStyleCombo != null)
            {
                for (int i = 0; i < HudStyleCombo.Items.Count; i++)
                {
                    if (HudStyleCombo.Items[i] is ComboBoxItem item && (item.Tag?.ToString() == style))
                    {
                        HudStyleCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Toggles
            if (ToggleFps != null) ToggleFps.IsOn = s.HudShowFps;
            if (ToggleLow1Pct != null) ToggleLow1Pct.IsOn = s.HudShowLow1Percent;
            if (ToggleMsDelay != null) ToggleMsDelay.IsOn = s.HudShowMsDelay;
            if (ToggleCpu != null) ToggleCpu.IsOn = s.HudShowCpu;
            if (ToggleGpu != null) ToggleGpu.IsOn = s.HudShowGpu;
            if (ToggleRam != null) ToggleRam.IsOn = s.HudShowRam;
            if (ToggleNet != null) ToggleNet.IsOn = s.HudShowNet;
            if (ToggleApi != null) ToggleApi.IsOn = s.HudShowApi;
            if (ToggleAppName != null) ToggleAppName.IsOn = s.HudShowAppName;

            ApplyMetricVisibility();
        }
        catch { }
    }

    private void ApplyOpacity(double opacity)
    {
        opacity = Math.Clamp(opacity, 0.05, 1.0);
        if (HudContainer != null)
        {
            HudContainer.Opacity = opacity;
        }
        if (OpacityValueText != null)
        {
            OpacityValueText.Text = $"{(int)Math.Round(opacity * 100)}%";
        }
    }

    private void ApplyHudStyle(string style)
    {
        if (BarViewPanel != null) BarViewPanel.Visibility = (style == "Bar") ? Visibility.Visible : Visibility.Collapsed;
        if (CardViewPanel != null) CardViewPanel.Visibility = (style == "Card") ? Visibility.Visible : Visibility.Collapsed;
        if (BadgeViewPanel != null) BadgeViewPanel.Visibility = (style == "Badge") ? Visibility.Visible : Visibility.Collapsed;

        ApplyDpiAwareWindowSize(repositionTopRight: false);
    }

    private void ApplyMetricVisibility()
    {
        if (!_isInitialized) return;
        var s = SettingsService.Instance.Current;

        // Bar badges
        if (BarFpsBadge != null) BarFpsBadge.Visibility = s.HudShowFps ? Visibility.Visible : Visibility.Collapsed;
        if (BarLow1PctBadge != null) BarLow1PctBadge.Visibility = s.HudShowLow1Percent ? Visibility.Visible : Visibility.Collapsed;
        if (BarMsDelayBadge != null) BarMsDelayBadge.Visibility = s.HudShowMsDelay ? Visibility.Visible : Visibility.Collapsed;
        if (BarApiBadge != null) BarApiBadge.Visibility = s.HudShowApi ? Visibility.Visible : Visibility.Collapsed;
        if (BarCpuBadge != null) BarCpuBadge.Visibility = s.HudShowCpu ? Visibility.Visible : Visibility.Collapsed;
        if (BarGpuBadge != null) BarGpuBadge.Visibility = s.HudShowGpu ? Visibility.Visible : Visibility.Collapsed;
        if (BarRamBadge != null) BarRamBadge.Visibility = s.HudShowRam ? Visibility.Visible : Visibility.Collapsed;
        if (BarNetBadge != null) BarNetBadge.Visibility = s.HudShowNet ? Visibility.Visible : Visibility.Collapsed;
        if (BarAppNameBadge != null) BarAppNameBadge.Visibility = s.HudShowAppName ? Visibility.Visible : Visibility.Collapsed;

        // Card badges
        if (CardFpsBadge != null) CardFpsBadge.Visibility = s.HudShowFps ? Visibility.Visible : Visibility.Collapsed;
        if (CardLow1PctBadge != null) CardLow1PctBadge.Visibility = s.HudShowLow1Percent ? Visibility.Visible : Visibility.Collapsed;
        if (CardMsDelayBadge != null) CardMsDelayBadge.Visibility = s.HudShowMsDelay ? Visibility.Visible : Visibility.Collapsed;
        if (GraphicsApiBadge != null) GraphicsApiBadge.Visibility = s.HudShowApi ? Visibility.Visible : Visibility.Collapsed;
        if (CardCpuRow != null) CardCpuRow.Visibility = s.HudShowCpu ? Visibility.Visible : Visibility.Collapsed;
        if (CardGpuRow != null) CardGpuRow.Visibility = s.HudShowGpu ? Visibility.Visible : Visibility.Collapsed;
        if (CardRamRow != null) CardRamRow.Visibility = s.HudShowRam ? Visibility.Visible : Visibility.Collapsed;
        if (CardNetRow != null) CardNetRow.Visibility = s.HudShowNet ? Visibility.Visible : Visibility.Collapsed;

        ApplyDpiAwareWindowSize(repositionTopRight: false);
    }

    private void OverlayRoot_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyDpiAwareWindowSize(repositionTopRight: false);
        if (OverlayRoot?.XamlRoot != null)
        {
            OverlayRoot.XamlRoot.Changed += (_, _) => ApplyDpiAwareWindowSize(repositionTopRight: false);
        }
    }

    private double GetCurrentDpiScale()
    {
        if (OverlayRoot?.XamlRoot != null && OverlayRoot.XamlRoot.RasterizationScale > 0)
        {
            return OverlayRoot.XamlRoot.RasterizationScale;
        }
        return Math.Max(1.0, DisplayHelper.GetDpi(_hwnd) / 96.0);
    }

    private void ConfigureAlwaysOnTopWindow()
    {
        try
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                _presenter = presenter;
                presenter.IsAlwaysOnTop = true;
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
            }

            var s = SettingsService.Instance.Current;
            bool hasSavedPos = s.HudCustomPositionX.HasValue && s.HudCustomPositionY.HasValue;
            ApplyDpiAwareWindowSize(repositionTopRight: !hasSavedPos);

            if (hasSavedPos)
            {
                appWindow.Move(new Windows.Graphics.PointInt32(s.HudCustomPositionX!.Value, s.HudCustomPositionY!.Value));
            }
        }
        catch { }
    }

    private void ApplyDpiAwareWindowSize(bool repositionTopRight)
    {
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow == null) return;
            double dpiScale = GetCurrentDpiScale();

            var s = SettingsService.Instance.Current;
            string style = s.HudStyle ?? "Bar";

            double baseW = style switch
            {
                "Bar" => BarLogicalWidth,
                "Badge" => BadgeLogicalWidth,
                _ => _isCompact ? CompactLogicalWidth : ExpandedLogicalWidth
            };
            double baseH = style switch
            {
                "Bar" => BarLogicalHeight,
                "Badge" => BadgeLogicalHeight,
                _ => _isCompact ? CompactLogicalHeight : ExpandedLogicalHeight
            };

            double measuredW = 0;
            double measuredH = 0;
            if (HudContainer != null && HudContainer.IsLoaded)
            {
                HudContainer.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                measuredW = HudContainer.DesiredSize.Width;
                measuredH = HudContainer.DesiredSize.Height;
            }

            double logicalW = Math.Max(baseW, measuredW);
            double logicalH = Math.Max(baseH, measuredH);

            int targetW = (int)Math.Ceiling(logicalW * dpiScale);
            int targetH = (int)Math.Ceiling(logicalH * dpiScale);
            var clientSize = new Windows.Graphics.SizeInt32(targetW, targetH);

            try
            {
                appWindow.ResizeClient(clientSize);
            }
            catch
            {
                appWindow.Resize(clientSize);
            }

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            int actualW = Math.Max(clientSize.Width, appWindow.Size.Width);
            int actualH = Math.Max(clientSize.Height, appWindow.Size.Height);

            if (repositionTopRight)
            {
                int marginRight = (int)Math.Round(16 * dpiScale);
                int marginTop = (int)Math.Round(24 * dpiScale);
                int initialX = workArea.X + workArea.Width - actualW - marginRight;
                int initialY = workArea.Y + marginTop;

                initialX = Math.Clamp(initialX, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - actualW));
                initialY = Math.Clamp(initialY, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - actualH));
                appWindow.Move(new Windows.Graphics.PointInt32(initialX, initialY));
            }
            else
            {
                ClampWindowToMonitorWorkArea(appWindow, workArea, actualW, actualH, dpiScale);
            }
        }
        catch { }
    }

    private static void ClampWindowToMonitorWorkArea(AppWindow appWindow, Windows.Graphics.RectInt32 workArea, int actualW, int actualH, double dpiScale)
    {
        int curX = appWindow.Position.X;
        int curY = appWindow.Position.Y;
        int maxX = Math.Max(workArea.X, workArea.X + workArea.Width - actualW);
        int maxY = Math.Max(workArea.Y, workArea.Y + workArea.Height - actualH);

        int snapDistance = (int)Math.Round(20 * dpiScale);
        if (Math.Abs(curX - workArea.X) <= snapDistance) curX = workArea.X;
        else if (Math.Abs(curX - maxX) <= snapDistance) curX = maxX;

        if (Math.Abs(curY - workArea.Y) <= snapDistance) curY = workArea.Y;
        else if (Math.Abs(curY - maxY) <= snapDistance) curY = maxY;

        int clampedX = Math.Clamp(curX, workArea.X, maxX);
        int clampedY = Math.Clamp(curY, workArea.Y, maxY);

        if (clampedX != appWindow.Position.X || clampedY != appWindow.Position.Y)
        {
            appWindow.Move(new Windows.Graphics.PointInt32(clampedX, clampedY));
        }
    }

    private void OnCompositionRendering(object? sender, object e)
    {
        double elapsedMs = _frameStopwatch.Elapsed.TotalMilliseconds;
        _frameStopwatch.Restart();

        if (elapsedMs > 0.5 && elapsedMs < 1000.0)
        {
            _lastFrameMs = elapsedMs;
            _recentFrameDurations.Enqueue(elapsedMs);
            while (_recentFrameDurations.Count > 120)
            {
                _recentFrameDurations.Dequeue();
            }
        }

        _frameCount++;
        if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
        {
            double seconds = _fpsStopwatch.ElapsedMilliseconds / 1000.0;
            _lastFps = (int)Math.Round(_frameCount / seconds);

            if (_recentFrameDurations.Count >= 10)
            {
                var sorted = _recentFrameDurations.OrderBy(t => t).ToList();
                int idx99 = Math.Clamp((int)(sorted.Count * 0.99), 0, sorted.Count - 1);
                double p99FrameTime = sorted[idx99];
                _lastLow1PctFps = p99FrameTime > 0 ? (int)Math.Round(1000.0 / p99FrameTime) : _lastFps;
            }
            else
            {
                _lastLow1PctFps = _lastFps;
            }

            _frameCount = 0;
            _fpsStopwatch.Restart();

            UpdateFpsDisplay();
        }
    }

    private void UpdateFpsDisplay()
    {
        // 1. Bar Mode
        BarFpsText.Text = $"{_lastFps} FPS";
        BarLow1PctText.Text = $"{_lastLow1PctFps} 1%LOW";
        BarMsDelayText.Text = $"{_lastFrameMs:F1} ms";

        // 2. Card Mode
        FpsCounterText.Text = $"{_lastFps} FPS";
        CardLow1PctText.Text = $"{_lastLow1PctFps} 1%LOW";
        CardMsDelayText.Text = $"{_lastFrameMs:F1} ms";

        // 3. Badge Mode
        BadgeFpsText.Text = $"{_lastFps} FPS";
        BadgeDelayText.Text = $"{_lastFrameMs:F1}ms";
    }

    private async Task RefreshOverlayMetricsAsync()
    {
        try
        {
            var snap = await _perfService.GetMetricsSnapshotAsync();
            UpdateUiWithMetrics(snap);
        }
        catch { }
    }

    private void UpdateUiWithMetrics(SystemMetricsSnapshot snap)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            string api = string.IsNullOrWhiteSpace(snap.ActiveGraphicsApi) ? "DX12" : snap.ActiveGraphicsApi;
            string appName = string.IsNullOrWhiteSpace(snap.ForegroundAppName) ? "DESKTOP" : snap.ForegroundAppName.ToUpperInvariant();

            // ── Bar View Updates ──
            BarApiText.Text = api;
            BarCpuText.Text = $"CPU {snap.CpuUsagePercent:F0}%";
            BarGpuText.Text = snap.GpuMemoryUsedMb > 0
                ? $"GPU {snap.GpuUsagePercent:F0}% · {snap.DisplayGpuShortVram}"
                : $"GPU {snap.GpuUsagePercent:F0}%";
            BarRamText.Text = $"RAM {snap.RamUsagePercent:F0}%";
            BarNetText.Text = snap.NetDownloadRateDisplay;
            BarAppNameText.Text = appName;

            // ── Card View Updates ──
            GraphicsApiText.Text = api;
            HudSubtitleText.Text = appName;

            CpuProgress.Value = snap.CpuUsagePercent;
            CpuText.Text = snap.DisplayCpuPercent;
            CompactCpuText.Text = $"{snap.CpuUsagePercent:F0}%";

            GpuProgress.Value = snap.GpuUsagePercent;
            GpuText.Text = snap.GpuMemoryTotalMb > 0 || snap.GpuMemoryUsedMb > 0
                ? $"{snap.GpuUsagePercent:F0}% · {snap.DisplayGpuShortVram}"
                : snap.DisplayGpuPercent;
            CompactGpuText.Text = $"{snap.GpuUsagePercent:F0}% ({api})";
            GpuModelFooterText.Text = $"🎮 {snap.GpuModelName}";

            RamProgress.Value = snap.RamUsagePercent;
            RamLabelText.Text = !string.IsNullOrWhiteSpace(snap.RamMemoryType) && !snap.RamMemoryType.Contains('/')
                ? snap.RamMemoryType
                : "RAM";
            RamText.Text = snap.TotalRamMb > 0
                ? $"{snap.RamUsagePercent:F0}% · {snap.UsedRamMb / 1024.0:F1}G"
                : snap.DisplayRamPercent;
            CompactRamText.Text = $"{snap.RamUsagePercent:F0}%";
            RamSpecFooterText.Text = snap.DisplayRamTypeAndSpeed;

            NetDownText.Text = $"⬇ {snap.NetDownloadRateDisplay}";
            NetUpText.Text = $"⬆ {snap.NetUploadRateDisplay}";
            NetTotalText.Text = snap.NetAdapterSpeedDisplay;
            CompactNetText.Text = snap.NetDownloadRateDisplay;

            // ── Badge View Updates ──
            BadgeApiText.Text = api;
            BadgeCpuText.Text = $"⚡ {snap.CpuUsagePercent:F0}%";
            BadgeGpuText.Text = $"🎮 {snap.GpuUsagePercent:F0}%";
        });
    }

    // ═══ Smooth Mouse Drag & Edge Clamping ═══

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private bool _isDragging;
    private POINT _dragStartCursor;
    private Windows.Graphics.PointInt32 _windowStartPos;

    private void HudContainer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            DependencyObject? curr = dep;
            while (curr != null && curr != HudContainer)
            {
                if (curr is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase ||
                    curr is Microsoft.UI.Xaml.Controls.Primitives.RangeBase ||
                    curr is ToggleSwitch ||
                    curr is ComboBox)
                {
                    return;
                }
                curr = VisualTreeHelper.GetParent(curr);
            }
        }

        var pt = e.GetCurrentPoint(HudContainer);
        if (pt.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            GetCursorPos(out _dragStartCursor);

            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            _windowStartPos = appWindow.Position;

            HudContainer.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void HudContainer_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            GetCursorPos(out POINT curCursor);
            int deltaX = curCursor.X - _dragStartCursor.X;
            int deltaY = curCursor.Y - _dragStartCursor.Y;

            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            int targetX = _windowStartPos.X + deltaX;
            int targetY = _windowStartPos.Y + deltaY;

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            int maxX = Math.Max(workArea.X, workArea.X + workArea.Width - appWindow.Size.Width);
            int maxY = Math.Max(workArea.Y, workArea.Y + workArea.Height - appWindow.Size.Height);

            int clampedX = Math.Clamp(targetX, workArea.X, maxX);
            int clampedY = Math.Clamp(targetY, workArea.Y, maxY);

            appWindow.Move(new Windows.Graphics.PointInt32(clampedX, clampedY));
            e.Handled = true;
        }
    }

    private void HudContainer_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            HudContainer.ReleasePointerCapture(e.Pointer);

            try
            {
                var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                var s = SettingsService.Instance.Current;
                s.HudCustomPositionX = appWindow.Position.X;
                s.HudCustomPositionY = appWindow.Position.Y;
                SettingsService.Instance.Save();
            }
            catch { }
            e.Handled = true;
        }
    }

    private void HudContainer_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
    }

    // ═══ Style & Settings Handlers ═══

    private void HudStyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (HudStyleCombo.SelectedItem is ComboBoxItem item && item.Tag is string style)
        {
            var s = SettingsService.Instance.Current;
            s.HudStyle = style;
            SettingsService.Instance.Save();
            ApplyHudStyle(style);
            DebugLogService.Instance.LogAction("效能監控 (Performance)", $"切換 HUD 風格為 [{style}]");
        }
    }

    private void HudOpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_isInitialized) return;
        double val = e.NewValue / 100.0;
        ApplyOpacity(val);
        var s = SettingsService.Instance.Current;
        if (Math.Abs(s.HudOpacity - val) > 0.01)
        {
            s.HudOpacity = val;
            SettingsService.Instance.Save();
        }
    }

    private void MetricToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        var s = SettingsService.Instance.Current;
        if (ToggleFps != null) s.HudShowFps = ToggleFps.IsOn;
        if (ToggleLow1Pct != null) s.HudShowLow1Percent = ToggleLow1Pct.IsOn;
        if (ToggleMsDelay != null) s.HudShowMsDelay = ToggleMsDelay.IsOn;
        if (ToggleCpu != null) s.HudShowCpu = ToggleCpu.IsOn;
        if (ToggleGpu != null) s.HudShowGpu = ToggleGpu.IsOn;
        if (ToggleRam != null) s.HudShowRam = ToggleRam.IsOn;
        if (ToggleNet != null) s.HudShowNet = ToggleNet.IsOn;
        if (ToggleApi != null) s.HudShowApi = ToggleApi.IsOn;
        if (ToggleAppName != null) s.HudShowAppName = ToggleAppName.IsOn;

        SettingsService.Instance.Save();
        ApplyMetricVisibility();
        DebugLogService.Instance.LogAction("效能監控 (Performance)", "變更 FPS HUD 顯示指標開關");
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem)
        {
            HudOptionsFlyout.ShowAt(elem);
        }
        else
        {
            HudOptionsFlyout.ShowAt(HudContainer);
        }
    }

    private void CompactToggle_Click(object sender, RoutedEventArgs e)
    {
        _isCompact = !_isCompact;

        if (_isCompact)
        {
            ExpandedViewPanel.Visibility = Visibility.Collapsed;
            CompactViewPanel.Visibility = Visibility.Visible;
            CompactToggleIcon.Text = "⊞";
        }
        else
        {
            CompactViewPanel.Visibility = Visibility.Collapsed;
            ExpandedViewPanel.Visibility = Visibility.Visible;
            CompactToggleIcon.Text = "⊟";
        }

        ApplyDpiAwareWindowSize(repositionTopRight: false);
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    public static void Toggle()
    {
        if (CurrentInstance != null)
        {
            try
            {
                CurrentInstance.Close();
                DebugLogService.Instance.LogAction("效能監控 (Performance)", "關閉 FPS HUD 懸浮窗");
            }
            catch { }
            CurrentInstance = null;
        }
        else
        {
            try
            {
                var overlay = new PerformanceOverlayWindow();
                overlay.Activate();
                DebugLogService.Instance.LogAction("效能監控 (Performance)", "開啟 FPS HUD 懸浮窗成功");
            }
            catch (Exception ex)
            {
                GlobalExceptionHandler.ReportException(ex, "效能懸浮窗啟動 (HUD Launch)");
                DebugLogService.Instance.Error("效能懸浮窗啟動失敗", ex, "SystemPerformance");
            }
        }
    }
}
