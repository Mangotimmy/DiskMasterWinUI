using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using DiskMasterWinUI.Controls;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Pages;
using DiskMasterWinUI.Services;
using WinRT.Interop;

namespace DiskMasterWinUI;

public sealed partial class MainWindow : Window
{
    private StarterPage? _starterPage;
    private EasyModePage? _easyModePage;
    private WimDeployPage? _wimDeployPage;
    private BootManagerPage? _bootManagerPage;
    private DiskToolsPage? _diskToolsPage;
    private SystemHealthPage? _systemHealthPage;
    private NtfsPermissionsPage? _ntfsPermissionsPage;
    private AdvancedModePage? _advancedModePage;
    private SystemOptimizerPage? _systemOptimizerPage;
    private NetworkToolsPage? _networkToolsPage;
    private SystemPerformancePage? _systemPerformancePage;
    private SettingsPage? _settingsPage;

    public TrayIconService TrayService { get; } = new();

    private double _currentZoom = 1.0;
    private bool _isShowingDialog = false;
    private bool _suppressWorkspaceSelectionChanged = false;
    private bool _isApplyingWorkspacePreset = false;

    private readonly List<TabViewItem> _allTabItems = new();
    private readonly Dictionary<string, FloatingTabWindow> _floatingWindows = new();

    private string? _rightDockTag;
    private Page? _rightDockPage;
    private string? _bottomDockTag;
    private Page? _bottomDockPage;

    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private static readonly Dictionary<string, string[]> WorkspaceTabTags = new()
    {
        ["Master"] = ["Starter", "EasyMode", "WimDeploy", "BootManager", "DiskTools", "SystemHealth", "NtfsPermissions", "AdvancedMode", "SystemOptimizer", "NetworkTools", "SystemPerformance", "Settings"],
        ["Deploy"] = ["Starter", "WimDeploy", "BootManager", "EasyMode", "DiskTools", "Settings"],
        ["Repair"] = ["Starter", "SystemHealth", "BootManager", "DiskTools", "NtfsPermissions", "SystemPerformance", "EasyMode", "Settings"],
        ["Gaming"] = ["Starter", "SystemOptimizer", "NetworkTools", "SystemPerformance", "DiskTools", "SystemHealth", "Settings"],
        ["Lite"] = ["Starter", "EasyMode", "NetworkTools", "SystemPerformance", "Settings"]
    };

    private static readonly string[] EasyModeAllowedTags = ["Starter", "EasyMode", "NetworkTools", "SystemPerformance", "Settings"];

    public MainWindow()
    {
        InitializeComponent();

        DispatcherHelper.UIDispatcher = this.DispatcherQueue;
        WindowHelper.CurrentHwnd = WindowNative.GetWindowHandle(this);
        NormalView.Loaded += (_, _) => WindowHelper.RootXamlRoot = NormalView.XamlRoot;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(iconPath))
        {
            try
            {
                AppWindow.SetIcon(iconPath);
            }
            catch { }
        }

        // Apply Win32 WM_SETICON on HWND to fix Windows 11 Taskbar generic white document icon
        SetWindowTaskbarIcons();

        // Cache all 12 original tabs for workspace filtering & floating dock-back
        _allTabItems.AddRange([
            TabStarter,
            TabEasyMode,
            TabWimDeploy,
            TabBootManager,
            TabDiskTools,
            TabSystemHealth,
            TabNtfsPermissions,
            TabAdvancedMode,
            TabOptimizer,
            TabNetworkTools,
            TabSystemPerformance,
            TabSettings
        ]);

        try
        {
            TrayService.Initialize(WindowHelper.CurrentHwnd, "DiskMaster Pro");
            TrayService.RestoreRequested += () =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    TrayService.RestoreFromTray();
                });
            };
            TrayService.QuickActionRequested += (action) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    HandleQuickAction(action);
                });
            };

            // Initialize Windows Notification Center & Toast integration
            WindowsNotificationService.Instance.Initialize(
                onNavigate: (tag) =>
                {
                    TrayService.RestoreFromTray();
                    if (!string.IsNullOrEmpty(tag) && tag != "Default")
                    {
                        NavigateToTag(tag);
                    }
                },
                trayFallback: (title, msg) =>
                {
                    TrayService.ShowNotification(title, msg);
                });
        }
        catch { }

        AppWindow.Closing += (sender, args) =>
        {
            if (SettingsService.Instance.Current.CloseToTray)
            {
                args.Cancel = true;
                TrayService.MinimizeToTray();
                WindowsNotificationService.Instance.ShowNotification("DiskMaster Pro", LocalizationService.Instance["CloseToTrayNotice"] ?? "已縮小至系統匣後台執行", "EasyMode");
            }
        };

        AppWindow.Changed += (sender, args) =>
        {
            if (args.DidPresenterChange && sender.Presenter is OverlappedPresenter op && op.State == OverlappedPresenterState.Minimized && SettingsService.Instance.Current.MinimizeToTray)
            {
                TrayService.MinimizeToTray();
            }
        };

        // Auto-size and center window based on monitor work area
        try
        {
            var (pos, size) = DisplayHelper.GetOptimalWindowBounds(WindowHelper.CurrentHwnd);
            AppWindow.Resize(size);
            AppWindow.Move(pos);
        }
        catch { }

        // Check Administrator privilege status and update title bar badge
        UpdateAdminStatusUI();

        PopulateWorkspaceCombo();
        ApplyWorkspacePreset(SettingsService.Instance.Current.WorkspacePreset);
        ApplyCustomFont();
        LocalizationService.Instance.ApplyLanguagePreference(SettingsService.Instance.Current.Language);
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;

        // Restore scale from settings or auto-detect optimal scale
        var savedScale = SettingsService.Instance.Current.ScalePercent;
        if (savedScale >= 80 && savedScale <= 150)
        {
            SetZoom(savedScale / 100.0);
        }
        else
        {
            AutoDetectZoom();
        }

        SetupKeyboardAccelerators();
        TabReorderHelper.Attach(MainTabs, SaveCustomTabOrder);

        CurrentInstance = this;
        ApplyBackgroundAndCompanion();

        SettingsService.Instance.SettingsChanged += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyBackgroundAndCompanion();
                ApplyCustomFont();
                var savedScale = SettingsService.Instance.Current.ScalePercent;
                if (savedScale >= 80 && savedScale <= 150 && Math.Abs(_currentZoom - (savedScale / 100.0)) > 0.01)
                {
                    SetZoom(savedScale / 100.0);
                }
                if (!_isApplyingWorkspacePreset)
                {
                    var currentWorkspace = SettingsService.Instance.Current.WorkspacePreset ?? "Master";
                    ApplyWorkspacePreset(currentWorkspace);
                    PopulateWorkspaceCombo(forceRebuild: false);
                }
            });
        };

        GlobalExceptionHandler.ExceptionReported += (ex, context) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                GlobalExceptionBar.Message = $"[{context}] {ex.Message}";
                GlobalExceptionBar.IsOpen = true;

                if (SettingsService.Instance.Current.EnablePromptWindows && WindowHelper.RootXamlRoot != null)
                {
                    ShowModalExceptionDialog(ex, context);
                }
            });
        };

        // Select first tab by default
        if (MainTabs.TabItems.Count > 0)
        {
            MainTabs.SelectedIndex = 0;
        }
    }

    private void SetWindowTaskbarIcons()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                var hBig = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                var hSmall = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                if (hBig != IntPtr.Zero)
                {
                    SendMessage(WindowHelper.CurrentHwnd, WM_SETICON, (IntPtr)ICON_BIG, hBig);
                }
                if (hSmall != IntPtr.Zero)
                {
                    SendMessage(WindowHelper.CurrentHwnd, WM_SETICON, (IntPtr)ICON_SMALL, hSmall);
                }
            }
        }
        catch { }
    }

    private void HandleQuickAction(string action)
    {
        TrayService.RestoreFromTray();
        switch (action)
        {
            case "Open":
                break;
            case "EasyMode":
                NavigateToTag("EasyMode");
                break;
            case "Performance":
                NavigateToTag("SystemPerformance");
                break;
            case "Network":
                NavigateToTag("NetworkTools");
                break;
            case "DiskTools":
                NavigateToTag("DiskTools");
                break;
            case "SystemHealth":
                NavigateToTag("SystemHealth");
                break;
            case "CleanCache":
                NavigateToTag("SystemHealth");
                if (_systemHealthPage != null)
                {
                    _ = _systemHealthPage.ViewModel.CleanSelectedTempCategoriesCommand.ExecuteAsync(null);
                }
                break;
            case "Settings":
                NavigateToTag("Settings");
                break;
            case "Exit":
                TrayService.Dispose();
                AppWindow.Destroy();
                Application.Current.Exit();
                break;
        }
    }

    public static MainWindow? CurrentInstance { get; private set; }

    public void ApplyCustomTabOrder(IEnumerable<string> order, IEnumerable<string> disabledTags)
    {
        var preset = SettingsService.Instance.Current.WorkspacePreset ?? "Master";
        ApplyWorkspacePreset(preset);
    }

    public void ResetTabOrderToDefault()
    {
        var preset = SettingsService.Instance.Current.WorkspacePreset ?? "Master";
        ApplyWorkspacePreset(preset);
    }

    public void CompanionSay(string text)
    {
        CompanionControl.Say(text);
    }

    public void UpdateCompanionHealth(string status, int? temp)
    {
        CompanionControl.UpdateHealthStatus(status, temp);
    }

    public void SetCompanionEmotion(CompanionEmotion emotion)
    {
        CompanionControl.SetEmotion(emotion);
    }

    public void ApplyBackgroundAndCompanion()
    {
        var settings = SettingsService.Instance.Current;

        // Custom Background Image
        if (!string.IsNullOrWhiteSpace(settings.BackgroundImagePath) && System.IO.File.Exists(settings.BackgroundImagePath))
        {
            try
            {
                BackgroundCustomImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(settings.BackgroundImagePath));
                BackgroundCustomImage.Opacity = Math.Clamp(settings.BackgroundOpacity, 0.05, 1.0);
                BackgroundCustomImage.Visibility = Visibility.Visible;
            }
            catch
            {
                BackgroundCustomImage.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            BackgroundCustomImage.Visibility = Visibility.Collapsed;
        }

        // Apply Glass Transparency to Cards and Expanders
        ApplyCardGlassTransparency(settings);

        // 3D VRM / 2D Live2D Companion Avatar (100% True Alpha Transparency)
        if (settings.EnableCompanionAvatar)
        {
            CompanionControl.Visibility = Visibility.Visible;
            CompanionControl.LoadCustomModel(settings.CompanionModelPath);
            CompanionControl.ApplySavedSettings();
        }
        else
        {
            CompanionControl.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyCardGlassTransparency(AppSettings settings)
    {
        bool hasBackground = BackgroundCustomImage.Visibility == Visibility.Visible;
        bool isGlass = settings.EnableGlassTransparency || hasBackground;
        double opacity = isGlass ? Math.Clamp(settings.CardOpacity, 0.15, 1.0) : 1.0;

        bool isDark = true;
        try
        {
            isDark = (App.Current.RequestedTheme == ApplicationTheme.Dark);
        }
        catch { }

        byte alpha = (byte)(opacity * 255);
        byte alphaSec = (byte)(opacity * 0.75 * 255);
        byte alphaHeader = (byte)(opacity * 0.85 * 255);
        byte alphaContent = (byte)(opacity * 0.55 * 255);

        if (isDark)
        {
            DynamicCardBrush.Color = Windows.UI.Color.FromArgb(alpha, 30, 30, 30);
            DynamicCardSecBrush.Color = Windows.UI.Color.FromArgb(alphaSec, 42, 42, 42);
            DynamicExpanderHeaderBrush.Color = Windows.UI.Color.FromArgb(alphaHeader, 36, 36, 36);
            DynamicExpanderContentBrush.Color = Windows.UI.Color.FromArgb(alphaContent, 22, 22, 22);
        }
        else
        {
            DynamicCardBrush.Color = Windows.UI.Color.FromArgb(alpha, 255, 255, 255);
            DynamicCardSecBrush.Color = Windows.UI.Color.FromArgb(alphaSec, 246, 246, 246);
            DynamicExpanderHeaderBrush.Color = Windows.UI.Color.FromArgb(alphaHeader, 250, 250, 250);
            DynamicExpanderContentBrush.Color = Windows.UI.Color.FromArgb(alphaContent, 240, 240, 240);
        }
    }

    private void NormalView_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (CompanionControl.Visibility == Visibility.Visible)
        {
            CompanionControl.TrackPointer(e.GetCurrentPoint(NormalView).Position);
        }
    }

    public void ApplyCustomFont()
    {
        var fontName = SettingsService.Instance.Current.CustomFontFamily;
        if (string.IsNullOrWhiteSpace(fontName))
        {
            fontName = "Segoe UI Variable";
        }

        var scalePercent = SettingsService.Instance.Current.CustomFontScale > 0 ? SettingsService.Instance.Current.CustomFontScale : 100.0;
        double scaleFactor = scalePercent / 100.0;

        try
        {
            var fontFamily = new FontFamily(fontName);
            MainTabs.FontFamily = fontFamily;
            ContentFrame.FontFamily = fontFamily;
            AppTitleBar.FontFamily = fontFamily;
            WorkspaceCombo.FontFamily = fontFamily;
            FloatTabBtn.FontFamily = fontFamily;

            ContentFrame.FontSize = 14.0 * scaleFactor;
            MainTabs.FontSize = 14.0 * scaleFactor;
        }
        catch { }
    }

    private void PopulateWorkspaceCombo(bool forceRebuild = false)
    {
        var loc = LocalizationService.Instance;
        var presets = new (string Key, string Name)[]
        {
            ("Master", loc["WorkspaceMaster"]),
            ("Deploy", loc["WorkspaceDeploy"]),
            ("Repair", loc["WorkspaceRepair"]),
            ("Gaming", loc["WorkspaceGaming"]),
            ("Lite", loc["WorkspaceLite"])
        };

        var currentPreset = SettingsService.Instance.Current.WorkspacePreset ?? "Master";
        _suppressWorkspaceSelectionChanged = true;
        try
        {
            if (forceRebuild || WorkspaceCombo.Items.Count != presets.Length)
            {
                WorkspaceCombo.Items.Clear();
                for (int i = 0; i < presets.Length; i++)
                {
                    var item = new ComboBoxItem { Content = presets[i].Name, Tag = presets[i].Key, FontSize = 11 };
                    WorkspaceCombo.Items.Add(item);
                }
            }

            for (int i = 0; i < WorkspaceCombo.Items.Count; i++)
            {
                if (WorkspaceCombo.Items[i] is ComboBoxItem cbi && cbi.Tag is string key &&
                    key.Equals(currentPreset, StringComparison.OrdinalIgnoreCase))
                {
                    WorkspaceCombo.SelectedIndex = i;
                    break;
                }
            }
        }
        finally
        {
            _suppressWorkspaceSelectionChanged = false;
        }
    }

    private void WorkspaceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressWorkspaceSelectionChanged) return;
        if (WorkspaceCombo.SelectedItem is ComboBoxItem item && item.Tag is string key)
        {
            ApplyWorkspacePreset(key);
        }
    }

    private void ApplyWorkspacePreset(string presetKey)
    {
        if (_isApplyingWorkspacePreset) return;
        _isApplyingWorkspacePreset = true;

        try
        {
            if (!WorkspaceTabTags.TryGetValue(presetKey, out var allowedTags))
            {
                allowedTags = WorkspaceTabTags["Master"];
                presetKey = "Master";
            }

            var s = SettingsService.Instance.Current;
            if (s.WorkspacePreset != presetKey)
            {
                s.WorkspacePreset = presetKey;
                SettingsService.Instance.Save();
            }

            if (s.IsEasyMode)
            {
                allowedTags = allowedTags.Where(tag => EasyModeAllowedTags.Contains(tag)).ToArray();
            }

            if (s.DisabledTabTags != null && s.DisabledTabTags.Count > 0)
            {
                allowedTags = allowedTags.Where(tag => !s.DisabledTabTags.Contains(tag)).ToArray();
            }

            if (s.EnableCustomTabReorder && s.CustomTabOrder != null && s.CustomTabOrder.Count > 0)
            {
                allowedTags = allowedTags
                    .OrderBy(tag =>
                    {
                        int idx = s.CustomTabOrder.IndexOf(tag);
                        return idx >= 0 ? idx : int.MaxValue;
                    })
                    .ToArray();
            }

            var currentSelectedTag = (MainTabs.SelectedItem as TabViewItem)?.Tag?.ToString();

            MainTabs.TabItems.Clear();
            TabViewItem? toSelect = null;

            foreach (var tag in allowedTags)
            {
                if (_floatingWindows.ContainsKey(tag) || tag == _rightDockTag || tag == _bottomDockTag) continue;

                var tabItem = _allTabItems.FirstOrDefault(t => t.Tag?.ToString() == tag);
                if (tabItem != null)
                {
                    MainTabs.TabItems.Add(tabItem);
                    if (tag == currentSelectedTag)
                    {
                        toSelect = tabItem;
                    }
                }
            }

            if (MainTabs.TabItems.Count > 0)
            {
                var target = toSelect ?? (MainTabs.TabItems[0] as TabViewItem);
                MainTabs.SelectedItem = target;
                if (target?.Tag is string targetTag)
                {
                    NavigateToTag(targetTag);
                }
            }
        }
        finally
        {
            _isApplyingWorkspacePreset = false;
        }
    }

    private void ModeSwitcher_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Instance.Current;
        s.IsEasyMode = !s.IsEasyMode;
        SettingsService.Instance.Save();
        UpdateModeSwitcherUI();
        ApplyWorkspacePreset(s.WorkspacePreset ?? "Master");
    }

    private void UpdateModeSwitcherUI()
    {
        var isEasy = SettingsService.Instance.Current.IsEasyMode;
        var loc = LocalizationService.Instance;
        if (isEasy)
        {
            ModeSwitcherText.Text = loc.CurrentLanguage switch
            {
                "zh-CN" => "🟢 简易模式",
                "en-US" => "🟢 Easy Mode",
                "ja-JP" => "🟢 かんたんモード",
                _ => "🟢 簡易模式"
            };
            ModeSwitcherIcon.Glyph = "\uE790";
        }
        else
        {
            ModeSwitcherText.Text = loc.CurrentLanguage switch
            {
                "zh-CN" => "⚡ 专业进阶模式",
                "en-US" => "⚡ Advance Mode",
                "ja-JP" => "⚡ アドバンスモード",
                _ => "⚡ 專業進階模式"
            };
            ModeSwitcherIcon.Glyph = "\uE71D";
        }
    }

    // ── Chrome Logic: Multi-Directional Docking & Floating Windows ──

    public string GetTabHeader(string tag)
    {
        var tab = _allTabItems.FirstOrDefault(t => t.Tag?.ToString() == tag);
        return tab?.Header?.ToString() ?? tag;
    }

    public Page GetOrCreatePageForTag(string tag) => tag switch
    {
        "Starter" => _starterPage ??= new StarterPage(),
        "EasyMode" => _easyModePage ??= new EasyModePage(),
        "WimDeploy" => _wimDeployPage ??= new WimDeployPage(),
        "BootManager" => _bootManagerPage ??= new BootManagerPage(),
        "DiskTools" => _diskToolsPage ??= new DiskToolsPage(),
        "SystemHealth" => _systemHealthPage ??= new SystemHealthPage(),
        "NtfsPermissions" => _ntfsPermissionsPage ??= new NtfsPermissionsPage(),
        "AdvancedMode" => _advancedModePage ??= new AdvancedModePage(),
        "SystemOptimizer" => _systemOptimizerPage ??= new SystemOptimizerPage(),
        "NetworkTools" => _networkToolsPage ??= new NetworkToolsPage(),
        "SystemPerformance" => _systemPerformancePage ??= new SystemPerformancePage(),
        "Settings" => _settingsPage ??= new SettingsPage(),
        _ => new Page()
    };

    public void DockToRight(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        if (_rightDockTag == tag) return;

        if (_bottomDockTag == tag)
        {
            CloseBottomDock(reinsertToTabs: false);
        }

        if (_floatingWindows.TryGetValue(tag, out var floatWin))
        {
            _floatingWindows.Remove(tag);
            floatWin.PerformDock(closeWindow: true);
        }

        if (!string.IsNullOrEmpty(_rightDockTag))
        {
            CloseRightDock(reinsertToTabs: true);
        }

        var tabItem = MainTabs.TabItems.OfType<TabViewItem>().FirstOrDefault(t => t.Tag?.ToString() == tag);
        if (tabItem != null)
        {
            if (ReferenceEquals(MainTabs.SelectedItem, tabItem))
            {
                ContentFrame.Content = null;
            }
            MainTabs.TabItems.Remove(tabItem);
            if (MainTabs.TabItems.Count > 0 && MainTabs.SelectedItem == null)
            {
                MainTabs.SelectedIndex = 0;
            }
        }

        var page = GetOrCreatePageForTag(tag);
        _rightDockTag = tag;
        _rightDockPage = page;

        RightDockTitleText.Text = $"側邊分屏: {GetTabHeader(tag)}";
        RightDockFrame.Content = page;
        RightDockPanel.Visibility = Visibility.Visible;
        RightDockSplitter.Visibility = Visibility.Visible;
        RightDockColDef.Width = new GridLength(460);

        UpdateFloatingManagerUI();
    }

    public void CloseRightDock(bool reinsertToTabs = true)
    {
        if (string.IsNullOrEmpty(_rightDockTag)) return;

        var tag = _rightDockTag;
        _rightDockTag = null;
        _rightDockPage = null;

        RightDockFrame.Content = null;
        RightDockPanel.Visibility = Visibility.Collapsed;
        RightDockSplitter.Visibility = Visibility.Collapsed;
        RightDockColDef.Width = GridLength.Auto;

        if (reinsertToTabs)
        {
            ReinsertTabToMainTabs(tag);
        }
        UpdateFloatingManagerUI();
    }

    public void DockToBottom(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        if (_bottomDockTag == tag) return;

        if (_rightDockTag == tag)
        {
            CloseRightDock(reinsertToTabs: false);
        }

        if (_floatingWindows.TryGetValue(tag, out var floatWin))
        {
            _floatingWindows.Remove(tag);
            floatWin.PerformDock(closeWindow: true);
        }

        if (!string.IsNullOrEmpty(_bottomDockTag))
        {
            CloseBottomDock(reinsertToTabs: true);
        }

        var tabItem = MainTabs.TabItems.OfType<TabViewItem>().FirstOrDefault(t => t.Tag?.ToString() == tag);
        if (tabItem != null)
        {
            if (ReferenceEquals(MainTabs.SelectedItem, tabItem))
            {
                ContentFrame.Content = null;
            }
            MainTabs.TabItems.Remove(tabItem);
            if (MainTabs.TabItems.Count > 0 && MainTabs.SelectedItem == null)
            {
                MainTabs.SelectedIndex = 0;
            }
        }

        var page = GetOrCreatePageForTag(tag);
        _bottomDockTag = tag;
        _bottomDockPage = page;

        BottomDockTitleText.Text = $"底部水平分屏: {GetTabHeader(tag)}";
        BottomDockFrame.Content = page;
        BottomDockPanel.Visibility = Visibility.Visible;
        BottomDockSplitter.Visibility = Visibility.Visible;
        BottomDockRowDef.Height = new GridLength(280);

        UpdateFloatingManagerUI();
    }

    public void CloseBottomDock(bool reinsertToTabs = true)
    {
        if (string.IsNullOrEmpty(_bottomDockTag)) return;

        var tag = _bottomDockTag;
        _bottomDockTag = null;
        _bottomDockPage = null;

        BottomDockFrame.Content = null;
        BottomDockPanel.Visibility = Visibility.Collapsed;
        BottomDockSplitter.Visibility = Visibility.Collapsed;
        BottomDockRowDef.Height = GridLength.Auto;

        if (reinsertToTabs)
        {
            ReinsertTabToMainTabs(tag);
        }
        UpdateFloatingManagerUI();
    }

    private void ReinsertTabToMainTabs(string tag)
    {
        var tabItem = _allTabItems.FirstOrDefault(t => t.Tag?.ToString() == tag);
        if (tabItem != null && !MainTabs.TabItems.Contains(tabItem))
        {
            var currentPreset = SettingsService.Instance.Current.WorkspacePreset ?? "Master";
            if (!WorkspaceTabTags.TryGetValue(currentPreset, out var allowedTags))
            {
                allowedTags = WorkspaceTabTags["Master"];
            }

            int insertIndex = MainTabs.TabItems.Count;
            if (allowedTags.Contains(tag))
            {
                insertIndex = 0;
                int tagIndexInAllowed = Array.IndexOf(allowedTags, tag);
                for (int i = 0; i < MainTabs.TabItems.Count; i++)
                {
                    var otherTag = (MainTabs.TabItems[i] as TabViewItem)?.Tag?.ToString() ?? "";
                    int otherIndexInAllowed = Array.IndexOf(allowedTags, otherTag);
                    if (otherIndexInAllowed < tagIndexInAllowed)
                    {
                        insertIndex = i + 1;
                    }
                }
                insertIndex = Math.Clamp(insertIndex, 0, MainTabs.TabItems.Count);
            }
            MainTabs.TabItems.Insert(insertIndex, tabItem);
            MainTabs.SelectedItem = tabItem;
            NavigateToTag(tag);
            SaveCustomTabOrder();
        }
    }

    private void RightToBottom_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_rightDockTag))
        {
            var tag = _rightDockTag;
            CloseRightDock(reinsertToTabs: false);
            DockToBottom(tag);
        }
    }

    private void RightToFloat_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_rightDockTag))
        {
            var tag = _rightDockTag;
            var header = GetTabHeader(tag);
            var page = _rightDockPage ?? GetOrCreatePageForTag(tag);
            CloseRightDock(reinsertToTabs: false);
            CreateFloatingWindow(tag, header, page);
        }
    }

    private void RightDockClose_Click(object sender, RoutedEventArgs e)
    {
        CloseRightDock(reinsertToTabs: true);
    }

    private void BottomToRight_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_bottomDockTag))
        {
            var tag = _bottomDockTag;
            CloseBottomDock(reinsertToTabs: false);
            DockToRight(tag);
        }
    }

    private void BottomToFloat_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_bottomDockTag))
        {
            var tag = _bottomDockTag;
            var header = GetTabHeader(tag);
            var page = _bottomDockPage ?? GetOrCreatePageForTag(tag);
            CloseBottomDock(reinsertToTabs: false);
            CreateFloatingWindow(tag, header, page);
        }
    }

    private void BottomDockClose_Click(object sender, RoutedEventArgs e)
    {
        CloseBottomDock(reinsertToTabs: true);
    }

    public void FloatTab(TabViewItem tabItem, Windows.Graphics.PointInt32? mousePos = null)
    {
        var tag = tabItem.Tag?.ToString();
        if (string.IsNullOrEmpty(tag) || _floatingWindows.ContainsKey(tag)) return;
        if (MainTabs.TabItems.Count <= 1) return;

        var header = tabItem.Header?.ToString() ?? tag;
        Page page = GetOrCreatePageForTag(tag);

        if (ReferenceEquals(ContentFrame.Content, page))
        {
            ContentFrame.Content = null;
        }

        MainTabs.TabItems.Remove(tabItem);
        if (MainTabs.TabItems.Count > 0)
        {
            MainTabs.SelectedIndex = Math.Clamp(MainTabs.SelectedIndex, 0, MainTabs.TabItems.Count - 1);
        }

        CreateFloatingWindow(tag, header, page, mousePos);
    }

    public void CreateFloatingWindow(string tag, string header, Page page, Windows.Graphics.PointInt32? mousePos = null)
    {
        if (_floatingWindows.ContainsKey(tag)) return;

        var win = new FloatingTabWindow(tag, header, page, mousePos);
        win.DockRequested += OnFloatingTabDockRequested;
        win.DockToRightRequested += (w) =>
        {
            _floatingWindows.Remove(w.TabTag);
            DockToRight(w.TabTag);
        };
        win.DockToBottomRequested += (w) =>
        {
            _floatingWindows.Remove(w.TabTag);
            DockToBottom(w.TabTag);
        };
        _floatingWindows[tag] = win;
        win.Activate();
        UpdateFloatingManagerUI();
        SaveCustomTabOrder();
    }

    private TabViewItem? _draggedTabItem;

    private void MainTabs_TabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args)
    {
        _draggedTabItem = args.Tab;
        args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        args.Data.SetText(args.Tab.Tag?.ToString() ?? "tab");
        ShowDockDropOverlay(true);
    }

    private void MainTabs_TabDroppedOutside(TabView sender, TabViewTabDroppedOutsideEventArgs args)
    {
        ShowDockDropOverlay(false);
        var tab = args.Tab ?? _draggedTabItem;
        _draggedTabItem = null;
        if (tab != null)
        {
            var cursor = DisplayHelper.GetCurrentCursorPosition();
            FloatTab(tab, cursor);
        }
    }

    private void MainTabs_TabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        _draggedTabItem = null;
        ShowDockDropOverlay(false);
        SaveCustomTabOrder();
    }

    private void DockContainerGrid_DragOver(object sender, DragEventArgs e)
    {
        DockDropOverlay_DragOver(sender, e);
    }

    private void DockContainerGrid_DragLeave(object sender, DragEventArgs e)
    {
        DockDropOverlay_DragLeave(sender, e);
    }

    private void DockContainerGrid_Drop(object sender, DragEventArgs e)
    {
        DockDropOverlay_Drop(sender, e);
    }

    private void DockDropOverlay_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
        e.DragUIOverride.IsGlyphVisible = false;

        ShowDockDropOverlay(true);

        var pos = e.GetPosition(DockDropOverlay);
        var w = DockDropOverlay.ActualWidth;
        var h = DockDropOverlay.ActualHeight;

        UpdateDockDropHighlight(pos, w, h);
    }

    private void DockDropOverlay_DragLeave(object sender, DragEventArgs e)
    {
        ResetDockDropHighlights();
    }

    private void DockDropOverlay_Drop(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        var tab = _draggedTabItem ?? (MainTabs.SelectedItem as TabViewItem);
        _draggedTabItem = null;
        ShowDockDropOverlay(false);

        if (tab == null) return;
        var tag = tab.Tag?.ToString();
        if (string.IsNullOrEmpty(tag)) return;

        var pos = e.GetPosition(DockDropOverlay);
        var w = DockDropOverlay.ActualWidth;
        var h = DockDropOverlay.ActualHeight;

        if (pos.X > w - 280)
        {
            DockToRight(tag);
        }
        else if (pos.Y > h - 190)
        {
            DockToBottom(tag);
        }
        else
        {
            var cursor = DisplayHelper.GetCurrentCursorPosition();
            FloatTab(tab, cursor);
        }
    }

    private void DockDropOverlay_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _draggedTabItem = null;
        ShowDockDropOverlay(false);
    }

    private void ShowDockDropOverlay(bool visible)
    {
        if (DockDropOverlay == null) return;
        DockDropOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DockDropOverlay.IsHitTestVisible = visible;
        if (!visible)
        {
            ResetDockDropHighlights();
        }
    }

    private void UpdateDockDropHighlight(Windows.Foundation.Point pos, double width, double height)
    {
        if (DropZoneCenter == null || DropZoneRight == null || DropZoneBottom == null) return;

        if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accentObj) && accentObj is Brush accentBrush &&
            Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var defaultObj) && defaultObj is Brush defaultStroke)
        {
            if (pos.X > width - 280)
            {
                DropZoneRight.BorderBrush = accentBrush;
                DropZoneRight.BorderThickness = new Thickness(3);
                DropZoneRight.Opacity = 1.0;

                DropZoneBottom.BorderBrush = defaultStroke;
                DropZoneBottom.BorderThickness = new Thickness(2);
                DropZoneBottom.Opacity = 0.6;

                DropZoneCenter.BorderBrush = defaultStroke;
                DropZoneCenter.BorderThickness = new Thickness(2);
                DropZoneCenter.Opacity = 0.6;
            }
            else if (pos.Y > height - 190)
            {
                DropZoneBottom.BorderBrush = accentBrush;
                DropZoneBottom.BorderThickness = new Thickness(3);
                DropZoneBottom.Opacity = 1.0;

                DropZoneRight.BorderBrush = defaultStroke;
                DropZoneRight.BorderThickness = new Thickness(2);
                DropZoneRight.Opacity = 0.6;

                DropZoneCenter.BorderBrush = defaultStroke;
                DropZoneCenter.BorderThickness = new Thickness(2);
                DropZoneCenter.Opacity = 0.6;
            }
            else
            {
                DropZoneCenter.BorderBrush = accentBrush;
                DropZoneCenter.BorderThickness = new Thickness(3);
                DropZoneCenter.Opacity = 1.0;

                DropZoneRight.BorderBrush = defaultStroke;
                DropZoneRight.BorderThickness = new Thickness(2);
                DropZoneRight.Opacity = 0.6;

                DropZoneBottom.BorderBrush = defaultStroke;
                DropZoneBottom.BorderThickness = new Thickness(2);
                DropZoneBottom.Opacity = 0.6;
            }
        }
    }

    private void ResetDockDropHighlights()
    {
        if (DropZoneCenter == null || DropZoneRight == null || DropZoneBottom == null) return;
        if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var defaultObj) && defaultObj is Brush defaultStroke)
        {
            DropZoneCenter.BorderBrush = defaultStroke;
            DropZoneCenter.BorderThickness = new Thickness(2);
            DropZoneCenter.Opacity = 0.6;

            DropZoneRight.BorderBrush = defaultStroke;
            DropZoneRight.BorderThickness = new Thickness(2);
            DropZoneRight.Opacity = 0.6;

            DropZoneBottom.BorderBrush = defaultStroke;
            DropZoneBottom.BorderThickness = new Thickness(2);
            DropZoneBottom.Opacity = 0.6;
        }
    }

    private void SaveCustomTabOrder()
    {
        try
        {
            var order = MainTabs.TabItems
                .OfType<TabViewItem>()
                .Select(t => t.Tag?.ToString())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            var s = SettingsService.Instance.Current;
            s.CustomTabOrder = order!;
            SettingsService.Instance.Save();
        }
        catch { }
    }

    private void FloatCurrentTab_Click(object sender, RoutedEventArgs e)
    {
        if (MainTabs.SelectedItem is TabViewItem currentTab)
        {
            FloatTab(currentTab);
        }
    }

    private void OnFloatingTabDockRequested(FloatingTabWindow win)
    {
        var tag = win.TabTag;
        _floatingWindows.Remove(tag);
        ReinsertTabToMainTabs(tag);
        UpdateFloatingManagerUI();
    }

    private void DockAllFloatingTabs_Click(object sender, RoutedEventArgs e)
    {
        DockAll_Click(sender, e);
    }

    private void DockAll_Click(object sender, RoutedEventArgs e)
    {
        CloseRightDock(reinsertToTabs: true);
        CloseBottomDock(reinsertToTabs: true);

        var windows = _floatingWindows.Values.ToList();
        foreach (var win in windows)
        {
            win.PerformDock();
        }
    }

    private void UpdateFloatingManagerUI()
    {
        int floatingCount = _floatingWindows.Count;
        int dockedCount = (!string.IsNullOrEmpty(_rightDockTag) ? 1 : 0) + (!string.IsNullOrEmpty(_bottomDockTag) ? 1 : 0);
        int total = floatingCount + dockedCount;

        if (total > 0)
        {
            FloatingManagerBtn.Visibility = Visibility.Visible;
            var loc = LocalizationService.Instance;
            var template = loc.CurrentLanguage switch
            {
                "zh-CN" => "{0} 分屏/浮动 (点击全部嵌回)",
                "en-US" => "{0} Docked/Float (Click to Reset)",
                "ja-JP" => "{0} 分割/分離中 (クリックで全て戻す)",
                _ => "{0} 分屏/浮動 (點擊全部嵌回)"
            };
            FloatingCountText.Text = string.Format(template, total);
        }
        else
        {
            FloatingManagerBtn.Visibility = Visibility.Collapsed;
        }
    }

    // ── Tab ContextFlyout Right-Click Handlers ──

    private TabViewItem? GetTabFromSender(object sender)
    {
        if (sender is MenuFlyoutItem mfi)
        {
            if (mfi.DataContext is TabViewItem dTab) return dTab;
            if (mfi.Parent is MenuFlyout mf && mf.Target is TabViewItem tTab) return tTab;
            foreach (var tab in _allTabItems)
            {
                if (tab.ContextFlyout == mfi.Parent) return tab;
            }
        }
        return MainTabs.SelectedItem as TabViewItem;
    }

    private void TabContext_Float_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromSender(sender);
        if (tab != null) FloatTab(tab);
    }

    private void TabContext_DockRight_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromSender(sender);
        if (tab?.Tag is string tag)
        {
            DockToRight(tag);
        }
    }

    private void TabContext_DockBottom_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromSender(sender);
        if (tab?.Tag is string tag)
        {
            DockToBottom(tag);
        }
    }

    private void TabContext_MoveLeft_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromSender(sender);
        if (tab != null && MainTabs.TabItems.Contains(tab))
        {
            int oldIndex = MainTabs.TabItems.IndexOf(tab);
            if (oldIndex > 0)
            {
                MainTabs.TabItems.RemoveAt(oldIndex);
                MainTabs.TabItems.Insert(oldIndex - 1, tab);
                MainTabs.SelectedItem = tab;
                SaveCustomTabOrder();
            }
        }
    }

    private void TabContext_MoveRight_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromSender(sender);
        if (tab != null && MainTabs.TabItems.Contains(tab))
        {
            int oldIndex = MainTabs.TabItems.IndexOf(tab);
            if (oldIndex >= 0 && oldIndex < MainTabs.TabItems.Count - 1)
            {
                MainTabs.TabItems.RemoveAt(oldIndex);
                MainTabs.TabItems.Insert(oldIndex + 1, tab);
                MainTabs.SelectedItem = tab;
                SaveCustomTabOrder();
            }
        }
    }

    private void TabContext_MoveFirst_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromSender(sender);
        if (tab != null && MainTabs.TabItems.Contains(tab))
        {
            int oldIndex = MainTabs.TabItems.IndexOf(tab);
            if (oldIndex > 0)
            {
                MainTabs.TabItems.RemoveAt(oldIndex);
                MainTabs.TabItems.Insert(0, tab);
                MainTabs.SelectedItem = tab;
                SaveCustomTabOrder();
            }
        }
    }

    private void TabContext_MoveLast_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromSender(sender);
        if (tab != null && MainTabs.TabItems.Contains(tab))
        {
            int oldIndex = MainTabs.TabItems.IndexOf(tab);
            int lastIndex = MainTabs.TabItems.Count - 1;
            if (oldIndex < lastIndex)
            {
                MainTabs.TabItems.RemoveAt(oldIndex);
                MainTabs.TabItems.Add(tab);
                MainTabs.SelectedItem = tab;
                SaveCustomTabOrder();
            }
        }
    }

    private void TabContext_Refresh_Click(object sender, RoutedEventArgs e)
    {
        SmartRefreshCurrentPage();
    }

    private void TabContext_CopyTitle_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromSender(sender);
        if (tab != null)
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(tab.Header?.ToString() ?? "");
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        }
    }

    private async void SmartRefreshCurrentPage()
    {
        var currentTag = (MainTabs.SelectedItem as TabViewItem)?.Tag?.ToString();
        switch (currentTag)
        {
            case "Starter":
                if (_starterPage != null)
                {
                    await _starterPage.ViewModel.RefreshStatusCommand.ExecuteAsync(null);
                }
                break;
            case "EasyMode":
                if (_easyModePage != null)
                {
                    await _easyModePage.ViewModel.RefreshDisksCommand.ExecuteAsync(null);
                }
                break;
            case "WimDeploy":
                _wimDeployPage?.ViewModel.RefreshAvailableDrives();
                break;
            case "BootManager":
                if (_bootManagerPage != null)
                {
                    await _bootManagerPage.ViewModel.RefreshEntriesCommand.ExecuteAsync(null);
                }
                break;
            case "DiskTools":
                if (_diskToolsPage != null)
                {
                    await _diskToolsPage.ViewModel.RefreshAllCommand.ExecuteAsync(null);
                }
                break;
            case "AdvancedMode":
                if (_advancedModePage != null)
                {
                    await _advancedModePage.ViewModel.RefreshBcdEntriesCommand.ExecuteAsync(null);
                }
                break;
            case "SystemOptimizer":
                if (_systemOptimizerPage != null)
                {
                    await _systemOptimizerPage.ViewModel.RefreshAllCommand.ExecuteAsync(null);
                }
                break;
            case "NetworkTools":
                if (_networkToolsPage != null)
                {
                    await _networkToolsPage.ViewModel.InitializeAsync(forceReload: true);
                }
                break;
            case "SystemPerformance":
                if (_systemPerformancePage != null)
                {
                    await _systemPerformancePage.ViewModel.RefreshAllCommand.ExecuteAsync(null);
                }
                break;
            case "Settings":
                _settingsPage?.ViewModel.LoadSettings();
                break;
        }
    }

    public async Task ShowTaskCompletedPromptAsync(string title, string message, bool isSuccess = true)
    {
        var s = SettingsService.Instance.Current;

        try
        {
            TrayService.RestoreFromTray();
        }
        catch { }

        if (s.EnableTaskbarFlash)
        {
            TaskbarFlashService.FlashWindow(WindowHelper.CurrentHwnd, count: 3);
        }

        if (s.EnableAudioFeedback)
        {
            if (isSuccess)
            {
                AudioFeedbackService.PlayAsterisk();
            }
            else
            {
                AudioFeedbackService.PlayExclamation();
            }
        }

        CompanionSay(isSuccess ? $"✨ {title}" : $"⚠️ {title}");

        if (WindowHelper.RootXamlRoot != null && !_isShowingDialog)
        {
            _isShowingDialog = true;
            try
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = WindowHelper.RootXamlRoot,
                    Title = (isSuccess ? "✅ " : "⚠️ ") + title,
                    Content = new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true
                    },
                    CloseButtonText = LocalizationService.Instance.CurrentLanguage switch
                    {
                        "en-US" => "OK",
                        "ja-JP" => "OK",
                        "zh-CN" => "确定",
                        _ => "確定"
                    },
                    DefaultButton = ContentDialogButton.Close
                };
                await dialog.ShowAsync();
            }
            catch { }
            finally
            {
                _isShowingDialog = false;
            }
        }
    }

    private async void ShowModalExceptionDialog(Exception ex, string context)
    {
        if (_isShowingDialog || WindowHelper.RootXamlRoot == null) return;
        _isShowingDialog = true;
        try
        {
            var loc = LocalizationService.Instance;
            var dialog = new ContentDialog
            {
                XamlRoot = WindowHelper.RootXamlRoot,
                Title = loc.CurrentLanguage switch
                {
                    "zh-CN" => "⚠️ 系统提示",
                    "en-US" => "⚠️ System Alert",
                    "ja-JP" => "⚠️ システム警告",
                    _ => "⚠️ 系統提示"
                },
                Content = new TextBlock
                {
                    Text = $"[{context}]\n\n{ex.Message}\n\n{loc.GetString("PromptWindows")}",
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                },
                PrimaryButtonText = loc.CurrentLanguage switch
                {
                    "zh-CN" => "📋 复制错误详情",
                    "en-US" => "📋 Copy Details",
                    "ja-JP" => "📋 エラー詳細をコピー",
                    _ => "📋 複製錯誤詳情"
                },
                CloseButtonText = loc.CurrentLanguage switch { "en-US" => "Close", "ja-JP" => "閉じる", "zh-CN" => "关闭", _ => "關閉" },
                DefaultButton = ContentDialogButton.Close
            };
            dialog.PrimaryButtonClick += (_, _) =>
            {
                GlobalExceptionHandler.CopyToClipboard(ex, context);
            };
            await dialog.ShowAsync();
        }
        catch { }
        finally
        {
            _isShowingDialog = false;
        }
    }

    private void SetupKeyboardAccelerators()
    {
        NormalView.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        void AddHiddenAccel(KeyboardAccelerator a)
        {
            NormalView.KeyboardAccelerators.Add(a);
        }

        // Ctrl+1 through Ctrl+8 for Tab navigation
        for (int i = 0; i < 8; i++)
        {
            var index = i;
            var key = (VirtualKey)((int)VirtualKey.Number1 + i);
            var accel = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
            accel.Invoked += (s, e) =>
            {
                if (index < MainTabs.TabItems.Count)
                {
                    MainTabs.SelectedIndex = index;
                    e.Handled = true;
                }
            };
            AddHiddenAccel(accel);
        }

        // Ctrl+Tab (Next Tab)
        var nextTabAccel = new KeyboardAccelerator { Key = VirtualKey.Tab, Modifiers = VirtualKeyModifiers.Control };
        nextTabAccel.Invoked += (s, e) =>
        {
            if (MainTabs.TabItems.Count > 1)
            {
                MainTabs.SelectedIndex = (MainTabs.SelectedIndex + 1) % MainTabs.TabItems.Count;
                e.Handled = true;
            }
        };
        AddHiddenAccel(nextTabAccel);

        // Ctrl+Shift+Tab (Previous Tab)
        var prevTabAccel = new KeyboardAccelerator { Key = VirtualKey.Tab, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
        prevTabAccel.Invoked += (s, e) =>
        {
            if (MainTabs.TabItems.Count > 1)
            {
                MainTabs.SelectedIndex = (MainTabs.SelectedIndex - 1 + MainTabs.TabItems.Count) % MainTabs.TabItems.Count;
                e.Handled = true;
            }
        };
        AddHiddenAccel(prevTabAccel);

        // F5 & Ctrl+R for Smart Refresh
        var f5Accel = new KeyboardAccelerator { Key = VirtualKey.F5 };
        f5Accel.Invoked += (s, e) => { SmartRefreshCurrentPage(); e.Handled = true; };
        AddHiddenAccel(f5Accel);

        var ctrlRAccel = new KeyboardAccelerator { Key = VirtualKey.R, Modifiers = VirtualKeyModifiers.Control };
        ctrlRAccel.Invoked += (s, e) => { SmartRefreshCurrentPage(); e.Handled = true; };
        AddHiddenAccel(ctrlRAccel);

        // Ctrl+Shift+F for Float Tab
        var floatAccel = new KeyboardAccelerator { Key = VirtualKey.F, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
        floatAccel.Invoked += (s, e) =>
        {
            if (MainTabs.SelectedItem is TabViewItem tab) FloatTab(tab);
            e.Handled = true;
        };
        AddHiddenAccel(floatAccel);

        // Ctrl+Shift+D for Dock All
        var dockAllAccel = new KeyboardAccelerator { Key = VirtualKey.D, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
        dockAllAccel.Invoked += (s, e) => { DockAll_Click(this, new RoutedEventArgs()); e.Handled = true; };
        AddHiddenAccel(dockAllAccel);

        // Alt+Left (Move Tab Left)
        var moveLeftAccel = new KeyboardAccelerator { Key = VirtualKey.Left, Modifiers = VirtualKeyModifiers.Menu };
        moveLeftAccel.Invoked += (s, e) =>
        {
            if (MainTabs.SelectedItem is TabViewItem tab)
            {
                int idx = MainTabs.TabItems.IndexOf(tab);
                if (idx > 0)
                {
                    MainTabs.TabItems.RemoveAt(idx);
                    MainTabs.TabItems.Insert(idx - 1, tab);
                    MainTabs.SelectedItem = tab;
                    SaveCustomTabOrder();
                    e.Handled = true;
                }
            }
        };
        AddHiddenAccel(moveLeftAccel);

        // Alt+Right (Move Tab Right)
        var moveRightAccel = new KeyboardAccelerator { Key = VirtualKey.Right, Modifiers = VirtualKeyModifiers.Menu };
        moveRightAccel.Invoked += (s, e) =>
        {
            if (MainTabs.SelectedItem is TabViewItem tab)
            {
                int idx = MainTabs.TabItems.IndexOf(tab);
                if (idx >= 0 && idx < MainTabs.TabItems.Count - 1)
                {
                    MainTabs.TabItems.RemoveAt(idx);
                    MainTabs.TabItems.Insert(idx + 1, tab);
                    MainTabs.SelectedItem = tab;
                    SaveCustomTabOrder();
                    e.Handled = true;
                }
            }
        };
        AddHiddenAccel(moveRightAccel);

        // Alt+Home (Move Tab to First)
        var moveFirstAccel = new KeyboardAccelerator { Key = VirtualKey.Home, Modifiers = VirtualKeyModifiers.Menu };
        moveFirstAccel.Invoked += (s, e) =>
        {
            if (MainTabs.SelectedItem is TabViewItem tab)
            {
                int idx = MainTabs.TabItems.IndexOf(tab);
                if (idx > 0)
                {
                    MainTabs.TabItems.RemoveAt(idx);
                    MainTabs.TabItems.Insert(0, tab);
                    MainTabs.SelectedItem = tab;
                    SaveCustomTabOrder();
                    e.Handled = true;
                }
            }
        };
        AddHiddenAccel(moveFirstAccel);

        // Alt+End (Move Tab to Last)
        var moveLastAccel = new KeyboardAccelerator { Key = VirtualKey.End, Modifiers = VirtualKeyModifiers.Menu };
        moveLastAccel.Invoked += (s, e) =>
        {
            if (MainTabs.SelectedItem is TabViewItem tab)
            {
                int idx = MainTabs.TabItems.IndexOf(tab);
                if (idx >= 0 && idx < MainTabs.TabItems.Count - 1)
                {
                    MainTabs.TabItems.RemoveAt(idx);
                    MainTabs.TabItems.Add(tab);
                    MainTabs.SelectedItem = tab;
                    SaveCustomTabOrder();
                    e.Handled = true;
                }
            }
        };
        AddHiddenAccel(moveLastAccel);

        // Ctrl+D for Export Diagnostics Report
        var exportDiagAccel = new KeyboardAccelerator { Key = VirtualKey.D, Modifiers = VirtualKeyModifiers.Control };
        exportDiagAccel.Invoked += async (s, e) =>
        {
            e.Handled = true;
            await DiagnosticExportService.Instance.ExportReportToFileAsync(autoOpenFile: true);
        };
        AddHiddenAccel(exportDiagAccel);

        // Ctrl+L for Clear Log
        var clearLogAccel = new KeyboardAccelerator { Key = VirtualKey.L, Modifiers = VirtualKeyModifiers.Control };
        clearLogAccel.Invoked += (s, e) =>
        {
            var currentTag = (MainTabs.SelectedItem as TabViewItem)?.Tag?.ToString();
            switch (currentTag)
            {
                case "EasyMode": _easyModePage?.ViewModel.ClearLogCommand.Execute(null); break;
                case "WimDeploy": if (_wimDeployPage != null) _wimDeployPage.ViewModel.DeployLog = ""; break;
                case "DiskTools": if (_diskToolsPage != null) _diskToolsPage.ViewModel.OutputLog = ""; break;
                case "AdvancedMode": _advancedModePage?.ViewModel.ClearFullLogCommand.Execute(null); break;
                case "SystemOptimizer": if (_systemOptimizerPage != null) _systemOptimizerPage.ViewModel.ConsoleLog = ""; break;
            }
            e.Handled = true;
        };
        NormalView.KeyboardAccelerators.Add(clearLogAccel);

        // Ctrl+, for Settings
        var settingsAccel = new KeyboardAccelerator { Key = (VirtualKey)188, Modifiers = VirtualKeyModifiers.Control };
        settingsAccel.Invoked += (s, e) =>
        {
            for (int i = 0; i < MainTabs.TabItems.Count; i++)
            {
                if ((MainTabs.TabItems[i] as TabViewItem)?.Tag?.ToString() == "Settings")
                {
                    MainTabs.SelectedIndex = i;
                    e.Handled = true;
                    break;
                }
            }
        };
        NormalView.KeyboardAccelerators.Add(settingsAccel);

        // Ctrl+Plus for Zoom In
        var zoomInAccel = new KeyboardAccelerator { Key = VirtualKey.Add, Modifiers = VirtualKeyModifiers.Control };
        zoomInAccel.Invoked += (s, e) => { ZoomIn(); e.Handled = true; };
        NormalView.KeyboardAccelerators.Add(zoomInAccel);

        // Ctrl+Minus for Zoom Out
        var zoomOutAccel = new KeyboardAccelerator { Key = VirtualKey.Subtract, Modifiers = VirtualKeyModifiers.Control };
        zoomOutAccel.Invoked += (s, e) => { ZoomOut(); e.Handled = true; };
        NormalView.KeyboardAccelerators.Add(zoomOutAccel);

        // Ctrl+0 for Reset Zoom
        var zoomResetAccel = new KeyboardAccelerator { Key = VirtualKey.Number0, Modifiers = VirtualKeyModifiers.Control };
        zoomResetAccel.Invoked += (s, e) => { SetZoom(1.0); e.Handled = true; };
        NormalView.KeyboardAccelerators.Add(zoomResetAccel);

        // Esc for Dismiss
        var escAccel = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escAccel.Invoked += (s, e) =>
        {
            if (GlobalExceptionBar.IsOpen)
            {
                GlobalExceptionBar.IsOpen = false;
                e.Handled = true;
            }
        };
        NormalView.KeyboardAccelerators.Add(escAccel);
    }

    public void ApplyLanguage()
    {
        var loc = LocalizationService.Instance;

        AppTitleBar.Title = loc["AppTitle"];
        this.Title = AppTitleBar.Title;

        TabStarter.Header = loc.CurrentLanguage switch
        {
            "zh-CN" => "入门向导",
            "en-US" => "Starter Hub",
            "ja-JP" => "スタートガイド",
            _ => "入門精靈"
        };
        TabEasyMode.Header = loc["EasyMode"];
        TabWimDeploy.Header = loc["WimDeploy"];
        TabBootManager.Header = loc["BootManager"];
        TabDiskTools.Header = loc["DiskTools"];
        TabSystemHealth.Header = loc["SystemRepair"];
        TabNtfsPermissions.Header = loc["NtfsPermissions"];
        TabAdvancedMode.Header = loc["AdvancedMode"];
        TabOptimizer.Header = loc["Optimizer"];
        TabNetworkTools.Header = loc.CurrentLanguage switch
        {
            "zh-CN" => "网络工具",
            "en-US" => "Network Tools",
            "ja-JP" => "ネットワーク ツール",
            _ => "網路工具"
        };
        TabSystemPerformance.Header = loc.CurrentLanguage switch
        {
            "zh-CN" => "性能分析",
            "en-US" => "Performance",
            "ja-JP" => "パフォーマンス",
            _ => "效能分析"
        };
        TabSettings.Header = loc["Settings"];

        LanguageToggleBtn.Content = loc.CurrentLanguage switch
        {
            "zh-CN" => "🌐 简体中文",
            "en-US" => "🌐 English",
            "ja-JP" => "🌐 日本語",
            _ => "🌐 繁體中文"
        };
        ToolTipService.SetToolTip(LanguageToggleBtn, loc.CurrentLanguage switch
        {
            "zh-CN" => "切换语言 (当前: 简体中文)",
            "en-US" => "Switch Language (Current: English)",
            "ja-JP" => "言語を切り替える (現在: 日本語)",
            _ => "切換語言 (目前: 繁體中文)"
        });

        FloatTabBtn.Content = loc.CurrentLanguage switch
        {
            "zh-CN" => "🪟 浮动",
            "en-US" => "🪟 Float",
            "ja-JP" => "🪟 分離",
            _ => "🪟 浮動"
        };
        PopulateWorkspaceCombo(forceRebuild: true);
        UpdateFloatingManagerUI();
        UpdateModeSwitcherUI();
        UpdateAdminStatusUI();

        AdminWarningBar.Title = loc["AdminRequired"];
        AdminWarningBar.Message = loc["AdminRequiredMsg"];

        _starterPage?.ApplyLanguage();
        _easyModePage?.ApplyLanguage();
        _wimDeployPage?.ApplyLanguage();
        _bootManagerPage?.ApplyLanguage();
        _diskToolsPage?.ApplyLanguage();
        _systemHealthPage?.ApplyLanguage();
        _ntfsPermissionsPage?.ApplyLanguage();
        _advancedModePage?.ApplyLanguage();
        _systemOptimizerPage?.ApplyLanguage();
        _networkToolsPage?.ApplyLanguage();
        _systemPerformancePage?.ApplyLanguage();
        _settingsPage?.ApplyLanguage();
    }

    private string? _currentNavigatedTag;

    public void NavigateToTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return;

        var tabItem = MainTabs.TabItems.OfType<TabViewItem>().FirstOrDefault(t => t.Tag?.ToString() == tag);
        if (tabItem != null && !ReferenceEquals(MainTabs.SelectedItem, tabItem))
        {
            MainTabs.SelectedItem = tabItem;
        }

        if (tag == _currentNavigatedTag && ContentFrame.Content != null) return;
        _currentNavigatedTag = tag;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        DebugLogService.Instance.Debug($"Navigating to tab '{tag}'...", "Navigation");

        ContentFrame.Content = GetOrCreatePageForTag(tag);
        UpdateViewportLayout();

        sw.Stop();
        if (sw.ElapsedMilliseconds >= 200)
        {
            DebugLogService.Instance.Warning($"Tab navigation to '{tag}' took {sw.ElapsedMilliseconds}ms (Potential UI hitch detected!)", "Navigation");
        }
        else
        {
            DebugLogService.Instance.Debug($"Tab navigation to '{tag}' completed in {sw.ElapsedMilliseconds}ms (Smooth)", "Navigation");
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabs.SelectedItem is TabViewItem item)
        {
            NavigateToTag(item.Tag?.ToString());
        }
    }

    private void ZoomIn()
    {
        if (_currentZoom < 1.5)
        {
            SetZoom(Math.Min(1.5, _currentZoom + 0.1));
        }
    }

    private void ZoomOut()
    {
        if (_currentZoom > 0.8)
        {
            SetZoom(Math.Max(0.8, _currentZoom - 0.1));
        }
    }

    public void UpdateViewportLayout()
    {
        if (ContentScrollViewer == null || ContentRoot == null) return;
        var factor = Math.Clamp(_currentZoom, 0.7, 1.8);
        var viewportW = ContentScrollViewer.ActualWidth;
        var viewportH = ContentScrollViewer.ActualHeight;
        if (viewportW <= 0) return;

        // Browser Viewport logic:
        // LayoutWidth = (ActualWidth - padding) / factor
        var layoutW = Math.Max(360, (viewportW - 4) / factor);
        ContentRoot.Width = layoutW;
        RootScaleTransform.ScaleX = factor;
        RootScaleTransform.ScaleY = factor;

        if (_currentNavigatedTag == "EasyMode" && viewportH > 0)
        {
            ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var layoutH = Math.Max(520, (viewportH - 4) / factor);
            ContentRoot.Height = layoutH;
            ContentRoot.Margin = new Thickness(0);
        }
        else
        {
            ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            ContentRoot.ClearValue(FrameworkElement.HeightProperty);
            var contentH = ContentFrame?.ActualHeight ?? ContentRoot.ActualHeight;
            if (factor > 1.0 && contentH > 0)
            {
                ContentRoot.Margin = new Thickness(0, 0, 0, (contentH * (factor - 1.0)) + 30);
            }
            else
            {
                ContentRoot.Margin = new Thickness(0);
            }
        }
    }

    private void ContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewportLayout();
    }

    private void ContentFrame_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewportLayout();
    }

    private void SetZoom(double factor)
    {
        _currentZoom = factor;
        UpdateViewportLayout();

        var s = SettingsService.Instance.Current;
        if (s.ScalePercent != (int)(factor * 100))
        {
            s.ScalePercent = (int)(factor * 100);
            SettingsService.Instance.Save();
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomIn();
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();

    private void CompanionToggle_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Instance.Current;
        s.EnableCompanionAvatar = !s.EnableCompanionAvatar;
        SettingsService.Instance.Save();
        CompanionControl.Visibility = s.EnableCompanionAvatar ? Visibility.Visible : Visibility.Collapsed;
        if (s.EnableCompanionAvatar)
        {
            CompanionControl.ApplySavedSettings();
            CompanionSay("2D 守護伴侶已就位！");
        }
    }

    private void LanguageToggle_Click(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.ToggleLanguage();
    }

    public void AutoDetectZoom()
    {
        var rec = DisplayHelper.GetRecommendedScalePercent(WindowHelper.CurrentHwnd);
        SetZoom(rec / 100.0);
    }

    private void AutoZoom_Click(object sender, RoutedEventArgs e)
    {
        AutoDetectZoom();
    }

    private void AdminRestart_Click(object sender, RoutedEventArgs e)
    {
        AdminHelper.RestartAsAdmin();
    }

    private void UpdateAdminStatusUI()
    {
        bool isAdmin = AdminHelper.IsRunningAsAdmin();
        AdminWarningBar.IsOpen = !isAdmin;

        var loc = LocalizationService.Instance;
        if (isAdmin)
        {
            AdminStatusIcon.Text = "🛡️";
            AdminStatusText.Text = loc.CurrentLanguage switch
            {
                "zh-CN" => "系统管理员模式",
                "en-US" => "Administrator Mode",
                "ja-JP" => "管理者モード",
                _ => "系統管理員模式"
            };
            ToolTipService.SetToolTip(AdminStatusBtn, loc.CurrentLanguage switch
            {
                "zh-CN" => "当前以管理员权限运行，所有底层磁盘与系统配置功能完全可用",
                "en-US" => "Running with Administrator privileges. All advanced disk & system features enabled.",
                "ja-JP" => "管理者権限で実行中。すべての機能が利用可能です。",
                _ => "目前以系統管理員身分執行，所有底層磁碟與系統配置功能完全可用"
            });
        }
        else
        {
            AdminStatusIcon.Text = "👤";
            AdminStatusText.Text = loc.CurrentLanguage switch
            {
                "zh-CN" => "普通用户模式 (点击提权)",
                "en-US" => "Standard User (Click to Elevate)",
                "ja-JP" => "標準ユーザー (クリックして昇格)",
                _ => "一般使用者模式 (點擊提權)"
            };
            ToolTipService.SetToolTip(AdminStatusBtn, loc.CurrentLanguage switch
            {
                "zh-CN" => "点击立即以管理员身份重启，解锁全部高级底层磁盘功能",
                "en-US" => "Click to restart as Administrator and unlock all advanced disk features",
                "ja-JP" => "クリックして管理者として再起動し、すべての高度な機能を解放します",
                _ => "點擊立即以系統管理員身分重啟，解鎖全部底層功能"
            });
        }
    }

    private void AdminStatusBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!AdminHelper.IsRunningAsAdmin())
        {
            AdminRestart_Click(sender, e);
        }
    }

    private void CopyErrorDetails_Click(object sender, RoutedEventArgs e)
    {
        GlobalExceptionHandler.CopyToClipboard(GlobalExceptionHandler.LastException, GlobalExceptionHandler.LastExceptionContext);
        GlobalExceptionBar.Message = LocalizationService.Instance.CurrentLanguage switch
        {
            "zh-CN" => "已将完整错误记录与 StackTrace 复制至剪贴板！可直接粘贴回报。",
            "en-US" => "Error details and stack trace copied to clipboard!",
            "ja-JP" => "エラー詳細とスタックトレースをクリップボードにコピーしました！",
            _ => "已將完整錯誤記錄與 StackTrace 複製至剪貼簿！可直接貼上回報。"
        };
    }

    private void DismissError_Click(object sender, RoutedEventArgs e)
    {
        GlobalExceptionBar.IsOpen = false;
    }
}
