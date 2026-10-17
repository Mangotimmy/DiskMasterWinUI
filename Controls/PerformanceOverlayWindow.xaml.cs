using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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
    private int _frameCount = 0;
    private bool _isCompact = false;
    private IntPtr _hwnd;
    private OverlappedPresenter? _presenter;

    // Logical DIP dimensions (scaled by physical monitor DPI so high-DPI screens never clip)
    private const int ExpandedLogicalWidth = 360;
    private const int ExpandedLogicalHeight = 236;
    private const int CompactLogicalWidth = 420;
    private const int CompactLogicalHeight = 72;

    /// <summary>
    /// Computes the physical pixel client dimensions for the FPS HUD window, honoring both minimum
    /// logical DIP bounds and actual measured XAML content size scaled by the monitor's DPI factor.
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

    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 0x0002;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public PerformanceOverlayWindow()
    {
        InitializeComponent();
        CurrentInstance = this;

        _hwnd = WindowNative.GetWindowHandle(this);
        ConfigureAlwaysOnTopWindow();

        // 1. Live Render FPS Tracking
        _fpsStopwatch.Start();
        CompositionTarget.Rendering += OnCompositionRendering;

        // 2. Metrics Update Timer (1.2s cadence)
        _metricsTimer.Interval = TimeSpan.FromSeconds(1.2);
        _metricsTimer.Tick += async (_, _) => await RefreshOverlayMetricsAsync();
        _metricsTimer.Start();

        // Immediate first sample
        _ = RefreshOverlayMetricsAsync();

        this.Closed += (_, _) =>
        {
            _metricsTimer.Stop();
            _fpsStopwatch.Stop();
            CompositionTarget.Rendering -= OnCompositionRendering;
            if (ReferenceEquals(CurrentInstance, this))
            {
                CurrentInstance = null;
            }
        };
    }

    private void OverlayRoot_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyDpiAwareWindowSize(repositionTopRight: false);
        if (OverlayRoot.XamlRoot != null)
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

            ApplyDpiAwareWindowSize(repositionTopRight: true);
        }
        catch { }
    }

    private void ApplyDpiAwareWindowSize(bool repositionTopRight)
    {
        try
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            double dpiScale = GetCurrentDpiScale();

            double measuredW = 0;
            double measuredH = 0;
            if (HudContainer != null && HudContainer.IsLoaded)
            {
                double constraintW = _isCompact ? CompactLogicalWidth : ExpandedLogicalWidth;
                HudContainer.Measure(new Windows.Foundation.Size(constraintW, double.PositiveInfinity));
                measuredW = HudContainer.DesiredSize.Width;
                measuredH = HudContainer.DesiredSize.Height;
            }

            var clientSize = CalculateDpiAwareHudClientSize(_isCompact, dpiScale, measuredW, measuredH);

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

        // Snap to edges if within 20 physical pixels of monitor border
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
        _frameCount++;
        if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
        {
            double seconds = _fpsStopwatch.ElapsedMilliseconds / 1000.0;
            int currentFps = (int)Math.Round(_frameCount / seconds);
            FpsCounterText.Text = $"{currentFps} FPS";

            _frameCount = 0;
            _fpsStopwatch.Restart();
        }
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
            // Active Graphics API Badge (DX12 / DX11 / Vulkan / OpenGL)
            string api = string.IsNullOrWhiteSpace(snap.ActiveGraphicsApi) ? "DX12" : snap.ActiveGraphicsApi;
            GraphicsApiText.Text = api;
            HudSubtitleText.Text = string.IsNullOrWhiteSpace(snap.ForegroundAppName) ? "PERF HUD" : snap.ForegroundAppName.ToUpperInvariant();

            // CPU
            CpuProgress.Value = snap.CpuUsagePercent;
            CpuText.Text = snap.DisplayCpuPercent;
            CompactCpuText.Text = $"{snap.CpuUsagePercent:F0}%";

            // GPU (Usage % + Dedicated VRAM)
            GpuProgress.Value = snap.GpuUsagePercent;
            GpuText.Text = snap.GpuMemoryTotalMb > 0 || snap.GpuMemoryUsedMb > 0
                ? $"{snap.GpuUsagePercent:F0}% · {snap.DisplayGpuShortVram}"
                : snap.DisplayGpuPercent;
            CompactGpuText.Text = $"{snap.GpuUsagePercent:F0}% ({api})";
            GpuModelFooterText.Text = $"🎮 {snap.GpuModelName}";

            // RAM (Usage % + DDR4/DDR5 & MHz)
            RamProgress.Value = snap.RamUsagePercent;
            RamLabelText.Text = !string.IsNullOrWhiteSpace(snap.RamMemoryType) && !snap.RamMemoryType.Contains('/')
                ? snap.RamMemoryType
                : "RAM";
            RamText.Text = snap.TotalRamMb > 0
                ? $"{snap.RamUsagePercent:F0}% · {snap.UsedRamMb / 1024.0:F1}G"
                : snap.DisplayRamPercent;
            CompactRamText.Text = $"{snap.RamUsagePercent:F0}%";
            RamSpecFooterText.Text = snap.DisplayRamTypeAndSpeed;

            // LAN
            NetDownText.Text = $"⬇ {snap.NetDownloadRateDisplay}";
            NetUpText.Text = $"⬆ {snap.NetUploadRateDisplay}";
            NetTotalText.Text = snap.NetAdapterSpeedDisplay;
            CompactNetText.Text = snap.NetDownloadRateDisplay;
        });
    }

    private void HudContainer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            DependencyObject? curr = dep;
            while (curr != null)
            {
                if (curr is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
                    return;
                curr = VisualTreeHelper.GetParent(curr);
            }
        }

        if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
            ReleaseCapture();
            SendMessage(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);

            try
            {
                var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                ClampWindowToMonitorWorkArea(appWindow, displayArea.WorkArea, appWindow.Size.Width, appWindow.Size.Height, GetCurrentDpiScale());
            }
            catch { }
        }
    }

    private void DragRegion_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        HudContainer_PointerPressed(sender, e);
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
            CurrentInstance.Close();
            CurrentInstance = null;
        }
        else
        {
            var overlay = new PerformanceOverlayWindow();
            overlay.Activate();
        }
    }
}
