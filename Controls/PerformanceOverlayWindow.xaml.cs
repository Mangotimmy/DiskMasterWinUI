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
    public static event Action? OverlayStateChanged;

    public bool IsClickThrough { get; private set; }
    public bool IsBorderRemoved { get; private set; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_LAYERED = 0x00080000;

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

    // ── Settings-sync & window-application guards ──
    // Applying a setting must never write the settings file again, otherwise
    // Save -> SettingsChanged -> LoadSavedSettings -> Save forms an infinite loop
    // that starves the UI thread (the historical "Not Responding" bug).
    private bool _isApplyingHudSettings;
    private string _lastAppliedHudSignature = "";
    private string _currentHudStyle = "";
    private string _currentHudTheme = "";
    private double _currentHudOpacity = -1;
    private bool? _lastNativeClickThrough;
    private bool _layoutDirty = true;
    private int _lastClientWidth;
    private int _lastClientHeight;
    private double _lastDpiScale;
    private int _appliedClientWidth;
    private int _appliedClientHeight;
    private bool _isRootWatcherAttached;

    // Guards the programmatic ToggleMenuFlyoutItem.IsChecked updates. Without this,
    // assigning IsChecked can re-enter the menu Click handlers, which save settings,
    // which raises SettingsChanged, which calls LoadSavedSettings again: a UI-thread
    // starvation loop that historically produced Application Hang events.
    private bool _isUpdatingMenuChecks;
    private SolidColorBrush? _themedBorderBrush;
    private long _settingsBurstWindowStartTicks;
    private int _settingsBurstCount;
    private long _settingsBurstSuppressUntilTicks;

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

        // 3. Listen to live settings changes
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;

        // Apply saved HUD settings
        LoadSavedSettings();
        _isInitialized = true;
        ApplyMetricVisibility();

        // Immediate first sample
        _ = RefreshOverlayMetricsAsync();

        OverlayStateChanged?.Invoke();

        this.Closed += (_, _) =>
        {
            _isInitialized = false;
            _metricsTimer.Stop();
            _fpsStopwatch.Stop();
            _frameStopwatch.Stop();
            CompositionTarget.Rendering -= OnCompositionRendering;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            HudSettingsWindow.CurrentInstance?.Close();
            if (ReferenceEquals(CurrentInstance, this))
            {
                CurrentInstance = null;
            }
            OverlayStateChanged?.Invoke();
        };
    }

    private void OnSettingsChanged()
    {
        if (_isApplyingHudSettings || !_isInitialized) return;

        // Circuit breaker: a menu Click handler that saves from inside ApplyXxx can
        // otherwise raise SettingsChanged hundreds of times per second. Even one
        // such path starves the UI thread and produces Application Hang events.
        long now = Environment.TickCount64;
        if (_settingsBurstSuppressUntilTicks > now) return;
        if (now - _settingsBurstWindowStartTicks > 1000)
        {
            _settingsBurstWindowStartTicks = now;
            _settingsBurstCount = 0;
        }
        if (++_settingsBurstCount > 20)
        {
            _settingsBurstSuppressUntilTicks = now + 2000;
            DebugLogService.Instance.Error(
                $"HUD 設定事件暴衝: 1 秒內收到 {_settingsBurstCount} 次 SettingsChanged，已暫停套用 2 秒以防 UI 執行緒飢餓。", null, "PerformanceOverlay");
            return;
        }

        var signature = ComputeHudSettingsSignature();
        if (string.Equals(signature, _lastAppliedHudSignature, StringComparison.Ordinal)) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isInitialized || _isApplyingHudSettings) return;
            if (string.Equals(ComputeHudSettingsSignature(), _lastAppliedHudSignature, StringComparison.Ordinal)) return;
            LoadSavedSettings();
        });
    }

    private static string ComputeHudSettingsSignature()
    {
        var s = SettingsService.Instance.Current;
        return string.Join("|",
            s.HudStyle ?? "Bar",
            s.HudTheme ?? "Cyberpunk",
            s.HudOpacity.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            s.HudShowFps, s.HudShowLow1Percent, s.HudShowMsDelay, s.HudShowCpu,
            s.HudShowGpu, s.HudShowRam, s.HudShowNet, s.HudShowApi, s.HudShowAppName,
            s.HudClickThrough, s.HudRemoveBorder,
            s.HudCustomPositionX, s.HudCustomPositionY);
    }

    private void LoadSavedSettings()
    {
        if (_isApplyingHudSettings) return;
        _isApplyingHudSettings = true;
        try
        {
            var s = SettingsService.Instance.Current;

            // Opacity & Theme
            double op = s.HudOpacity > 0 ? s.HudOpacity : 0.85;
            string theme = string.IsNullOrEmpty(s.HudTheme) ? "Cyberpunk" : s.HudTheme;
            ApplyColorTheme(theme);

            // Style (Default is "Bar")
            string style = string.IsNullOrEmpty(s.HudStyle) ? "Bar" : s.HudStyle;
            ApplyHudStyle(style);

            ApplyMetricVisibility();

            ApplyClickThrough(s.HudClickThrough);
            ApplyBorderVisibility(s.HudRemoveBorder);

            UpdateMenuCheckmarks(theme, style, op);
            _lastAppliedHudSignature = ComputeHudSettingsSignature();
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Error("套用 HUD 設定失敗", ex, "PerformanceOverlay");
        }
        finally
        {
            _isApplyingHudSettings = false;
        }
    }

    public void ApplyClickThrough(bool enable)
    {
        bool needsNativeApply = !_lastNativeClickThrough.HasValue || _lastNativeClickThrough.Value != enable;
        IsClickThrough = enable;

        if (needsNativeApply && _hwnd != IntPtr.Zero)
        {
            try
            {
                var curExStyle = (uint)GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
                // CRITICAL FIX: NEVER set WS_EX_LAYERED on a WinUI 3 window!
                // WinUI 3 uses DirectComposition. Setting WS_EX_LAYERED breaks the DirectComposition
                // visual swapchain and causes the entire window to render as a solid pitch-black screen.
                // WS_EX_TRANSPARENT alone correctly passes mouse messages to the underlying window.
                var newExStyle = enable
                    ? (curExStyle | WS_EX_TRANSPARENT) & ~WS_EX_LAYERED
                    : (curExStyle & ~(WS_EX_TRANSPARENT | WS_EX_LAYERED));
                if (newExStyle != curExStyle)
                {
                    SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)newExStyle);
                    SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
                }
                _lastNativeClickThrough = enable;
            }
            catch (Exception ex)
            {
                DebugLogService.Instance.Error("設定 HUD 滑鼠穿透失敗", ex, "PerformanceOverlay");
            }
        }

        if (HudContainer != null)
        {
            HudContainer.IsHitTestVisible = !enable;
        }

        if (BarClickThroughBadge != null) BarClickThroughBadge.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
        if (CardClickThroughBadge != null) CardClickThroughBadge.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
        if (BadgeClickThroughBadge != null) BadgeClickThroughBadge.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;

        // Do not touch the flyout checkmark here: LoadSavedSettings/UpdateMenuCheckmarks is the
        // single guarded path that updates ToggleMenuFlyoutItem.IsChecked. Assigning it inside an
        // apply method was one of the paths that could re-enter the menu Click handlers.
        OverlayStateChanged?.Invoke();
    }

    public void ApplyBorderVisibility(bool removeBorder)
    {
        IsBorderRemoved = removeBorder;
        if (HudContainer != null)
        {
            var thickness = removeBorder ? new Thickness(0) : new Thickness(1.5);
            if (HudContainer.BorderThickness != thickness)
            {
                HudContainer.BorderThickness = thickness;
            }

            // A transparent brush guarantees no faint line remains at fractional DPI even if
            // a theme re-apply races with this method. Restoring uses the themed brush captured
            // by ApplyColorTheme.
            HudContainer.BorderBrush = removeBorder
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
                : (_themedBorderBrush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(140, 56, 189, 248)));

            // BorderThickness participates in measure; without this the window keeps its old
            // footprint and the toggle looks like it did nothing.
            _layoutDirty = true;
            ApplyDpiAwareWindowSize(repositionTopRight: false);
        }

        // The flyout checkmark is updated through the guarded UpdateMenuCheckmarks path.
        OverlayStateChanged?.Invoke();
    }

    /// <summary>Public entry point used by HudSettingsWindow for immediate metric updates.</summary>
    public void ApplyMetricSettings() => ApplyMetricVisibility();

    public void ApplyColorTheme(string theme)
    {
        if (HudContainer == null) return;
        var s = SettingsService.Instance.Current;
        double op = s.HudOpacity > 0 ? s.HudOpacity : 0.85;
        string effectiveTheme = string.IsNullOrEmpty(theme) ? "Cyberpunk" : theme;

        if (string.Equals(_currentHudTheme, effectiveTheme, StringComparison.Ordinal) &&
            Math.Abs(_currentHudOpacity - op) < 0.001)
        {
            UpdateMenuCheckmarks(effectiveTheme, s.HudStyle ?? "Bar", op);
            return;
        }

        _currentHudTheme = effectiveTheme;
        _currentHudOpacity = op;

        byte alpha = (byte)Math.Clamp((int)Math.Round(op * 255), 0, 255);
        // Decouple the glow edge from the background opacity. At the persisted 16% opacity
        // the old min(alpha, 220) border was only 16% opaque, so removing it was imperceptible.
        byte borderAlpha = (byte)Math.Clamp((int)Math.Round((double)Math.Max((int)alpha, 140)), 0, 220);

        switch (effectiveTheme)
        {
            case "Emerald":
                HudContainer.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, 6, 22, 14));
                _themedBorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(borderAlpha, 16, 185, 129));
                break;
            case "Ocean":
                HudContainer.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, 10, 22, 40));
                _themedBorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(borderAlpha, 59, 130, 246));
                break;
            case "Solar":
                HudContainer.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, 28, 14, 10));
                _themedBorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(borderAlpha, 245, 158, 11));
                break;
            case "Midnight":
                HudContainer.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, 12, 14, 18));
                _themedBorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(borderAlpha, 71, 85, 105));
                break;
            case "Cyberpunk":
            default:
                HudContainer.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, 11, 15, 25));
                _themedBorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(borderAlpha, 56, 189, 248));
                break;
        }

        HudContainer.BorderThickness = IsBorderRemoved ? new Thickness(0) : new Thickness(1.5);
        HudContainer.BorderBrush = IsBorderRemoved
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
            : _themedBorderBrush;

        UpdateMenuCheckmarks(effectiveTheme, s.HudStyle ?? "Bar", op);
    }

    private void ApplyOpacity(double opacity)
    {
        opacity = Math.Clamp(opacity, 0.05, 1.0);
        if (HudContainer != null)
        {
            HudContainer.Opacity = 1.0;
            var s = SettingsService.Instance.Current;
            ApplyColorTheme(string.IsNullOrEmpty(s.HudTheme) ? "Cyberpunk" : s.HudTheme);
        }
    }

    private void ApplyHudStyle(string style)
    {
        style = string.IsNullOrEmpty(style) ? "Bar" : style;
        if (!string.Equals(_currentHudStyle, style, StringComparison.Ordinal))
        {
            _currentHudStyle = style;
            if (BarViewPanel != null) BarViewPanel.Visibility = (style == "Bar") ? Visibility.Visible : Visibility.Collapsed;
            if (CardViewPanel != null) CardViewPanel.Visibility = (style == "Card") ? Visibility.Visible : Visibility.Collapsed;
            if (BadgeViewPanel != null) BadgeViewPanel.Visibility = (style == "Badge") ? Visibility.Visible : Visibility.Collapsed;
            _layoutDirty = true;
        }

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

        // Badge view items
        if (BadgeFpsText != null) BadgeFpsText.Visibility = s.HudShowFps ? Visibility.Visible : Visibility.Collapsed;
        if (BadgeDelayText != null) BadgeDelayText.Visibility = s.HudShowMsDelay ? Visibility.Visible : Visibility.Collapsed;
        if (BadgeApiText != null) BadgeApiText.Visibility = s.HudShowApi ? Visibility.Visible : Visibility.Collapsed;
        if (BadgeCpuText != null) BadgeCpuText.Visibility = s.HudShowCpu ? Visibility.Visible : Visibility.Collapsed;
        if (BadgeGpuText != null) BadgeGpuText.Visibility = s.HudShowGpu ? Visibility.Visible : Visibility.Collapsed;

        if (BadgeSepDelay != null) BadgeSepDelay.Visibility = (s.HudShowFps && s.HudShowMsDelay) ? Visibility.Visible : Visibility.Collapsed;
        if (BadgeSepApi != null) BadgeSepApi.Visibility = ((s.HudShowFps || s.HudShowMsDelay) && s.HudShowApi) ? Visibility.Visible : Visibility.Collapsed;
        if (BadgeSepCpu != null) BadgeSepCpu.Visibility = ((s.HudShowFps || s.HudShowMsDelay || s.HudShowApi) && s.HudShowCpu) ? Visibility.Visible : Visibility.Collapsed;
        if (BadgeSepGpu != null) BadgeSepGpu.Visibility = ((s.HudShowFps || s.HudShowMsDelay || s.HudShowApi || s.HudShowCpu) && s.HudShowGpu) ? Visibility.Visible : Visibility.Collapsed;

        _layoutDirty = true;
        ApplyDpiAwareWindowSize(repositionTopRight: false);
    }

    private void OverlayRoot_Loaded(object sender, RoutedEventArgs e)
    {
        // The first real measure is only possible once the HUD content is loaded.
        // Mark the layout dirty so the Bar/Card/Badge window width can be sized to content exactly once.
        _layoutDirty = true;
        ApplyDpiAwareWindowSize(repositionTopRight: false);
        if (!_isRootWatcherAttached && OverlayRoot?.XamlRoot != null)
        {
            _isRootWatcherAttached = true;
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

            // 1. Strip WS_EX_LAYERED if present, which corrupts DirectComposition into solid black screen
            var curExStyle = (uint)GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            if ((curExStyle & WS_EX_LAYERED) != 0)
            {
                SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)(curExStyle & ~WS_EX_LAYERED));
            }

            // 2. Enable immersive dark mode and rounded corners via DWM
            int darkMode = 1;
            DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            int cornerPref = DWMWCP_ROUND;
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            // 3. Translucency is provided by the XAML DesktopAcrylicBackdrop.
            // DwmExtendFrameIntoClientArea must NOT be used here: it turns the whole XAML
            // client area into DWM glass and the HUD renders as an empty white-bordered box.
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
            if (Math.Abs(_lastDpiScale - dpiScale) > 0.01)
            {
                _lastDpiScale = dpiScale;
                _layoutDirty = true;
            }

            var s = SettingsService.Instance.Current;
            string style = string.IsNullOrEmpty(s.HudStyle) ? "Bar" : s.HudStyle;

            if (_layoutDirty || _lastClientWidth <= 0 || _lastClientHeight <= 0)
            {
                double baseW = style switch
                {
                    "Bar" => 140, // Dynamic width based on active badges; minimum 140 DIPs for controls
                    "Badge" => 100,
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

                double logicalW = Math.Max(baseW, measuredW) + 2;

                // Card mode should hug its measured content. The old Math.Max(baseH, measuredH)
                // forced the 236-DIP card minimum even when the content was ~175 DIP tall, leaving
                // a large empty acrylic area below the footer. Bar/Badge keep their compact minima.
                double minH = style switch
                {
                    "Bar" => BarLogicalHeight,
                    "Badge" => BadgeLogicalHeight,
                    _ => 0
                };
                double logicalH = measuredH > 0 ? Math.Max(minH, measuredH + 2) : baseH;

                _lastClientWidth = (int)Math.Ceiling(logicalW * dpiScale);
                _lastClientHeight = (int)Math.Ceiling(logicalH * dpiScale);
                _layoutDirty = false;
            }

            var clientSize = new Windows.Graphics.SizeInt32(_lastClientWidth, _lastClientHeight);
            if (_appliedClientWidth != clientSize.Width || _appliedClientHeight != clientSize.Height)
            {
                try
                {
                    appWindow.ResizeClient(clientSize);
                }
                catch
                {
                    appWindow.Resize(clientSize);
                }
                _appliedClientWidth = clientSize.Width;
                _appliedClientHeight = clientSize.Height;
            }

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            if (repositionTopRight)
            {
                int marginRight = (int)Math.Round(16 * dpiScale);
                int marginTop = (int)Math.Round(24 * dpiScale);
                int initialX = workArea.X + workArea.Width - clientSize.Width - marginRight;
                int initialY = workArea.Y + marginTop;

                initialX = Math.Clamp(initialX, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - clientSize.Width));
                initialY = Math.Clamp(initialY, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - clientSize.Height));
                MoveWindowIfNeeded(appWindow, initialX, initialY);
            }
            else
            {
                ClampWindowToMonitorWorkArea(appWindow, workArea, clientSize.Width, clientSize.Height, dpiScale);
            }
        }
        catch { }
    }

    private static void MoveWindowIfNeeded(AppWindow appWindow, int x, int y)
    {
        if (appWindow.Position.X != x || appWindow.Position.Y != y)
        {
            appWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
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
            if (!_isInitialized) return;
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
            CompactCpuText.Text = $"CPU {snap.CpuUsagePercent:F0}%";

            GpuProgress.Value = snap.GpuUsagePercent;
            GpuText.Text = snap.GpuMemoryTotalMb > 0 || snap.GpuMemoryUsedMb > 0
                ? $"{snap.GpuUsagePercent:F0}% · {snap.DisplayGpuShortVram}"
                : snap.DisplayGpuPercent;
            CompactGpuText.Text = $"GPU {snap.GpuUsagePercent:F0}% ({api})";
            GpuModelFooterText.Text = snap.GpuModelName;

            RamProgress.Value = snap.RamUsagePercent;
            RamLabelText.Text = !string.IsNullOrWhiteSpace(snap.RamMemoryType) && !snap.RamMemoryType.Contains('/')
                ? snap.RamMemoryType
                : "RAM";
            RamText.Text = snap.TotalRamMb > 0
                ? $"{snap.RamUsagePercent:F0}% · {snap.UsedRamMb / 1024.0:F1}G"
                : snap.DisplayRamPercent;
            CompactRamText.Text = $"RAM {snap.RamUsagePercent:F0}%";
            RamSpecFooterText.Text = snap.DisplayRamTypeAndSpeed;

            NetDownText.Text = snap.NetDownloadRateDisplay;
            NetUpText.Text = snap.NetUploadRateDisplay;
            NetTotalText.Text = snap.NetAdapterSpeedDisplay;
            CompactNetText.Text = snap.NetDownloadRateDisplay;

            // ── Badge View Updates ──
            BadgeApiText.Text = api;
            BadgeCpuText.Text = $"CPU {snap.CpuUsagePercent:F0}%";
            BadgeGpuText.Text = $"GPU {snap.GpuUsagePercent:F0}%";
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

    // ═══ Style & Theme Menu Handlers ═══

    private void UpdateMenuCheckmarks(string theme, string style, double op)
    {
        if (_isUpdatingMenuChecks) return;

        _isUpdatingMenuChecks = true;
        try
        {
            SetMenuChecked(ThemeCyberpunkItem, theme == "Cyberpunk");
            SetMenuChecked(ThemeEmeraldItem, theme == "Emerald");
            SetMenuChecked(ThemeOceanItem, theme == "Ocean");
            SetMenuChecked(ThemeSolarItem, theme == "Solar");
            SetMenuChecked(ThemeMidnightItem, theme == "Midnight");

            SetMenuChecked(StyleBarItem, style == "Bar");
            SetMenuChecked(StyleCardItem, style == "Card");
            SetMenuChecked(StyleBadgeItem, style == "Badge");

            SetMenuChecked(Opacity100Item, Math.Abs(op - 1.0) < 0.05);
            SetMenuChecked(Opacity85Item, Math.Abs(op - 0.85) < 0.05);
            SetMenuChecked(Opacity70Item, Math.Abs(op - 0.70) < 0.05);
            SetMenuChecked(Opacity50Item, Math.Abs(op - 0.50) < 0.05);

            SetMenuChecked(ClickThroughMenuItem, IsClickThrough);
            SetMenuChecked(RemoveBorderMenuItem, IsBorderRemoved);
        }
        finally
        {
            _isUpdatingMenuChecks = false;
        }
    }

    private static void SetMenuChecked(ToggleMenuFlyoutItem? item, bool value)
    {
        if (item == null || item.IsChecked == value) return;
        item.IsChecked = value;
    }

    private void ClickThroughMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingHudSettings || _isUpdatingMenuChecks) return;
        bool enable = !IsClickThrough;
        PersistHudSetting(s => s.HudClickThrough = enable);
        ApplyClickThrough(enable);
        DebugLogService.Instance.LogAction("效能監控 (Performance)", enable
            ? "已啟用 HUD 滑鼠穿透模式 (滑鼠忽略 HUD，直接點擊操作背景程式)"
            : "已停用 HUD 滑鼠穿透模式 (恢復正常滑鼠互動與拖曳)");
    }

    private void RemoveBorderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingHudSettings || _isUpdatingMenuChecks) return;
        bool removeBorder = !IsBorderRemoved;
        PersistHudSetting(s => s.HudRemoveBorder = removeBorder);
        ApplyBorderVisibility(removeBorder);
        DebugLogService.Instance.LogAction("效能監控 (Performance)", removeBorder
            ? "已移除 HUD 外邊框 (無邊框極簡模式)"
            : "已恢復 HUD 發光外邊框");
    }

    public static void ToggleClickThroughGlobal()
    {
        if (CurrentInstance == null)
        {
            Toggle();
            if (CurrentInstance != null)
            {
                PersistHudSetting(s => s.HudClickThrough = true);
                CurrentInstance.ApplyClickThrough(true);
                DebugLogService.Instance.LogAction("效能監控 (Performance)", "已啟用 HUD 滑鼠穿透模式 (滑鼠忽略 HUD，直接點擊操作背景程式)");
            }
        }
        else
        {
            bool enable = !CurrentInstance.IsClickThrough;
            PersistHudSetting(s => s.HudClickThrough = enable);
            CurrentInstance.ApplyClickThrough(enable);
            DebugLogService.Instance.LogAction("效能監控 (Performance)", enable
                ? "已啟用 HUD 滑鼠穿透模式 (滑鼠忽略 HUD，直接點擊操作背景程式)"
                : "已停用 HUD 滑鼠穿透模式 (恢復正常滑鼠互動與拖曳)");
        }
    }

    private static void PersistHudSetting(Action<AppSettings> mutate)
    {
        var settings = SettingsService.Instance.Current;
        mutate(settings);
        SettingsService.Instance.Save();
    }

    private void HudThemeMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingHudSettings || _isUpdatingMenuChecks) return;
        if (sender is MenuFlyoutItem item && item.Tag is string theme)
        {
            var s = SettingsService.Instance.Current;
            s.HudTheme = theme;
            SettingsService.Instance.Save();
            ApplyColorTheme(theme);
            DebugLogService.Instance.LogAction("效能監控 (Performance)", $"切換 HUD 主題色彩為 [{theme}]");
        }
    }

    private void HudStyleMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingHudSettings || _isUpdatingMenuChecks) return;
        if (sender is MenuFlyoutItem item && item.Tag is string style)
        {
            var s = SettingsService.Instance.Current;
            s.HudStyle = style;
            SettingsService.Instance.Save();
            ApplyHudStyle(style);
            DebugLogService.Instance.LogAction("效能監控 (Performance)", $"切換 HUD 風格型態為 [{style}]");
        }
    }

    private void HudOpacityMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_isApplyingHudSettings || _isUpdatingMenuChecks) return;
        if (sender is MenuFlyoutItem item && item.Tag is string opStr && double.TryParse(opStr, out double op))
        {
            var s = SettingsService.Instance.Current;
            s.HudOpacity = op;
            SettingsService.Instance.Save();
            ApplyOpacity(op);
            DebugLogService.Instance.LogAction("效能監控 (Performance)", $"變更 HUD 透明度為 [{(int)(op * 100)}%]");
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        HudSettingsWindow.Toggle(this);
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

        _layoutDirty = true;
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
