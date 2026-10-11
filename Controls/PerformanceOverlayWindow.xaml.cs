using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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

            // Position at Top-Right of Primary Monitor
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            int screenWidth = displayArea.WorkArea.Width;
            int initialWidth = 280;
            int initialHeight = 168;
            int initialX = displayArea.WorkArea.X + screenWidth - initialWidth - 24;
            int initialY = displayArea.WorkArea.Y + 36;

            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(initialX, initialY, initialWidth, initialHeight));
        }
        catch { }
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
            // CPU
            CpuProgress.Value = snap.CpuUsagePercent;
            CpuText.Text = snap.DisplayCpuPercent;
            CompactCpuText.Text = $"{snap.CpuUsagePercent:F0}%";

            // GPU
            GpuProgress.Value = snap.GpuUsagePercent;
            GpuText.Text = snap.DisplayGpuPercent;
            CompactGpuText.Text = $"{snap.GpuUsagePercent:F0}%";

            // RAM
            RamProgress.Value = snap.RamUsagePercent;
            RamText.Text = snap.DisplayRamPercent;
            CompactRamText.Text = $"{snap.RamUsagePercent:F0}%";

            // LAN
            NetDownText.Text = $"⬇ {snap.NetDownloadRateDisplay}";
            NetUpText.Text = $"⬆ {snap.NetUploadRateDisplay}";
            NetTotalText.Text = snap.NetAdapterSpeedDisplay;
            CompactNetText.Text = snap.NetDownloadRateDisplay;
        });
    }

    private void DragRegion_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
            ReleaseCapture();
            SendMessage(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }
    }

    private void CompactToggle_Click(object sender, RoutedEventArgs e)
    {
        _isCompact = !_isCompact;

        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (_isCompact)
        {
            ExpandedViewPanel.Visibility = Visibility.Collapsed;
            CompactViewPanel.Visibility = Visibility.Visible;
            CompactToggleIcon.Text = "⊞";
            appWindow.Resize(new Windows.Graphics.SizeInt32(350, 48));
        }
        else
        {
            CompactViewPanel.Visibility = Visibility.Collapsed;
            ExpandedViewPanel.Visibility = Visibility.Visible;
            CompactToggleIcon.Text = "⊟";
            appWindow.Resize(new Windows.Graphics.SizeInt32(280, 168));
        }
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
