using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
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
    private RecoveryPage? _recoveryPage;
    private SystemHealthPage? _systemHealthPage;
    private NtfsPermissionsPage? _ntfsPermissionsPage;
    private GroupPolicyPage? _groupPolicyPage;
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

    // ── C5: coalesced settings-driven tab-strip rebuild ──
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _workspaceApplyTimer;
    private string _lastWorkspaceSignature = "";
    private string _pendingWorkspacePreset = "Master";

    // ── B15: capped, de-duplicated modal exception reporting ──
    private readonly HashSet<string> _reportedExceptionKeys = new();
    private readonly object _exceptionKeyLock = new();
    private Exception? _lastModalException;
    private int _modalExceptionCount = 0;
    private const int MaxModalExceptionDialogs = 3;

    private readonly List<TabViewItem> _allTabItems = new();
    private readonly Dictionary<string, FloatingTabWindow> _floatingWindows = new();

    /// <summary>
    /// Logical order of every known tab. Tabs that are currently docked, floating or filtered out of
    /// the active workspace keep their slot here so reordering the visible strip never drops them.
    /// </summary>
    private readonly List<string> _logicalTabOrder = new();

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
        ["Master"] = ["Starter", "EasyMode", "WimDeploy", "BootManager", "DiskTools", "Recovery", "SystemHealth", "NtfsPermissions", "GroupPolicy", "AdvancedMode", "SystemOptimizer", "NetworkTools", "SystemPerformance", "Settings"],
        ["Deploy"] = ["Starter", "WimDeploy", "BootManager", "EasyMode", "DiskTools", "Settings"],
        ["Repair"] = ["Starter", "SystemHealth", "BootManager", "DiskTools", "Recovery", "NtfsPermissions", "GroupPolicy", "SystemPerformance", "EasyMode", "Settings"],
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
            TabRecovery,
            TabSystemHealth,
            TabNtfsPermissions,
            TabGroupPolicy,
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
            if (SettingsService.Instance.Current.CloseToTray && !TrayService.IsExplicitExit)
            {
                args.Cancel = true;
                TrayService.MinimizeToTray();
                WindowsNotificationService.Instance.ShowNotification("DiskMaster Pro", LocalizationService.Instance["CloseToTrayNotice"] ?? "已縮小至系統匣後台執行", "EasyMode");
            }
        };

        this.Closed += (sender, args) =>
        {
            if (SettingsService.Instance.Current.CloseToTray && !TrayService.IsExplicitExit)
            {
                args.Handled = true;
                TrayService.MinimizeToTray();
            }
            else
            {
                TrayService.Dispose();
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
        ExplorerWatchdogService.Instance.Start();

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
                    ScheduleWorkspaceRebuild();
                }
            });
        };

        GlobalExceptionHandler.ExceptionReported += (ex, context) =>
        {
            // The InfoBar stays the always-on surface. The modal dialog is de-duplicated per exception
            // instance and per type+message, and is capped per session so a repeat offender cannot
            // stack dialogs while the app keeps running.
            bool firstOccurrence = false;
            lock (_exceptionKeyLock)
            {
                firstOccurrence = _reportedExceptionKeys.Add($"{ex.GetType().FullName}:{ex.Message}");
                if (_reportedExceptionKeys.Count > 200) _reportedExceptionKeys.Clear();
            }

            bool showModal = firstOccurrence
                && !ReferenceEquals(_lastModalException, ex)
                && _modalExceptionCount < MaxModalExceptionDialogs
                && SettingsService.Instance.Current.EnablePromptWindows;

            DispatcherQueue.TryEnqueue(() =>
            {
                GlobalExceptionBar.Message = $"[{context}] {ex.Message}";
                GlobalExceptionBar.IsOpen = true;

                if (showModal && WindowHelper.RootXamlRoot != null && !_isShowingDialog)
                {
                    _lastModalException = ex;
                    _modalExceptionCount++;
                    ShowModalExceptionDialog(ex, context);
                }
            });
        };

        // Select first tab by default
        if (MainTabs.TabItems.Count > 0)
        {
            MainTabs.SelectedIndex = 0;
        }

        // Diagnostic harness: only active with "--tabchrome-selftest <report-path>". It exercises the
        // real tear-out / dock / reorder code paths and writes PASS-FAIL evidence to the report file.
        var cmdArgs = Environment.GetCommandLineArgs();
        var selfTestIndex = Array.IndexOf(cmdArgs, "--tabchrome-selftest");
        if (selfTestIndex >= 0 && selfTestIndex + 1 < cmdArgs.Length)
        {
            var reportPath = cmdArgs[selfTestIndex + 1];
            NormalView.Loaded += (_, _) => RunTabChromeSelfTestAsync(reportPath);
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

    /// <summary>
    /// B12: awaits a fire-and-forget operation and routes faults to GlobalExceptionHandler instead of
    /// letting them surface only at GC time (or not at all).
    /// </summary>
    private static async Task RunGuardedAsync(Func<Task> operation, string context)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, context);
        }
    }

    /// <summary>
    /// Minimizes the application window to the system tray on startup when launched with --background.
    /// </summary>
    public void MinimizeToTrayOnStartup()
    {
        try
        {
            TrayService.MinimizeToTray();
        }
        catch { }
    }

    private void HandleQuickAction(string action)
    {
        switch (action)
        {
            case "Open":
                TrayService.RestoreFromTray();
                break;
            case "EasyMode":
            case "Recovery":
            case "WimDeploy":
            case "BootManager":
            case "DiskTools":
            case "SystemHealth":
            case "NtfsPermissions":
            case "GroupPolicy":
            case "SystemOptimizer":
            case "NetworkTools":
            case "SystemPerformance":
            case "Settings":
                TrayService.RestoreFromTray();
                NavigateToTag(action);
                break;
            case "Performance":
                TrayService.RestoreFromTray();
                NavigateToTag("SystemPerformance");
                break;
            case "Network":
                TrayService.RestoreFromTray();
                NavigateToTag("NetworkTools");
                break;
            case "CleanCache":
                TrayService.RestoreFromTray();
                NavigateToTag("SystemHealth");
                if (_systemHealthPage != null)
                {
                    _ = RunGuardedAsync(
                        () => _systemHealthPage.ViewModel.CleanSelectedTempCategoriesCommand.ExecuteAsync(null),
                        "QuickAction CleanCache (SystemHealth)");
                }
                break;
            case "TrimMemory":
                _ = RunGuardedAsync(async () =>
                {
                    var perf = new SystemPerformanceService();
                    var res = await perf.TrimProcessWorkingSetsAsync();
                    WindowsNotificationService.Instance.ShowNotification("DiskMaster 記憶體最佳化", res, "SystemPerformance");
                }, "QuickAction TrimMemory");
                break;
            case "FlushDns":
                _ = RunGuardedAsync(async () =>
                {
                    var tcp = new TcpOptimizerService();
                    var msg = await tcp.FlushDnsAsync();
                    WindowsNotificationService.Instance.ShowNotification("DiskMaster DNS 快取刷新", msg, "NetworkTools");
                }, "QuickAction FlushDns");
                break;
            case "ToggleOverlay":
                DiskMasterWinUI.Controls.PerformanceOverlayWindow.Toggle();
                break;
            case "RestartAdmin":
                AdminHelper.RestartAsAdmin();
                break;
            case "Exit":
                TrayService.IsExplicitExit = true;
                DispatcherQueue.TryEnqueue(async () =>
                {
                    try { DiskMasterWinUI.Controls.PerformanceOverlayWindow.CurrentInstance?.Close(); } catch { }
                    try { ExplorerWatchdogService.Instance.Stop(); } catch { }
                    foreach (var win in _floatingWindows.Values.ToList())
                    {
                        try { win.Close(); } catch { }
                    }
                    TrayService.Dispose();
                    try { this.Close(); } catch { }
                    try { Application.Current.Exit(); } catch { }
                    await Task.Delay(300);
                    Environment.Exit(0);
                });
                break;
        }
    }

    public static MainWindow? CurrentInstance { get; private set; }

    public void ApplyCustomTabOrder(IEnumerable<string> order, IEnumerable<string> disabledTags)
    {
        var requested = order?.Where(t => !string.IsNullOrEmpty(t)).ToList() ?? new List<string>();
        var s = SettingsService.Instance.Current;

        if (requested.Count > 0)
        {
            // Resync the logical order (all known tabs, including docked / floating ones) from the
            // settings UI so later SaveCustomTabOrder calls keep every slot consistent.
            GetFullLogicalOrder();
            _logicalTabOrder.RemoveAll(tag => requested.Contains(tag));
            _logicalTabOrder.InsertRange(0, requested);
            foreach (var tag in WorkspaceTabTags["Master"])
            {
                if (!_logicalTabOrder.Contains(tag)) _logicalTabOrder.Add(tag);
            }

            s.CustomTabOrder = _logicalTabOrder.ToList();
        }

        if (disabledTags != null)
        {
            s.DisabledTabTags = disabledTags.ToList();
        }

        SettingsService.Instance.Save();
        ApplyWorkspacePreset(s.WorkspacePreset ?? "Master");
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

    private void NormalView_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
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

    /// <summary>
    /// Everything about the settings that can change the tab strip / workspace layout.
    /// Used to skip the expensive strip rebuild when an unrelated setting changed.
    /// </summary>
    private static string ComputeWorkspaceSignature()
    {
        var s = SettingsService.Instance.Current;
        return string.Join("|",
            s.WorkspacePreset ?? "Master",
            s.IsEasyMode ? "1" : "0",
            s.EnableCustomTabReorder ? "1" : "0",
            string.Join(",", s.CustomTabOrder ?? new List<string>()),
            string.Join(",", s.DisabledTabTags ?? new List<string>()));
    }

    /// <summary>
    /// C5: SettingsService.Save() is raised by every zoom click, companion drag and settings toggle,
    /// each of which used to trigger a synchronous Clear()+re-add+NavigateToTag of the whole tab strip.
    /// The rebuild is now coalesced into one timer tick after the burst, and skipped entirely when the
    /// setting cannot affect the tab strip. Workspace combobox / tab-order changes still call
    /// ApplyWorkspacePreset directly, so user-visible workspace changes stay immediate.
    /// </summary>
    private void ScheduleWorkspaceRebuild()
    {
        var signature = ComputeWorkspaceSignature();
        if (signature == _lastWorkspaceSignature) return;

        _pendingWorkspacePreset = SettingsService.Instance.Current.WorkspacePreset ?? "Master";

        if (_workspaceApplyTimer == null)
        {
            _workspaceApplyTimer = DispatcherQueue.CreateTimer();
            _workspaceApplyTimer.Interval = TimeSpan.FromMilliseconds(200);
            _workspaceApplyTimer.IsRepeating = false;
            _workspaceApplyTimer.Tick += (_, _) =>
            {
                _workspaceApplyTimer?.Stop();
                if (_isApplyingWorkspacePreset) return;

                var current = ComputeWorkspaceSignature();
                if (current == _lastWorkspaceSignature) return;
                _lastWorkspaceSignature = current;

                ApplyWorkspacePreset(_pendingWorkspacePreset);
                PopulateWorkspaceCombo(forceRebuild: false);
            };
        }

        _workspaceApplyTimer.Stop();
        _workspaceApplyTimer.Start();
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
            else if (ContentFrame.Content is Page stalePage &&
                     (ReferenceEquals(RightDockFrame.Content, stalePage) ||
                      ReferenceEquals(BottomDockFrame.Content, stalePage) ||
                      _floatingWindows.Values.Any(w => ReferenceEquals(w.HostedPage, stalePage))))
            {
                // No tab is available in this preset: never leave a page parented by the central frame
                // while a dock pane / floating window owns it too.
                ContentFrame.Content = null;
            }
        }
        finally
        {
            _isApplyingWorkspacePreset = false;
            // Record the applied state so a Save() that follows this rebuild does not rebuild again.
            _lastWorkspaceSignature = ComputeWorkspaceSignature();
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

    public Page GetOrCreatePageForTag(string tag)
    {
        try
        {
            return tag switch
            {
                "Starter" => _starterPage ??= new StarterPage(),
                "EasyMode" => _easyModePage ??= new EasyModePage(),
                "WimDeploy" => _wimDeployPage ??= new WimDeployPage(),
                "BootManager" => _bootManagerPage ??= new BootManagerPage(),
                "DiskTools" => _diskToolsPage ??= new DiskToolsPage(),
                "Recovery" => _recoveryPage ??= new RecoveryPage(),
                "SystemHealth" => _systemHealthPage ??= new SystemHealthPage(),
                "NtfsPermissions" => _ntfsPermissionsPage ??= new NtfsPermissionsPage(),
                "GroupPolicy" => _groupPolicyPage ??= new GroupPolicyPage(),
                "AdvancedMode" => _advancedModePage ??= new AdvancedModePage(),
                "SystemOptimizer" => _systemOptimizerPage ??= new SystemOptimizerPage(),
                "NetworkTools" => _networkToolsPage ??= new NetworkToolsPage(),
                "SystemPerformance" => _systemPerformancePage ??= new SystemPerformancePage(),
                "Settings" => _settingsPage ??= new SettingsPage(),
                _ => new Page()
            };
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, $"GetOrCreatePageForTag({tag})");
            return new Page();
        }
    }

    // ── Single-parent Page ownership ──
    // A Page may only ever be the child of one host: ContentFrame, RightDockFrame, BottomDockFrame or
    // exactly one FloatingTabWindow. Every re-parenting path below detaches first, so XAML can never
    // throw "Element is already the child of another element." / "Value does not fall within the
    // expected range." (the FloatingTabWindow..ctor crash).

    /// <summary>
    /// Releases <paramref name="page"/> from every host frame and floating window that currently owns it.
    /// </summary>
    public void DetachPageFromAllHosts(Page? page)
    {
        if (page == null) return;

        if (ReferenceEquals(ContentFrame.Content, page)) ContentFrame.Content = null;
        if (ReferenceEquals(RightDockFrame.Content, page)) RightDockFrame.Content = null;
        if (ReferenceEquals(BottomDockFrame.Content, page)) BottomDockFrame.Content = null;

        foreach (var win in _floatingWindows.Values.ToList())
        {
            if (ReferenceEquals(win.HostedPage, page))
            {
                win.DetachHostedPage();
            }
        }
    }

    /// <summary>
    /// Removes the tab of <paramref name="tag"/> from the tab strip. The caller re-hosts the page, so
    /// the central frame is released here (exactly one host at any time).
    /// </summary>
    private TabViewItem? RemoveTabFromStrip(string tag)
    {
        var tabItem = MainTabs.TabItems.OfType<TabViewItem>().FirstOrDefault(t => t.Tag?.ToString() == tag);
        if (tabItem == null) return null;

        var page = GetOrCreatePageForTag(tag);
        MainTabs.TabItems.Remove(tabItem);
        DetachPageFromAllHosts(page);
        if (MainTabs.TabItems.Count > 0 && MainTabs.SelectedItem == null)
        {
            MainTabs.SelectedIndex = 0;
        }
        return tabItem;
    }

    /// <summary>
    /// Closes the floating window of <paramref name="tag"/> without letting the window dock itself.
    /// The page is detached; when <paramref name="dockBackToTabs"/> is true the tab returns to the strip.
    /// </summary>
    private void CloseFloatingWindowFor(string tag, bool dockBackToTabs)
    {
        if (!_floatingWindows.TryGetValue(tag, out var win)) return;

        _floatingWindows.Remove(tag);
        win.DetachAndClose();

        if (dockBackToTabs)
        {
            ReinsertTabToMainTabs(tag);
        }
    }

    /// <summary>The full logical tab order (includes docked / floating / filtered-out tabs).</summary>
    private List<string> GetFullLogicalOrder()
    {
        if (_logicalTabOrder.Count == 0)
        {
            var knownTags = WorkspaceTabTags["Master"];
            var saved = SettingsService.Instance.Current.CustomTabOrder;
            if (saved != null)
            {
                foreach (var tag in saved)
                {
                    if (knownTags.Contains(tag) && !_logicalTabOrder.Contains(tag)) _logicalTabOrder.Add(tag);
                }
            }
            foreach (var tag in knownTags)
            {
                if (!_logicalTabOrder.Contains(tag)) _logicalTabOrder.Add(tag);
            }
        }
        return _logicalTabOrder;
    }

    /// <summary>Tags visible in the active workspace preset, easy mode and disabled-tab filters.</summary>
    private List<string> GetAllowedTagsForCurrentWorkspace()
    {
        var s = SettingsService.Instance.Current;
        var preset = s.WorkspacePreset ?? "Master";
        if (!WorkspaceTabTags.TryGetValue(preset, out var allowedTags))
        {
            allowedTags = WorkspaceTabTags["Master"];
        }

        if (s.IsEasyMode)
        {
            allowedTags = allowedTags.Where(tag => EasyModeAllowedTags.Contains(tag)).ToArray();
        }

        if (s.DisabledTabTags != null && s.DisabledTabTags.Count > 0)
        {
            allowedTags = allowedTags.Where(tag => !s.DisabledTabTags.Contains(tag)).ToArray();
        }

        return allowedTags.ToList();
    }

    /// <summary>
    /// Position a returning tab belongs to, derived from the persisted logical order.
    /// </summary>
    private int GetInsertIndexForTag(string tag)
    {
        var allowedTags = GetAllowedTagsForCurrentWorkspace();
        if (!allowedTags.Contains(tag)) return MainTabs.TabItems.Count;

        // When custom reordering is disabled the strip follows the workspace preset order.
        bool usePresetOrder = !SettingsService.Instance.Current.EnableCustomTabReorder;
        var reference = usePresetOrder ? allowedTags : GetFullLogicalOrder();

        int tagIndex = reference.IndexOf(tag);
        if (tagIndex < 0) return MainTabs.TabItems.Count;

        int insertIndex = 0;
        for (int i = 0; i < MainTabs.TabItems.Count; i++)
        {
            var otherTag = (MainTabs.TabItems[i] as TabViewItem)?.Tag?.ToString();
            if (otherTag == null) continue;
            int otherIndex = reference.IndexOf(otherTag);
            if (otherIndex >= 0 && otherIndex < tagIndex) insertIndex = i + 1;
        }
        return Math.Clamp(insertIndex, 0, MainTabs.TabItems.Count);
    }

    public bool IsRightDockOpen => !string.IsNullOrEmpty(_rightDockTag);

    public void DockToRight(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        if (_rightDockTag == tag) return;

        var page = GetOrCreatePageForTag(tag);

        // Release every other host that may currently own this tag/page.
        if (_bottomDockTag == tag) CloseBottomDock(reinsertToTabs: false);
        CloseFloatingWindowFor(tag, dockBackToTabs: false);
        DetachPageFromAllHosts(page);

        // A dock pane hosts exactly one page, so evict its previous tab straight back into the strip.
        if (!string.IsNullOrEmpty(_rightDockTag))
        {
            CloseRightDock(reinsertToTabs: true);
        }

        RemoveTabFromStrip(tag);

        _rightDockTag = tag;
        _rightDockPage = page;

        RightDockTitleText.Text = $"側邊分屏: {GetTabHeader(tag)}";
        RightDockFrame.Content = page;
        RightDockPanel.Visibility = Visibility.Visible;
        RightDockSplitter.Visibility = Visibility.Visible;
        RightDockColDef.Width = new GridLength(460);

        UpdateFloatingManagerUI();
        SaveCustomTabOrder();
        CompanionControl?.AutoDetectAndSnapToEdge(isRightSplitOpen: true, announce: true);
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
        SaveCustomTabOrder();
        CompanionControl?.AutoDetectAndSnapToEdge(isRightSplitOpen: false, announce: false);
    }

    public void DockToBottom(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        if (_bottomDockTag == tag) return;

        var page = GetOrCreatePageForTag(tag);

        // Release every other host that may currently own this tag/page.
        if (_rightDockTag == tag) CloseRightDock(reinsertToTabs: false);
        CloseFloatingWindowFor(tag, dockBackToTabs: false);
        DetachPageFromAllHosts(page);

        // A dock pane hosts exactly one page, so evict its previous tab straight back into the strip.
        if (!string.IsNullOrEmpty(_bottomDockTag))
        {
            CloseBottomDock(reinsertToTabs: true);
        }

        RemoveTabFromStrip(tag);

        _bottomDockTag = tag;
        _bottomDockPage = page;

        BottomDockTitleText.Text = $"底部水平分屏: {GetTabHeader(tag)}";
        BottomDockFrame.Content = page;
        BottomDockPanel.Visibility = Visibility.Visible;
        BottomDockSplitter.Visibility = Visibility.Visible;
        BottomDockRowDef.Height = new GridLength(280);

        UpdateFloatingManagerUI();
        SaveCustomTabOrder();
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
        SaveCustomTabOrder();
    }

    private void ReinsertTabToMainTabs(string tag)
    {
        var tabItem = _allTabItems.FirstOrDefault(t => t.Tag?.ToString() == tag);
        if (tabItem == null) return;

        if (!MainTabs.TabItems.Contains(tabItem))
        {
            MainTabs.TabItems.Insert(GetInsertIndexForTag(tag), tabItem);
        }

        // The page must no longer be parented by the closing window / dock pane when it comes home.
        DetachPageFromAllHosts(GetOrCreatePageForTag(tag));

        MainTabs.SelectedItem = tabItem;
        NavigateToTag(tag);
        UpdateFloatingManagerUI();
        SaveCustomTabOrder();
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
        if (tabItem == null) return;
        var tag = tabItem.Tag?.ToString();
        if (string.IsNullOrEmpty(tag) || _floatingWindows.ContainsKey(tag)) return;
        if (MainTabs.TabItems.Count <= 1) return;

        var header = tabItem.Header?.ToString() ?? tag;
        var page = GetOrCreatePageForTag(tag);

        // The tab may also be sitting in a dock pane (right-click "float" on a docked tag).
        if (_rightDockTag == tag) CloseRightDock(reinsertToTabs: false);
        if (_bottomDockTag == tag) CloseBottomDock(reinsertToTabs: false);
        CloseFloatingWindowFor(tag, dockBackToTabs: false);

        RemoveTabFromStrip(tag);
        DetachPageFromAllHosts(page);

        if (MainTabs.TabItems.Count > 0)
        {
            var nextIndex = Math.Clamp(MainTabs.SelectedIndex, 0, MainTabs.TabItems.Count - 1);
            MainTabs.SelectedIndex = nextIndex;
            if (MainTabs.SelectedItem is TabViewItem nextItem)
            {
                NavigateToTag(nextItem.Tag?.ToString());
            }
        }

        CreateFloatingWindow(tag, header, page, mousePos);
    }

    public void CreateFloatingWindow(string tag, string header, Page page, Windows.Graphics.PointInt32? mousePos = null)
    {
        if (_floatingWindows.ContainsKey(tag)) return;

        // Exactly one parent: release the page from the strip, both dock panes and any other floating
        // window before FloatingTabWindow hosts it.
        if (_rightDockTag == tag) CloseRightDock(reinsertToTabs: false);
        if (_bottomDockTag == tag) CloseBottomDock(reinsertToTabs: false);
        CloseFloatingWindowFor(tag, dockBackToTabs: false);
        DetachPageFromAllHosts(page);

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
    }

    private void MainTabs_TabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        _draggedTabItem = null;
    }

    private void MainTabs_TabDroppedOutside(TabView sender, TabViewTabDroppedOutsideEventArgs args)
    {
        var tab = args.Tab ?? _draggedTabItem;
        if (tab != null)
        {
            var cursor = DisplayHelper.GetCurrentCursorPosition();
            FloatTab(tab, cursor);
        }
        _draggedTabItem = null;
    }

    private void DockContainerGrid_DragOver(object sender, DragEventArgs e)
    {
        if (_draggedTabItem != null)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }
    }

    private void DockContainerGrid_Drop(object sender, DragEventArgs e)
    {
        if (_draggedTabItem != null)
        {
            var tab = _draggedTabItem;
            _draggedTabItem = null;
            var cursor = DisplayHelper.GetCurrentCursorPosition();
            FloatTab(tab, cursor);
        }
    }

    /// <summary>
    /// Persists the tab order. The visible strip order is projected onto the slots the visible tabs
    /// occupy in the logical order, so tabs that are docked, floating or workspace-filtered keep their
    /// persisted position instead of being pushed to the end.
    /// </summary>
    private void SaveCustomTabOrder()
    {
        try
        {
            var logical = GetFullLogicalOrder();

            var visible = MainTabs.TabItems
                .OfType<TabViewItem>()
                .Select(t => t.Tag?.ToString())
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(t => t!)
                .ToList();

            foreach (var tag in visible)
            {
                if (!logical.Contains(tag)) logical.Add(tag);
            }

            if (visible.Count > 0)
            {
                var slots = new List<int>();
                for (int i = 0; i < logical.Count; i++)
                {
                    if (visible.Contains(logical[i])) slots.Add(i);
                }

                for (int i = 0; i < slots.Count && i < visible.Count; i++)
                {
                    logical[slots[i]] = visible[i];
                }
            }

            var s = SettingsService.Instance.Current;
            s.CustomTabOrder = logical.ToList();
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
        win.DetachHostedPage();
        _floatingWindows.Remove(tag);
        ReinsertTabToMainTabs(tag);
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

    // ── Diagnostic self-test harness (only runs with --tabchrome-selftest <report-path>) ──
    // Exercises the exact production code paths used by drag tear-out, the dock buttons, the
    // floating window close button and tab reordering, and writes PASS/FAIL evidence to the file.

    private async void RunTabChromeSelfTestAsync(string reportPath)
    {
        int failures = 0;
        int passes = 0;
        var log = new System.Text.StringBuilder();

        void Write(string line)
        {
            lock (log)
            {
                log.AppendLine(line);
                try { System.IO.File.WriteAllText(reportPath, log.ToString()); } catch { }
            }
        }

        bool Check(string name, bool ok, string detail = "")
        {
            if (ok) passes++; else failures++;
            Write($"{(ok ? "PASS" : "FAIL")}\t{name}\t{detail}");
            return ok;
        }

        bool Probe(string name, Func<bool> probe)
        {
            try { return Check(name, probe(), ""); }
            catch (Exception ex) { return Check(name, false, ex.GetType().Name + ": " + ex.Message); }
        }

        void Skip(string name, string reason) => Write($"SKIP\t{name}\t{reason}");

        static string SettingsFilePath() => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskMaster_Portable", "settings.json");

        static bool SettingsFileWritable()
        {
            try
            {
                using var fs = new System.IO.FileStream(SettingsFilePath(), System.IO.FileMode.Open, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                return true;
            }
            catch { return false; }
        }

        static Frame? OwningFrameOf(Page p)
        {
            var node = VisualTreeHelper.GetParent(p);
            while (node != null)
            {
                if (node is Frame frame) return frame;
                node = VisualTreeHelper.GetParent(node);
            }
            return null;
        }

        static List<string>? OnDiskOrder()
        {
            try
            {
                var path = SettingsFilePath();
                if (!System.IO.File.Exists(path)) return null;
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("CustomTabOrder", out var arr)) return null;
                return arr.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
            }
            catch { return null; }
        }

        var settings = SettingsService.Instance.Current;
        var savedOrder = settings.CustomTabOrder?.ToList() ?? new List<string>();
        var savedDisabled = settings.DisabledTabTags?.ToList() ?? new List<string>();
        var savedPreset = settings.WorkspacePreset;
        var savedEasyMode = settings.IsEasyMode;

        const string tag = "Settings";
        var header = GetTabHeader(tag);
        var page = GetOrCreatePageForTag(tag);

        Write($"=== DiskMaster Pro tab chrome self-test {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        try
        {
            settings.WorkspacePreset = "Master";
            settings.IsEasyMode = false;
            settings.DisabledTabTags = new List<string>();
            _logicalTabOrder.Clear();
            SettingsService.Instance.Save();
            await Task.Delay(400);
            ApplyWorkspacePreset("Master");
            await Task.Delay(400);

            var tabItem = MainTabs.TabItems.OfType<TabViewItem>().FirstOrDefault(t => t.Tag?.ToString() == tag);
            if (!Check("0.1 tab exists in strip", tabItem != null)) return;
            MainTabs.SelectedItem = tabItem;
            await Task.Delay(400);
            ContentFrame.UpdateLayout();
            Probe("0.2 central frame owns page", () => ReferenceEquals(ContentFrame.Content, page));
            Probe("0.3 page visual parent is ContentFrame", () => ReferenceEquals(OwningFrameOf(page), ContentFrame));

            // ── 1. Tear out by drag (entry point used by TabDroppedOutside) ──
            FloatTab(tabItem!, DisplayHelper.GetCurrentCursorPosition());
            await Task.Delay(900);
            var floated = _floatingWindows.TryGetValue(tag, out var win);
            Check("1.1 floating window registered", floated);
            Probe("1.2 page detached from central frame", () => !ReferenceEquals(ContentFrame.Content, page));
            Probe("1.3 page detached from both dock panes",
                () => !ReferenceEquals(RightDockFrame.Content, page) && !ReferenceEquals(BottomDockFrame.Content, page));
            Check("1.4 tab removed from strip", !MainTabs.TabItems.Contains(tabItem!));
            if (!floated || win == null) return;
            win.ContentFrame.UpdateLayout();
            Probe("1.5 floating window hosts page", () => win.IsHostingPage);
            Probe("1.6 page visual parent is the floating frame", () => ReferenceEquals(OwningFrameOf(page), win.ContentFrame));

            // ── 2. Dock back into the tab strip (path of the '嵌回主視窗' button) ──
            win.PerformDock();
            await Task.Delay(900);
            NormalView.UpdateLayout();
            Check("2.1 floating window unregistered", !_floatingWindows.ContainsKey(tag));
            Probe("2.2 floating frame released the page", () => !win.IsHostingPage);
            Check("2.3 tab restored to the strip", MainTabs.TabItems.Contains(tabItem!));
            Check("2.4 tab header preserved", tabItem!.Header?.ToString() == header, $"{tabItem.Header}");
            Probe("2.5 page back in the central frame", () => ReferenceEquals(ContentFrame.Content, page));
            Probe("2.6 page visual parent is ContentFrame again", () => ReferenceEquals(OwningFrameOf(page), ContentFrame));

            // ── 3. Dock right, then dock back ──
            DockToRight(tag);
            await Task.Delay(600);
            NormalView.UpdateLayout();
            Check("3.1 tag registered as right-docked", _rightDockTag == tag);
            Probe("3.2 right dock frame owns page", () => ReferenceEquals(RightDockFrame.Content, page));
            Probe("3.3 page visual parent is RightDockFrame", () => ReferenceEquals(OwningFrameOf(page), RightDockFrame));
            CloseRightDock(reinsertToTabs: true);
            await Task.Delay(600);
            Check("3.4 tab re-inserted after dock closed", MainTabs.TabItems.Contains(tabItem!));
            Probe("3.5 page back in the central frame", () => ReferenceEquals(ContentFrame.Content, page));

            // ── 4. Dock bottom, then dock back ──
            DockToBottom(tag);
            await Task.Delay(600);
            NormalView.UpdateLayout();
            Check("4.1 tag registered as bottom-docked", _bottomDockTag == tag);
            Probe("4.2 bottom dock frame owns page", () => ReferenceEquals(BottomDockFrame.Content, page));
            CloseBottomDock(reinsertToTabs: true);
            await Task.Delay(600);
            Check("4.3 tab re-inserted after bottom dock closed", MainTabs.TabItems.Contains(tabItem!));
            Probe("4.4 page back in the central frame", () => ReferenceEquals(ContentFrame.Content, page));

            // ── 5. Right dock -> float, then close the floating window (X) ──
            DockToRight(tag);
            await Task.Delay(500);
            CloseRightDock(reinsertToTabs: false);
            CreateFloatingWindow(tag, header, page);
            await Task.Delay(900);
            var reopened = _floatingWindows.TryGetValue(tag, out var win5);
            Check("5.1 tag floated again from the right dock", reopened);
            if (reopened && win5 != null)
            {
                win5.Close();                       // identical to the user clicking the window's X
                await Task.Delay(900);
                NormalView.UpdateLayout();
                Check("5.2 floating window unregistered after close", !_floatingWindows.ContainsKey(tag));
                Check("5.3 tab returned to the strip after close", MainTabs.TabItems.Contains(tabItem!));
                Probe("5.4 page re-hosted in the central frame", () => ReferenceEquals(ContentFrame.Content, page));
                Probe("5.5 single parent after close", () => ReferenceEquals(OwningFrameOf(page), ContentFrame));
            }

            // ── 6. Reorder (switch positions) while another tab is docked, then simulate a restart ──
            DockToRight("SystemHealth");
            await Task.Delay(600);
            var visibleBefore = MainTabs.TabItems.OfType<TabViewItem>().Select(t => t.Tag?.ToString()).ToList();
            var moveTab = MainTabs.TabItems[0] as TabViewItem;
            var moveTag = moveTab?.Tag?.ToString();
            int dockedSlotBefore = GetFullLogicalOrder().IndexOf("SystemHealth");
            if (Check("6.1 reorder candidate available", moveTab != null && visibleBefore.Count > 2))
            {
                TabReorderHelper.MoveTab(MainTabs, moveTab!, +1, SaveCustomTabOrder);
                await Task.Delay(600);

                var visibleAfter = MainTabs.TabItems.OfType<TabViewItem>().Select(t => t.Tag?.ToString()).ToList();
                Check("6.2 helper swapped exactly once",
                    visibleAfter.Count == visibleBefore.Count &&
                    visibleAfter[0] == visibleBefore[1] && visibleAfter[1] == visibleBefore[0],
                    string.Join(",", visibleAfter.Take(3)));

                var persisted = SettingsService.Instance.Current.CustomTabOrder?.ToList() ?? new List<string>();
                var onDisk = OnDiskOrder();
                bool diskWritable = SettingsFileWritable();
                Check("6.3 docked tag keeps its logical slot", persisted.IndexOf("SystemHealth") == dockedSlotBefore,
                    $"slot {persisted.IndexOf("SystemHealth")} (expected {dockedSlotBefore})");
                Check("6.4 moved tag follows visible order",
                    moveTag != null && persisted.IndexOf(moveTag) > persisted.IndexOf(visibleBefore[1] ?? ""),
                    $"'{moveTag ?? ""}' at {persisted.IndexOf(moveTag ?? "")}");
                if (diskWritable)
                {
                    Check("6.5 order written to settings.json", onDisk != null && onDisk.SequenceEqual(persisted),
                        onDisk == null ? "settings.json unreadable" : string.Join(",", onDisk));
                }
                else
                {
                    Skip("6.5 order written to settings.json", "sandbox denies writes to %LOCALAPPDATA%");
                }

                // Simulated restart: the order must survive the settings persistence format and rebuild
                // the strip exactly like a cold start does. The JSON round trip below is precisely what
                // SettingsService.Save/Load use; the real file reload is used when it is writable.
                CloseRightDock(reinsertToTabs: true);
                await Task.Delay(500);

                List<string> restoredExpected;
                if (diskWritable)
                {
                    SettingsService.Instance.Load();
                    restoredExpected = SettingsService.Instance.Current.CustomTabOrder?.ToList() ?? new List<string>();
                }
                else
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(SettingsService.Instance.Current);
                    var reloaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    restoredExpected = reloaded.CustomTabOrder?.ToList() ?? new List<string>();
                    SettingsService.Instance.Current.CustomTabOrder = restoredExpected;
                }

                Check("6.6 persisted order survives settings round trip",
                    restoredExpected.SequenceEqual(persisted), string.Join(",", restoredExpected));

                _logicalTabOrder.Clear();
                ApplyWorkspacePreset(SettingsService.Instance.Current.WorkspacePreset ?? "Master");
                await Task.Delay(700);

                var restoredVisible = MainTabs.TabItems.OfType<TabViewItem>().Select(t => t.Tag?.ToString()).ToList();
                Check("6.7 restart restores persisted order", restoredVisible.SequenceEqual(restoredExpected),
                    string.Join(",", restoredVisible));
                Check("6.8 docked tag present after restart", restoredVisible.Contains("SystemHealth"));
                Check("6.9 moved tag at its persisted position",
                    moveTag != null && restoredVisible.IndexOf(moveTag) == restoredExpected.IndexOf(moveTag));
            }

            // ── 7. C5: settings-driven rebuild must be coalesced (not synchronous) and must apply ──
            bool HasTab(string t) => MainTabs.TabItems.OfType<TabViewItem>().Any(x => x.Tag?.ToString() == t);
            var s7 = SettingsService.Instance.Current;
            var disabledBefore = s7.DisabledTabTags?.ToList() ?? new List<string>();
            s7.DisabledTabTags = new List<string> { "GroupPolicy" };
            ScheduleWorkspaceRebuild();
            Check("7.1 rebuild is deferred, not synchronous", HasTab("GroupPolicy"));
            await Task.Delay(700);
            Check("7.2 debounced rebuild applied the visibility change", !HasTab("GroupPolicy"));
            s7.DisabledTabTags = disabledBefore;
            ScheduleWorkspaceRebuild();
            await Task.Delay(700);
            Check("7.3 debounced rebuild restored the tab", HasTab("GroupPolicy"));
        }
        catch (Exception ex)
        {
            Check("FATAL self-test exception", false, ex.ToString());
        }
        finally
        {
            try
            {
                if (_floatingWindows.ContainsKey(tag)) CloseFloatingWindowFor(tag, dockBackToTabs: false);
                CloseRightDock(reinsertToTabs: true);
                CloseBottomDock(reinsertToTabs: true);

                var restore = SettingsService.Instance.Current;
                restore.CustomTabOrder = savedOrder;
                restore.DisabledTabTags = savedDisabled;
                restore.WorkspacePreset = savedPreset;
                restore.IsEasyMode = savedEasyMode;
                _logicalTabOrder.Clear();
                SettingsService.Instance.Save();
                ApplyWorkspacePreset(savedPreset ?? "Master");
            }
            catch { }

            Write($"=== SELFTEST RESULT: {(failures == 0 ? "PASS" : "FAIL")} — {passes} passed, {failures} failed ===");
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

    private void TabContext_HideTab_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetTabFromSender(sender);
        var tag = tab?.Tag?.ToString();
        if (string.IsNullOrEmpty(tag) || tag == "Settings" || MainTabs.TabItems.Count <= 1) return;

        var s = SettingsService.Instance.Current;
        s.DisabledTabTags ??= new List<string>();
        if (!s.DisabledTabTags.Contains(tag))
        {
            s.DisabledTabTags.Add(tag);
            SettingsService.Instance.Save();
            ApplyWorkspacePreset(s.WorkspacePreset ?? "Master");
        }
    }

    private void TabContext_ResetOrder_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Instance.Current;
        s.CustomTabOrder = WorkspaceTabTags["Master"].ToList();
        s.DisabledTabTags = new List<string>();
        _logicalTabOrder.Clear();
        SettingsService.Instance.Save();
        ApplyWorkspacePreset(s.WorkspacePreset ?? "Master");
    }

    private async void TabContext_ExportDiag_Click(object sender, RoutedEventArgs e)
    {
        await DiagnosticExportService.Instance.ExportReportToFileAsync(autoOpenFile: true);
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
            case "GroupPolicy":
                _groupPolicyPage?.ViewModel.RefreshStatus();
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

        // Ctrl+L for Clear Log (with Undo backup via Ctrl+Z)
        var clearLogAccel = new KeyboardAccelerator { Key = VirtualKey.L, Modifiers = VirtualKeyModifiers.Control };
        clearLogAccel.Invoked += (s, e) =>
        {
            var currentTag = (MainTabs.SelectedItem as TabViewItem)?.Tag?.ToString();
            switch (currentTag)
            {
                case "EasyMode":
                    if (_easyModePage != null && !string.IsNullOrEmpty(_easyModePage.ViewModel.OutputLog))
                    {
                        var snap = _easyModePage.ViewModel.OutputLog;
                        UndoCutManager.PushUndo("清除簡易模式紀錄", () => _easyModePage.ViewModel.OutputLog = snap);
                        _easyModePage.ViewModel.ClearLogCommand.Execute(null);
                    }
                    break;
                case "WimDeploy":
                    if (_wimDeployPage != null && !string.IsNullOrEmpty(_wimDeployPage.ViewModel.DeployLog))
                    {
                        var snap = _wimDeployPage.ViewModel.DeployLog;
                        UndoCutManager.PushUndo("清除 WIM 部署紀錄", () => _wimDeployPage.ViewModel.DeployLog = snap);
                        _wimDeployPage.ViewModel.DeployLog = "";
                    }
                    break;
                case "DiskTools":
                    if (_diskToolsPage != null && !string.IsNullOrEmpty(_diskToolsPage.ViewModel.OutputLog))
                    {
                        var snap = _diskToolsPage.ViewModel.OutputLog;
                        UndoCutManager.PushUndo("清除磁碟工具紀錄", () => _diskToolsPage.ViewModel.OutputLog = snap);
                        _diskToolsPage.ViewModel.OutputLog = "";
                    }
                    break;
                case "AdvancedMode":
                    if (_advancedModePage != null && !string.IsNullOrEmpty(_advancedModePage.ViewModel.FullLog))
                    {
                        var snap = _advancedModePage.ViewModel.FullLog;
                        UndoCutManager.PushUndo("清除進階模式紀錄", () => _advancedModePage.ViewModel.FullLog = snap);
                        _advancedModePage.ViewModel.ClearFullLogCommand.Execute(null);
                    }
                    break;
                case "SystemOptimizer":
                    if (_systemOptimizerPage != null && !string.IsNullOrEmpty(_systemOptimizerPage.ViewModel.ConsoleLog))
                    {
                        var snap = _systemOptimizerPage.ViewModel.ConsoleLog;
                        UndoCutManager.PushUndo("清除系統優化紀錄", () => _systemOptimizerPage.ViewModel.ConsoleLog = snap);
                        _systemOptimizerPage.ViewModel.ConsoleLog = "";
                    }
                    break;
            }
            e.Handled = true;
        };
        NormalView.KeyboardAccelerators.Add(clearLogAccel);

        // Global & per-page Ctrl+Z (Undo) and Ctrl+X (Cut) support across all 14 pages
        UndoCutManager.Attach(NormalView, () => ContentFrame.Content as Page);

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
        _recoveryPage?.ApplyLanguage();
        _systemHealthPage?.ApplyLanguage();
        _ntfsPermissionsPage?.ApplyLanguage();
        _advancedModePage?.ApplyLanguage();
        _systemOptimizerPage?.ApplyLanguage();
        _networkToolsPage?.ApplyLanguage();
        _systemPerformancePage?.ApplyLanguage();
        _settingsPage?.ApplyLanguage();
    }

    private string? _currentNavigatedTag;
    private bool _isNavigating;

    public void NavigateToTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        if (_isNavigating) return;
        _isNavigating = true;

        try
        {
            // A page that is currently docked or floating must come home before it can be shown in the
            // central frame, otherwise it would end up with two parents.
            if (_floatingWindows.ContainsKey(tag))
            {
                CloseFloatingWindowFor(tag, dockBackToTabs: true);
            }
            if (_rightDockTag == tag)
            {
                CloseRightDock(reinsertToTabs: true);
            }
            if (_bottomDockTag == tag)
            {
                CloseBottomDock(reinsertToTabs: true);
            }

            var tabItem = MainTabs.TabItems.OfType<TabViewItem>().FirstOrDefault(t => t.Tag?.ToString() == tag);
            if (tabItem != null && !ReferenceEquals(MainTabs.SelectedItem, tabItem))
            {
                MainTabs.SelectedItem = tabItem;
            }

            var page = GetOrCreatePageForTag(tag);

            if (tag == _currentNavigatedTag && ReferenceEquals(ContentFrame.Content, page)) return;
            _currentNavigatedTag = tag;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DebugLogService.Instance.Debug($"Navigating to tab '{tag}'...", "Navigation");

            DetachPageFromAllHosts(page);
            ContentFrame.Content = page;
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
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, $"NavigateToTag({tag})");
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isNavigating || _isApplyingWorkspacePreset) return;
        try
        {
            if (MainTabs.SelectedItem is TabViewItem item)
            {
                NavigateToTag(item.Tag?.ToString());
            }
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "MainTabs_SelectionChanged");
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
        var viewportW = ContentScrollViewer.ActualWidth;
        var viewportH = ContentScrollViewer.ActualHeight;
        if (viewportW <= 0) return;

        var contentH = ContentFrame?.ActualHeight ?? ContentRoot.ActualHeight;
        var (layoutW, offsetX, bottomMargin, factor) = DisplayHelper.CalculateScaledViewportLayout(
            viewportW, viewportH, contentH, _currentZoom);

        ContentRoot.Width = layoutW;
        RootScaleTransform.ScaleX = factor;
        RootScaleTransform.ScaleY = factor;

        if (_currentNavigatedTag == "EasyMode" && viewportH > 0)
        {
            ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var layoutH = Math.Max(520, (viewportH - 4) / factor);
            ContentRoot.Height = layoutH;
            ContentRoot.Margin = new Thickness(offsetX, 0, 0, 0);
        }
        else
        {
            ContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            ContentRoot.ClearValue(FrameworkElement.HeightProperty);
            ContentRoot.Margin = new Thickness(offsetX, 0, 0, bottomMargin);
        }

        if (ContentScrollViewer.HorizontalOffset != 0)
        {
            ContentScrollViewer.ChangeView(0, null, null, disableAnimation: true);
        }

        CompanionControl?.AutoDetectAndSnapToEdge(IsRightDockOpen, announce: false);
    }

    private void ContentScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (ContentScrollViewer != null && ContentScrollViewer.HorizontalOffset != 0)
        {
            ContentScrollViewer.ChangeView(0, null, null, disableAnimation: true);
        }
    }

    private void ContentRoot_BringIntoViewRequested(UIElement sender, BringIntoViewRequestedEventArgs args)
    {
        // Keep vertical BringIntoView working while clamping horizontal X to 0 so <100% scaled viewports never scroll horizontally
        var r = args.TargetRect;
        if (!double.IsNaN(r.X) && r.X != 0)
        {
            args.TargetRect = new Windows.Foundation.Rect(0, r.Y, Math.Min(1.0, Math.Max(0.0, r.Width)), r.Height);
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
