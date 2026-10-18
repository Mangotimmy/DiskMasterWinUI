using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Services;
using WinRT.Interop;

namespace DiskMasterWinUI.Controls;

public sealed partial class HudSettingsWindow : Window
{
    public static HudSettingsWindow? CurrentInstance { get; private set; }

    private readonly IntPtr _hwnd;
    private OverlappedPresenter? _presenter;
    private bool _isUpdatingUi = false;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_LAYERED = 0x00080000;

    private const int DefaultLogicalWidth = 300;
    private const int DefaultLogicalHeight = 520;

    public static void Toggle(PerformanceOverlayWindow? parent = null)
    {
        if (CurrentInstance != null)
        {
            try
            {
                CurrentInstance.Close();
            }
            catch { }
            CurrentInstance = null;
        }
        else
        {
            try
            {
                var win = new HudSettingsWindow(parent);
                win.Activate();
            }
            catch (Exception ex)
            {
                DebugLogService.Instance.Error("HUD 設定視窗啟動失敗", ex, "SystemPerformance");
            }
        }
    }

    public HudSettingsWindow(PerformanceOverlayWindow? parent = null)
    {
        // The opacity Slider's Minimum value fires ValueChanged during InitializeComponent,
        // before LoadSettings() can populate the real value. Without this pre-guard the handler
        // would persist 5% (the minimum) over the user's saved opacity as soon as this window opens.
        _isUpdatingUi = true;
        InitializeComponent();
        CurrentInstance = this;

        _hwnd = WindowNative.GetWindowHandle(this);
        ConfigureWindow(parent);

        LoadSettings();

        this.Closed += (_, _) =>
        {
            if (ReferenceEquals(CurrentInstance, this))
            {
                CurrentInstance = null;
            }
        };
    }

    private void RootContainer_Loaded(object sender, RoutedEventArgs e)
    {
        if (RootContainer != null)
        {
            RootContainer.KeyDown += (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Escape)
                {
                    this.Close();
                }
            };
        }
    }

    private void ConfigureWindow(PerformanceOverlayWindow? parent)
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

            // Strip WS_EX_LAYERED to prevent DirectComposition black screen
            var curExStyle = (uint)GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            if ((curExStyle & WS_EX_LAYERED) != 0)
            {
                SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)(curExStyle & ~WS_EX_LAYERED));
            }

            int darkMode = 1;
            DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            int cornerPref = DWMWCP_ROUND;
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            double dpiScale = Math.Max(1.0, DisplayHelper.GetDpi(_hwnd) / 96.0);
            int targetW = (int)Math.Ceiling(DefaultLogicalWidth * dpiScale);
            int targetH = (int)Math.Ceiling(DefaultLogicalHeight * dpiScale);
            var clientSize = new Windows.Graphics.SizeInt32(targetW, targetH);
            appWindow.ResizeClient(clientSize);

            // Determine smart position relative to HUD or screen center
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            int targetX;
            int targetY;

            var targetParent = parent ?? PerformanceOverlayWindow.CurrentInstance;
            if (targetParent != null)
            {
                var parentHwnd = WindowNative.GetWindowHandle(targetParent);
                var parentWinId = Win32Interop.GetWindowIdFromWindow(parentHwnd);
                var parentAppWin = AppWindow.GetFromWindowId(parentWinId);

                // Align right edges of HUD and Settings window
                targetX = parentAppWin.Position.X + parentAppWin.Size.Width - targetW;
                targetY = parentAppWin.Position.Y + parentAppWin.Size.Height + (int)Math.Round(8 * dpiScale);

                // If overflowing bottom, place above parent HUD
                if (targetY + targetH > workArea.Y + workArea.Height)
                {
                    targetY = parentAppWin.Position.Y - targetH - (int)Math.Round(8 * dpiScale);
                }
            }
            else
            {
                targetX = workArea.X + workArea.Width - targetW - (int)Math.Round(24 * dpiScale);
                targetY = workArea.Y + (int)Math.Round(80 * dpiScale);
            }

            targetX = Math.Clamp(targetX, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - targetW));
            targetY = Math.Clamp(targetY, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - targetH));
            appWindow.Move(new Windows.Graphics.PointInt32(targetX, targetY));
        }
        catch { }
    }

    private void LoadSettings()
    {
        _isUpdatingUi = true;
        try
        {
            var s = SettingsService.Instance.Current;

            // Opacity
            double op = s.HudOpacity > 0 ? s.HudOpacity : 0.85;
            int opPercent = (int)Math.Round(op * 100);
            if (HudOpacitySlider != null) HudOpacitySlider.Value = opPercent;
            if (OpacityValueText != null) OpacityValueText.Text = $"{opPercent}%";

            // Style
            string style = string.IsNullOrEmpty(s.HudStyle) ? "Bar" : s.HudStyle;
            if (HudStyleCombo != null)
            {
                for (int i = 0; i < HudStyleCombo.Items.Count; i++)
                {
                    if (HudStyleCombo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == style)
                    {
                        HudStyleCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Theme
            string theme = string.IsNullOrEmpty(s.HudTheme) ? "Cyberpunk" : s.HudTheme;
            if (HudThemeCombo != null)
            {
                for (int i = 0; i < HudThemeCombo.Items.Count; i++)
                {
                    if (HudThemeCombo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == theme)
                    {
                        HudThemeCombo.SelectedIndex = i;
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
            if (ToggleClickThrough != null) ToggleClickThrough.IsOn = s.HudClickThrough;
            if (ToggleRemoveBorder != null) ToggleRemoveBorder.IsOn = s.HudRemoveBorder;
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void HudStyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        if (HudStyleCombo.SelectedItem is ComboBoxItem item && item.Tag is string style)
        {
            var s = SettingsService.Instance.Current;
            s.HudStyle = style;
            SettingsService.Instance.Save();
            DebugLogService.Instance.LogAction("效能監控 (Performance)", $"切換 HUD 風格為 [{style}]");
        }
    }

    private void HudThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi) return;
        if (HudThemeCombo?.SelectedItem is ComboBoxItem item && item.Tag is string theme)
        {
            var s = SettingsService.Instance.Current;
            s.HudTheme = theme;
            SettingsService.Instance.Save();
            DebugLogService.Instance.LogAction("效能監控 (Performance)", $"變更 HUD 色彩主題為 [{theme}]");
        }
    }

    private void HudOpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (OpacityValueText != null)
        {
            OpacityValueText.Text = $"{(int)Math.Round(e.NewValue)}%";
        }

        if (_isUpdatingUi) return;
        double val = e.NewValue / 100.0;
        var s = SettingsService.Instance.Current;
        if (Math.Abs(s.HudOpacity - val) > 0.01)
        {
            s.HudOpacity = val;
            SettingsService.Instance.Save();
        }
    }

    private void MetricToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
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
        PerformanceOverlayWindow.CurrentInstance?.ApplyMetricSettings();
        DebugLogService.Instance.LogAction("效能監控 (Performance)", "變更 FPS HUD 顯示指標開關");
    }

    private void PresetMinimal_Click(object sender, RoutedEventArgs e)
    {
        _isUpdatingUi = true;
        try
        {
            var s = SettingsService.Instance.Current;
            s.HudShowFps = true;
            s.HudShowLow1Percent = true;
            s.HudShowMsDelay = true;
            s.HudShowCpu = false;
            s.HudShowGpu = false;
            s.HudShowRam = false;
            s.HudShowNet = false;
            s.HudShowApi = false;
            s.HudShowAppName = false;

            if (ToggleFps != null) ToggleFps.IsOn = true;
            if (ToggleLow1Pct != null) ToggleLow1Pct.IsOn = true;
            if (ToggleMsDelay != null) ToggleMsDelay.IsOn = true;
            if (ToggleCpu != null) ToggleCpu.IsOn = false;
            if (ToggleGpu != null) ToggleGpu.IsOn = false;
            if (ToggleRam != null) ToggleRam.IsOn = false;
            if (ToggleNet != null) ToggleNet.IsOn = false;
            if (ToggleApi != null) ToggleApi.IsOn = false;
            if (ToggleAppName != null) ToggleAppName.IsOn = false;

            SettingsService.Instance.Save();
            DebugLogService.Instance.LogAction("效能監控 (Performance)", "套用 HUD 極簡 FPS 預設方案");
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void PresetStandard_Click(object sender, RoutedEventArgs e)
    {
        _isUpdatingUi = true;
        try
        {
            var s = SettingsService.Instance.Current;
            s.HudShowFps = true;
            s.HudShowLow1Percent = true;
            s.HudShowMsDelay = true;
            s.HudShowApi = true;
            s.HudShowCpu = true;
            s.HudShowGpu = true;
            s.HudShowRam = true;
            s.HudShowNet = false;
            s.HudShowAppName = false;

            if (ToggleFps != null) ToggleFps.IsOn = true;
            if (ToggleLow1Pct != null) ToggleLow1Pct.IsOn = true;
            if (ToggleMsDelay != null) ToggleMsDelay.IsOn = true;
            if (ToggleApi != null) ToggleApi.IsOn = true;
            if (ToggleCpu != null) ToggleCpu.IsOn = true;
            if (ToggleGpu != null) ToggleGpu.IsOn = true;
            if (ToggleRam != null) ToggleRam.IsOn = true;
            if (ToggleNet != null) ToggleNet.IsOn = false;
            if (ToggleAppName != null) ToggleAppName.IsOn = false;

            SettingsService.Instance.Save();
            DebugLogService.Instance.LogAction("效能監控 (Performance)", "套用 HUD 常用監控預設方案");
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void PresetFull_Click(object sender, RoutedEventArgs e)
    {
        _isUpdatingUi = true;
        try
        {
            var s = SettingsService.Instance.Current;
            s.HudShowFps = true;
            s.HudShowLow1Percent = true;
            s.HudShowMsDelay = true;
            s.HudShowApi = true;
            s.HudShowCpu = true;
            s.HudShowGpu = true;
            s.HudShowRam = true;
            s.HudShowNet = true;
            s.HudShowAppName = true;

            if (ToggleFps != null) ToggleFps.IsOn = true;
            if (ToggleLow1Pct != null) ToggleLow1Pct.IsOn = true;
            if (ToggleMsDelay != null) ToggleMsDelay.IsOn = true;
            if (ToggleApi != null) ToggleApi.IsOn = true;
            if (ToggleCpu != null) ToggleCpu.IsOn = true;
            if (ToggleGpu != null) ToggleGpu.IsOn = true;
            if (ToggleRam != null) ToggleRam.IsOn = true;
            if (ToggleNet != null) ToggleNet.IsOn = true;
            if (ToggleAppName != null) ToggleAppName.IsOn = true;

            SettingsService.Instance.Save();
            DebugLogService.Instance.LogAction("效能監控 (Performance)", "套用 HUD 完整全開預設方案");
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void PresetReset_Click(object sender, RoutedEventArgs e)
    {
        _isUpdatingUi = true;
        try
        {
            var s = SettingsService.Instance.Current;
            s.HudStyle = "Bar";
            s.HudTheme = "Cyberpunk";
            s.HudOpacity = 0.85;
            s.HudShowFps = true;
            s.HudShowLow1Percent = true;
            s.HudShowMsDelay = true;
            s.HudShowApi = true;
            s.HudShowCpu = true;
            s.HudShowGpu = true;
            s.HudShowRam = true;
            s.HudShowNet = true;
            s.HudClickThrough = false;
            s.HudRemoveBorder = false;

            if (HudOpacitySlider != null) HudOpacitySlider.Value = 85;
            if (OpacityValueText != null) OpacityValueText.Text = "85%";
            if (HudStyleCombo != null) HudStyleCombo.SelectedIndex = 0;
            if (HudThemeCombo != null) HudThemeCombo.SelectedIndex = 0;

            if (ToggleFps != null) ToggleFps.IsOn = true;
            if (ToggleLow1Pct != null) ToggleLow1Pct.IsOn = true;
            if (ToggleMsDelay != null) ToggleMsDelay.IsOn = true;
            if (ToggleApi != null) ToggleApi.IsOn = true;
            if (ToggleCpu != null) ToggleCpu.IsOn = true;
            if (ToggleGpu != null) ToggleGpu.IsOn = true;
            if (ToggleRam != null) ToggleRam.IsOn = true;
            if (ToggleNet != null) ToggleNet.IsOn = true;
            if (ToggleAppName != null) ToggleAppName.IsOn = true;
            if (ToggleClickThrough != null) ToggleClickThrough.IsOn = false;
            if (ToggleRemoveBorder != null) ToggleRemoveBorder.IsOn = false;

            PerformanceOverlayWindow.CurrentInstance?.ApplyClickThrough(false);
            PerformanceOverlayWindow.CurrentInstance?.ApplyBorderVisibility(false);

            SettingsService.Instance.Save();
            DebugLogService.Instance.LogAction("效能監控 (Performance)", "重設 HUD 設定為預設值");
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void ClickThrough_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        var s = SettingsService.Instance.Current;
        s.HudClickThrough = ToggleClickThrough.IsOn;
        SettingsService.Instance.Save();
        PerformanceOverlayWindow.CurrentInstance?.ApplyClickThrough(s.HudClickThrough);
        DebugLogService.Instance.LogAction("效能監控 (Performance)", $"設定 HUD 滑鼠點擊穿透: {(s.HudClickThrough ? "啟用" : "停用")}");
    }

    private void RemoveBorder_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        var s = SettingsService.Instance.Current;
        s.HudRemoveBorder = ToggleRemoveBorder.IsOn;
        SettingsService.Instance.Save();
        PerformanceOverlayWindow.CurrentInstance?.ApplyBorderVisibility(s.HudRemoveBorder);
        DebugLogService.Instance.LogAction("效能監控 (Performance)", $"設定 HUD 移除外邊框: {(s.HudRemoveBorder ? "啟用" : "停用")}");
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
