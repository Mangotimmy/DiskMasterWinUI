using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly TcpOptimizerService _tcpOptimizer = new();

    // ── Tab Reordering & Visibility Management ──
    public ObservableCollection<TabOrderItem> TabOrderItems { get; } = new();
    [ObservableProperty] private bool _enableCustomTabReorder = true;

    // ── Notification Sound Scheme ──
    public string[] NotificationSoundSchemeOptions { get; } =
    [
        "預設系統音效 (System Default)",
        "清脆風鈴 (Chimes)",
        "晶瑩提示 (Ding)",
        "經典號角 (Tada)",
        "柔和水晶 (Crystal)",
        "自訂音效檔案 (.wav)"
    ];
    [ObservableProperty] private string _selectedNotificationSoundScheme = "預設系統音效 (System Default)";
    [ObservableProperty] private string _customNotificationSoundPath = "";
    [ObservableProperty] private bool _isCustomNotificationSoundVisible;

    [ObservableProperty] private string _selectedTheme = "System";
    [ObservableProperty] private int _selectedScale = 100;
    [ObservableProperty] private string _selectedLanguage = "🌐 跟隨 Windows 系統語言 (Auto)";
    [ObservableProperty] private bool _enableSafetyConfirmations = true;
    [ObservableProperty] private string _defaultPartitionStyle = "GPT";
    [ObservableProperty] private string _defaultFileSystem = "NTFS";
    [ObservableProperty] private string _defaultBackupFolder = @"D:\Backup";
    [ObservableProperty] private int _maxLogBufferLines = 5000;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _appVersion = $"{UpdateService.CurrentVersion} Flagship (Build {DateTime.Now:yyyy.MM})";
    [ObservableProperty] private string _runningModeDescription = AppEnvironmentHelper.ExecutionModeDescription;
    [ObservableProperty] private string _systemInfo = "";
    [ObservableProperty] private string _displayMetricsDescription = "";

    // Custom Background
    [ObservableProperty] private string _backgroundImagePath = "";
    [ObservableProperty] private double _backgroundOpacity = 0.25;
    [ObservableProperty] private double _backgroundBlur = 0;

    // 3D VRM / Live2D Companion Avatar
    [ObservableProperty] private bool _enableCompanionAvatar;
    [ObservableProperty] private string _companionAvatarType = "VRM";
    [ObservableProperty] private string _companionModelPath = "";

    // Glass / Card Transparency
    [ObservableProperty] private bool _enableGlassTransparency = true;
    [ObservableProperty] private double _cardOpacity = 0.60;

    // ── Global Internet Download Configuration ──
    [ObservableProperty] private bool _httpRangeParallelEnabled = true;
    [ObservableProperty] private int _parallelThreadCount = 8;
    [ObservableProperty] private int _chunkBufferSizeKB = 256;
    [ObservableProperty] private bool _keepResumeCache = true;
    [ObservableProperty] private string _globalDownloadPath = "";
    [ObservableProperty] private string _proxyMode = "System";
    [ObservableProperty] private string _customProxyUrl = "";
    [ObservableProperty] private int _downloadTimeoutSeconds = 60;
    [ObservableProperty] private int _downloadMaxRetries = 3;

    // ── TCP Network Stack Optimization ──
    [ObservableProperty] private string _tcpAutoTuningLevel = "Normal";
    [ObservableProperty] private string _tcpCongestionProvider = "BBR";
    [ObservableProperty] private bool _tcpEcnEnabled = true;
    [ObservableProperty] private bool _tcpRssEnabled = true;
    [ObservableProperty] private bool _tcpRscEnabled = true;
    [ObservableProperty] private bool _tcpTimestampsDisabled = true;
    [ObservableProperty] private string _tcpSettingsSummary = "正在讀取 TCP 協定參數...";

    // ── Prompt Windows & Diagnostics ──
    [ObservableProperty] private bool _enablePromptWindows = true;
    [ObservableProperty] private string _diagnosticExportMessage = "";

    // ── Custom Fonts & Typography ──
    [ObservableProperty] private string _customFontFamily = "Segoe UI Variable";
    [ObservableProperty] private double _customFontScale = 100.0;
    [ObservableProperty] private string _selectedFontOption = "Segoe UI Variable";
    [ObservableProperty] private bool _isCustomFontInputVisible = false;

    // ── Adobe Workspace Presets ──
    [ObservableProperty] private string _selectedWorkspacePreset = "大師全功能";

    // ── Dual Mode & Starter Hub ──
    [ObservableProperty] private bool _isEasyMode = false;

    // ── Auto Updater ──
    [ObservableProperty] private bool _autoCheckUpdates = true;
    [ObservableProperty] private string _updateFrequency = "Daily";
    [ObservableProperty] private string _updateStatusText = "";
    [ObservableProperty] private bool _isCheckingUpdates = false;
    [ObservableProperty] private string _latestReleaseUrl = "";
    [ObservableProperty] private bool _hasAvailableUpdate = false;
    [ObservableProperty] private string _matchedAssetName = "";
    [ObservableProperty] private string _matchedAssetUrl = "";
    [ObservableProperty] private bool _isDownloadingUpdate = false;
    [ObservableProperty] private double _updateDownloadProgress = 0.0;
    [ObservableProperty] private string _downloadSpeedText = "";
    [ObservableProperty] private string _downloadedFilePath = "";
    [ObservableProperty] private bool _isDownloadCompleted = false;

    // ── System Tray & Window Notifications ──
    [ObservableProperty] private bool _minimizeToTray = false;
    [ObservableProperty] private bool _closeToTray = false;
    [ObservableProperty] private bool _enableTaskbarFlash = true;
    [ObservableProperty] private bool _enableAudioFeedback = true;
    [ObservableProperty] private bool _enableWindowsNotificationCenter = true;

    // ── Debug & Diagnostic Logging ──
    [ObservableProperty] private bool _enableDebugLogging = false;
    [ObservableProperty] private string _debugLogStatsText = "讀取中...";

    public string[] UpdateFrequencyOptions { get; } = ["Manual", "Startup", "Daily", "Weekly"];

    public string[] ThemeOptions { get; } = ["System", "Dark", "Light"];
    public int[] ScaleOptions { get; } = [80, 85, 90, 100, 110, 125, 150];
    public string[] PartitionStyleOptions { get; } = ["GPT", "MBR"];
    public string[] FileSystemOptions { get; } = ["NTFS", "FAT32", "exFAT"];
    public string[] CompanionTypeOptions { get; } = ["VRM", "Live2D"];
    public string[] ProxyModeOptions { get; } = ["System", "Direct", "Custom"];
    public int[] ParallelThreadOptions { get; } = [2, 4, 8, 16, 32];
    public int[] ChunkSizeOptions { get; } = [64, 128, 256, 512, 1024];
    public string[] TcpAutoTuningOptions { get; } = ["Normal", "Experimental", "Disabled", "Restricted"];
    public string[] TcpCongestionOptions { get; } = ["BBR", "CUBIC", "CTCP", "Default"];

    public string[] LanguageOptions { get; } =
    [
        "🌐 跟隨 Windows 系統語言 (Auto)",
        "繁體中文 (Traditional Chinese - zh-TW)",
        "简体中文 (Simplified Chinese - zh-CN)",
        "English (United States - en-US)",
        "日本語 (Japanese - ja-JP)"
    ];

    [ObservableProperty] private string[] _fontOptionList = [];
    [ObservableProperty] private string[] _workspacePresetOptions = [];

    private static readonly Dictionary<string, string> s_fontOptionToFamily = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Segoe UI Variable"] = "Segoe UI Variable",
        ["微軟正黑體"] = "Microsoft JhengHei UI",
        ["Microsoft JhengHei UI"] = "Microsoft JhengHei UI",
        ["微軟雅黑"] = "Microsoft YaHei UI",
        ["微软雅黑"] = "Microsoft YaHei UI",
        ["Microsoft YaHei UI"] = "Microsoft YaHei UI",
        ["Meiryo UI"] = "Meiryo UI",
        ["Yu Gothic UI"] = "Yu Gothic UI",
        ["Cascadia Code"] = "Cascadia Code",
        ["Consolas"] = "Consolas",
        ["JetBrains Mono"] = "JetBrains Mono"
    };

    public SettingsViewModel()
    {
        LoadSettings();
        SystemInfo = $"{Environment.OSVersion} | 64-bit: {Environment.Is64BitOperatingSystem} | Cores: {Environment.ProcessorCount}";
        try
        {
            DisplayMetricsDescription = DisplayHelper.GetDisplayMetricsDescription(WindowHelper.CurrentHwnd);
        }
        catch { }

        LocalizationService.Instance.LanguageChanged += RefreshLocalizedOptions;
        _ = RefreshTcpSettingsAsync();
    }

    public void LoadSettings()
    {
        var s = SettingsService.Instance.Current;
        SelectedTheme = s.Theme;
        SelectedScale = s.ScalePercent;

        SelectedLanguage = s.Language switch
        {
            "zh-CN" => "简体中文 (Simplified Chinese - zh-CN)",
            "en-US" => "English (United States - en-US)",
            "ja-JP" => "日本語 (Japanese - ja-JP)",
            "zh-TW" => "繁體中文 (Traditional Chinese - zh-TW)",
            _ => "🌐 跟隨 Windows 系統語言 (Auto)"
        };

        EnableSafetyConfirmations = s.EnableSafetyConfirmations;
        DefaultPartitionStyle = s.DefaultPartitionStyle;
        DefaultFileSystem = s.DefaultFileSystem;
        DefaultBackupFolder = s.DefaultBackupFolder;
        MaxLogBufferLines = s.MaxLogBufferLines;

        BackgroundImagePath = s.BackgroundImagePath;
        BackgroundOpacity = s.BackgroundOpacity;
        BackgroundBlur = s.BackgroundBlur;

        EnableCompanionAvatar = s.EnableCompanionAvatar;
        CompanionAvatarType = s.CompanionAvatarType;
        CompanionModelPath = s.CompanionModelPath;

        EnableGlassTransparency = s.EnableGlassTransparency;
        CardOpacity = s.CardOpacity;

        HttpRangeParallelEnabled = s.HttpRangeParallelEnabled;
        ParallelThreadCount = s.ParallelThreadCount;
        ChunkBufferSizeKB = s.ChunkBufferSizeKB;
        KeepResumeCache = s.KeepResumeCache;
        GlobalDownloadPath = string.IsNullOrWhiteSpace(s.GlobalDownloadPath) ? WindowsDownloadService.DefaultDownloadDirectory : s.GlobalDownloadPath;
        ProxyMode = s.ProxyMode;
        CustomProxyUrl = s.CustomProxyUrl;
        DownloadTimeoutSeconds = s.DownloadTimeoutSeconds;
        DownloadMaxRetries = s.DownloadMaxRetries;

        TcpAutoTuningLevel = s.TcpAutoTuningLevel;
        TcpCongestionProvider = s.TcpCongestionProvider;
        TcpEcnEnabled = s.TcpEcnEnabled;
        TcpRssEnabled = s.TcpRssEnabled;
        TcpRscEnabled = s.TcpRscEnabled;
        TcpTimestampsDisabled = s.TcpTimestampsDisabled;

        EnablePromptWindows = s.EnablePromptWindows;
        CustomFontFamily = string.IsNullOrWhiteSpace(s.CustomFontFamily) ? "Segoe UI Variable" : s.CustomFontFamily;
        CustomFontScale = s.CustomFontScale > 0 ? s.CustomFontScale : 100;

        IsEasyMode = s.IsEasyMode;
        AutoCheckUpdates = s.AutoCheckUpdates;
        UpdateFrequency = s.UpdateFrequency ?? "Daily";
        MinimizeToTray = s.MinimizeToTray;
        CloseToTray = s.CloseToTray;
        EnableTaskbarFlash = s.EnableTaskbarFlash;
        EnableAudioFeedback = s.EnableAudioFeedback;
        EnableWindowsNotificationCenter = s.EnableWindowsNotificationCenter;

        EnableDebugLogging = s.EnableDebugLogging;
        DebugLogService.Instance.IsEnabled = s.EnableDebugLogging;
        UpdateDebugLogStats();

        EnableCustomTabReorder = s.EnableCustomTabReorder;
        SelectedNotificationSoundScheme = (s.NotificationSoundScheme ?? "Default") switch
        {
            "Chimes" => "清脆風鈴 (Chimes)",
            "Ding" => "晶瑩提示 (Ding)",
            "Tada" => "經典號角 (Tada)",
            "Crystal" => "柔和水晶 (Crystal)",
            "Custom" => "自訂音效檔案 (.wav)",
            _ => "預設系統音效 (System Default)"
        };
        CustomNotificationSoundPath = s.CustomNotificationSoundPath ?? "";
        IsCustomNotificationSoundVisible = SelectedNotificationSoundScheme.Contains("自訂");
        LoadTabOrderItems();

        RefreshLocalizedOptions();
    }

    public void RefreshLocalizedOptions()
    {
        FontOptionList = LocalizationService.Instance.CurrentLanguage switch
        {
            "zh-TW" => ["Segoe UI Variable", "微軟正黑體", "微軟雅黑", "Meiryo UI", "Cascadia Code", "Consolas", "JetBrains Mono", "自訂字型名稱..."],
            "zh-CN" => ["Segoe UI Variable", "微软雅黑", "微软正黑体", "Meiryo UI", "Cascadia Code", "Consolas", "JetBrains Mono", "自定义字体名称..."],
            "ja-JP" => ["Segoe UI Variable", "Meiryo UI", "Yu Gothic UI", "Cascadia Code", "Consolas", "JetBrains Mono", "カスタムフォント名..."],
            _ => ["Segoe UI Variable", "Microsoft JhengHei UI", "Microsoft YaHei UI", "Meiryo UI", "Cascadia Code", "Consolas", "JetBrains Mono", "Custom Font Name..."]
        };

        WorkspacePresetOptions = [
            LocalizationService.Instance["WorkspaceMaster"],
            LocalizationService.Instance["WorkspaceDeploy"],
            LocalizationService.Instance["WorkspaceRepair"],
            LocalizationService.Instance["WorkspaceGaming"],
            LocalizationService.Instance["WorkspaceLite"]
        ];

        string customPlaceholder = LocalizationService.T("自訂字型名稱...", "自定义字体名称...", "Custom Font Name...", "カスタムフォント名...");
        var match = FontOptionList.FirstOrDefault(f =>
            s_fontOptionToFamily.TryGetValue(f, out var fam) && string.Equals(fam, CustomFontFamily, StringComparison.OrdinalIgnoreCase));
        SelectedFontOption = match ?? customPlaceholder;
        IsCustomFontInputVisible = SelectedFontOption.StartsWith("自訂") || SelectedFontOption.StartsWith("自定义") || SelectedFontOption.StartsWith("Custom") || SelectedFontOption.StartsWith("カスタム");

        var s = SettingsService.Instance.Current;
        SelectedWorkspacePreset = (s.WorkspacePreset ?? "Master") switch
        {
            "Deploy" => LocalizationService.Instance["WorkspaceDeploy"],
            "Repair" => LocalizationService.Instance["WorkspaceRepair"],
            "Gaming" => LocalizationService.Instance["WorkspaceGaming"],
            "Lite" => LocalizationService.Instance["WorkspaceLite"],
            _ => LocalizationService.Instance["WorkspaceMaster"]
        };

        var expectedLanguageOption = s.Language switch
        {
            "zh-CN" => "简体中文 (Simplified Chinese - zh-CN)",
            "en-US" => "English (United States - en-US)",
            "ja-JP" => "日本語 (Japanese - ja-JP)",
            "zh-TW" => "繁體中文 (Traditional Chinese - zh-TW)",
            _ => "🌐 跟隨 Windows 系統語言 (Auto)"
        };
        if (SelectedLanguage != expectedLanguageOption)
        {
            _isUpdatingLanguage = true;
            try
            {
                SelectedLanguage = expectedLanguageOption;
            }
            finally
            {
                _isUpdatingLanguage = false;
            }
        }
    }

    [RelayCommand]
    public void SaveSettings()
    {
        var s = SettingsService.Instance.Current;
        s.Theme = SelectedTheme;
        s.ScalePercent = SelectedScale;

        s.Language = SelectedLanguage switch
        {
            var l when l.Contains("zh-CN") => "zh-CN",
            var l when l.Contains("en-US") => "en-US",
            var l when l.Contains("ja-JP") => "ja-JP",
            var l when l.Contains("zh-TW") => "zh-TW",
            _ => "Auto"
        };

        s.EnableSafetyConfirmations = EnableSafetyConfirmations;
        s.DefaultPartitionStyle = DefaultPartitionStyle;
        s.DefaultFileSystem = DefaultFileSystem;
        s.DefaultBackupFolder = DefaultBackupFolder;
        s.MaxLogBufferLines = MaxLogBufferLines;

        s.BackgroundImagePath = BackgroundImagePath;
        s.BackgroundOpacity = BackgroundOpacity;
        s.BackgroundBlur = BackgroundBlur;

        s.EnableCompanionAvatar = EnableCompanionAvatar;
        s.CompanionAvatarType = CompanionAvatarType;
        s.CompanionModelPath = CompanionModelPath;

        s.EnableGlassTransparency = EnableGlassTransparency;
        s.CardOpacity = CardOpacity;

        s.HttpRangeParallelEnabled = HttpRangeParallelEnabled;
        s.ParallelThreadCount = ParallelThreadCount;
        s.ChunkBufferSizeKB = ChunkBufferSizeKB;
        s.KeepResumeCache = KeepResumeCache;
        s.GlobalDownloadPath = GlobalDownloadPath;
        s.ProxyMode = ProxyMode;
        s.CustomProxyUrl = CustomProxyUrl;
        s.DownloadTimeoutSeconds = DownloadTimeoutSeconds;
        s.DownloadMaxRetries = DownloadMaxRetries;

        s.TcpAutoTuningLevel = TcpAutoTuningLevel;
        s.TcpCongestionProvider = TcpCongestionProvider;
        s.TcpEcnEnabled = TcpEcnEnabled;
        s.TcpRssEnabled = TcpRssEnabled;
        s.TcpRscEnabled = TcpRscEnabled;
        s.TcpTimestampsDisabled = TcpTimestampsDisabled;

        s.EnablePromptWindows = EnablePromptWindows;
        s.CustomFontFamily = CustomFontFamily;
        s.CustomFontScale = CustomFontScale;

        s.IsEasyMode = IsEasyMode;
        s.AutoCheckUpdates = AutoCheckUpdates;
        s.UpdateFrequency = UpdateFrequency;
        s.MinimizeToTray = MinimizeToTray;
        s.CloseToTray = CloseToTray;
        s.EnableTaskbarFlash = EnableTaskbarFlash;
        s.EnableAudioFeedback = EnableAudioFeedback;
        s.EnableWindowsNotificationCenter = EnableWindowsNotificationCenter;
        s.EnableDebugLogging = EnableDebugLogging;
        DebugLogService.Instance.IsEnabled = EnableDebugLogging;

        s.WorkspacePreset = SelectedWorkspacePreset switch
        {
            var p when !string.IsNullOrEmpty(p) && (p.Contains("部署") || p.Contains("展開") || p.Contains("Deploy")) => "Deploy",
            var p when !string.IsNullOrEmpty(p) && (p.Contains("修復") || p.Contains("修复") || p.Contains("Repair")) => "Repair",
            var p when !string.IsNullOrEmpty(p) && (p.Contains("電競") || p.Contains("电竞") || p.Contains("Gaming") || p.Contains("ゲーム")) => "Gaming",
            var p when !string.IsNullOrEmpty(p) && (p.Contains("極簡") || p.Contains("极简") || p.Contains("Lite") || p.Contains("ミニマル")) => "Lite",
            _ => "Master"
        };

        s.EnableCustomTabReorder = EnableCustomTabReorder;
        s.NotificationSoundScheme = SelectedNotificationSoundScheme switch
        {
            var sch when sch.Contains("Chimes") => "Chimes",
            var sch when sch.Contains("Ding") => "Ding",
            var sch when sch.Contains("Tada") => "Tada",
            var sch when sch.Contains("Crystal") => "Crystal",
            var sch when sch.Contains("自訂") => "Custom",
            _ => "Default"
        };
        s.CustomNotificationSoundPath = CustomNotificationSoundPath;

        SettingsService.Instance.Save();
        StatusMessage = LocalizationService.T("設定已成功儲存！", "设置已成功保存！", "Settings saved successfully!", "設定が正常に保存されました！");
    }

    partial void OnSelectedNotificationSoundSchemeChanged(string value)
    {
        IsCustomNotificationSoundVisible = value.Contains("自訂");
        SaveSettings();
    }

    [RelayCommand]
    public void PlayPreviewNotificationSound()
    {
        string scheme = SelectedNotificationSoundScheme switch
        {
            var s when s.Contains("Chimes") => "Chimes",
            var s when s.Contains("Ding") => "Ding",
            var s when s.Contains("Tada") => "Tada",
            var s when s.Contains("Crystal") => "Crystal",
            var s when s.Contains("自訂") => "Custom",
            _ => "Default"
        };
        AudioFeedbackService.PlaySoundScheme(scheme, CustomNotificationSoundPath);
    }

    [RelayCommand]
    public async Task BrowseCustomNotificationSoundAsync()
    {
        var file = await FilePickerHelper.PickSingleFileAsync([".wav", ".mp3", ".wma"]);
        if (!string.IsNullOrWhiteSpace(file))
        {
            CustomNotificationSoundPath = file;
            SaveSettings();
            AudioFeedbackService.PlaySoundScheme("Custom", file);
        }
    }

    public void LoadTabOrderItems()
    {
        TabOrderItems.Clear();
        var s = SettingsService.Instance.Current;
        var disabled = new HashSet<string>(s.DisabledTabTags ?? new(), StringComparer.OrdinalIgnoreCase);

        var allDefinitions = new (string Tag, string Header, string Icon, string Desc)[]
        {
            ("Starter", "入門精靈", "\uE735", "新手引導、磁碟體檢與一鍵快速維護"),
            ("EasyMode", "簡易模式", "\uEDA2", "直覺易用的磁碟狀態檢視與日常操作"),
            ("WimDeploy", "系統部署", "\uE896", "WIM/ESD 映像套用、備份與引導建立"),
            ("BootManager", "引導管理", "\uE7C3", "UEFI/BIOS 開機項目管理與 BCD 修復"),
            ("DiskTools", "磁碟工具", "\uE90F", "S.M.A.R.T. 監控、分區管理與救援工具"),
            ("SystemHealth", "系統修復", "\uEC06", "DISM 映像修復、SFC 系統檔完整性掃描"),
            ("NtfsPermissions", "權限管理", "\uE72E", "NTFS 權限重設與擁有權奪取"),
            ("AdvancedMode", "進階腳本", "\uEC7A", "DiskPart 腳本批次執行與命令列終端"),
            ("SystemOptimizer", "最佳化", "\uE945", "CPU排程、遊戲模式與電源方案優化"),
            ("NetworkTools", "網路工具", "\uE968", "連接埠檢測、UPnP 轉發與網路診斷"),
            ("SystemPerformance", "效能分析", "\uE9D9", "即時 CPU/RAM/磁碟監控與進程獵手"),
            ("Settings", "偏好設定", "\uE713", "介面主題、字型排版與自訂配置")
        };

        var order = s.CustomTabOrder != null && s.CustomTabOrder.Count > 0 ? s.CustomTabOrder : allDefinitions.Select(d => d.Tag).ToList();

        var orderedDefs = allDefinitions.OrderBy(d =>
        {
            int idx = order.IndexOf(d.Tag);
            return idx >= 0 ? idx : int.MaxValue;
        }).ToList();

        int pos = 1;
        foreach (var def in orderedDefs)
        {
            var item = new TabOrderItem
            {
                Tag = def.Tag,
                Header = def.Header,
                IconGlyph = def.Icon,
                Description = def.Desc,
                DisplayOrder = pos++,
                IsEnabled = !disabled.Contains(def.Tag)
            };
            item.IsEnabledChanged += () => SaveAndApplyTabOrder();
            TabOrderItems.Add(item);
        }
    }

    public void SaveAndApplyTabOrder()
    {
        var s = SettingsService.Instance.Current;
        s.CustomTabOrder = TabOrderItems.Select(t => t.Tag).ToList();
        s.DisabledTabTags = TabOrderItems.Where(t => !t.IsEnabled).Select(t => t.Tag).ToList();
        SettingsService.Instance.Save();

        for (int i = 0; i < TabOrderItems.Count; i++)
        {
            TabOrderItems[i].DisplayOrder = i + 1;
        }

        MainWindow.CurrentInstance?.ApplyCustomTabOrder(s.CustomTabOrder, s.DisabledTabTags);
    }

    [RelayCommand]
    public void MoveTabUp(TabOrderItem? item)
    {
        if (item == null) return;
        int idx = TabOrderItems.IndexOf(item);
        if (idx > 0)
        {
            TabOrderItems.Move(idx, idx - 1);
            SaveAndApplyTabOrder();
            AudioFeedbackService.PlaySuccess();
        }
    }

    [RelayCommand]
    public void MoveTabDown(TabOrderItem? item)
    {
        if (item == null) return;
        int idx = TabOrderItems.IndexOf(item);
        if (idx >= 0 && idx < TabOrderItems.Count - 1)
        {
            TabOrderItems.Move(idx, idx + 1);
            SaveAndApplyTabOrder();
            AudioFeedbackService.PlaySuccess();
        }
    }

    [RelayCommand]
    public void MoveTabToTop(TabOrderItem? item)
    {
        if (item == null) return;
        int idx = TabOrderItems.IndexOf(item);
        if (idx > 0)
        {
            TabOrderItems.Move(idx, 0);
            SaveAndApplyTabOrder();
            AudioFeedbackService.PlaySuccess();
        }
    }

    [RelayCommand]
    public void MoveTabToBottom(TabOrderItem? item)
    {
        if (item == null) return;
        int idx = TabOrderItems.IndexOf(item);
        if (idx >= 0 && idx < TabOrderItems.Count - 1)
        {
            TabOrderItems.Move(idx, TabOrderItems.Count - 1);
            SaveAndApplyTabOrder();
            AudioFeedbackService.PlaySuccess();
        }
    }

    [RelayCommand]
    public void ResetTabOrder()
    {
        var s = SettingsService.Instance.Current;
        s.CustomTabOrder.Clear();
        s.DisabledTabTags.Clear();
        SettingsService.Instance.Save();
        LoadTabOrderItems();
        MainWindow.CurrentInstance?.ResetTabOrderToDefault();
        AudioFeedbackService.PlaySuccess();
    }

    [RelayCommand]
    public void ToggleTabEnabled(TabOrderItem? item)
    {
        if (item == null) return;
        SaveAndApplyTabOrder();
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        IsCheckingUpdates = true;
        UpdateStatusText = LocalizationService.T("正在檢查最新版本...", "正在检查最新版本...", "Checking for updates...", "アップデートを確認中...");
        try
        {
            var result = await UpdateService.Instance.CheckForUpdatesAsync();
            HasAvailableUpdate = result.HasUpdate;
            LatestReleaseUrl = result.ReleaseUrl;
            MatchedAssetName = result.MatchingAssetName ?? "";
            MatchedAssetUrl = result.MatchingAssetUrl ?? "";
            IsDownloadCompleted = false;
            DownloadedFilePath = "";
            UpdateDownloadProgress = 0.0;

            if (result.HasUpdate)
            {
                var assetNote = !string.IsNullOrEmpty(MatchedAssetName) ? $" [{MatchedAssetName}]" : "";
                UpdateStatusText = string.Format(LocalizationService.T("🎉 發現新版本 {0}{1}！點擊下方按鈕自動下載更新", "🎉 发现新版本 {0}{1}！点击下方按钮自动下载更新", "🎉 New version {0}{1} available! Click below to download update", "🎉 新バージョン {0}{1} が利用可能です！"), result.LatestVersion, assetNote);
            }
            else
            {
                UpdateStatusText = string.Format(LocalizationService.T("✅ 目前已是最新版本 ({0})", "✅ 当前已是最新版本 ({0})", "✅ You are running the latest version ({0})", "✅ 最新バージョンを実行中です ({0})"), UpdateService.CurrentVersion);
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"❌ {ex.Message}";
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    public async Task DownloadUpdateAsync()
    {
        if (string.IsNullOrEmpty(MatchedAssetUrl) && string.IsNullOrEmpty(LatestReleaseUrl)) return;

        string targetFileName = !string.IsNullOrEmpty(MatchedAssetName)
            ? MatchedAssetName
            : AppEnvironmentHelper.PreferredAssetName;

        string downloadFolder = !string.IsNullOrWhiteSpace(GlobalDownloadPath) && Directory.Exists(GlobalDownloadPath)
            ? GlobalDownloadPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        string destinationPath = Path.Combine(downloadFolder, targetFileName);

        try
        {
            IsDownloadingUpdate = true;
            IsDownloadCompleted = false;
            UpdateDownloadProgress = 0.0;
            DownloadSpeedText = "正在連接下載節點...";

            var progress = new Progress<UpdateDownloadProgress>(p =>
            {
                UpdateDownloadProgress = p.Percent;
                var downloadedMb = p.BytesReceived / 1048576.0;
                var totalMb = p.TotalBytes > 0 ? (p.TotalBytes / 1048576.0) : 0;
                DownloadSpeedText = totalMb > 0
                    ? $"{downloadedMb:F1} MB / {totalMb:F1} MB ({p.Percent:F0}%) • {p.SpeedText}"
                    : $"{downloadedMb:F1} MB • {p.SpeedText}";
            });

            string url = !string.IsNullOrEmpty(MatchedAssetUrl) ? MatchedAssetUrl : LatestReleaseUrl;
            await UpdateService.Instance.DownloadReleaseAssetAsync(url, destinationPath, progress);

            DownloadedFilePath = destinationPath;
            IsDownloadCompleted = true;
            DownloadSpeedText = $"✅ 下載完成！已儲存至: {destinationPath}";
            UpdateStatusText = $"🎉 更新已成功下載至本機 ({targetFileName})";
        }
        catch (Exception ex)
        {
            DownloadSpeedText = $"❌ 下載失敗: {ex.Message}";
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    [RelayCommand]
    public void ApplyDownloadedUpdate()
    {
        if (!string.IsNullOrEmpty(DownloadedFilePath) && File.Exists(DownloadedFilePath))
        {
            UpdateService.ApplyUpdateAndRestart(DownloadedFilePath);
        }
    }

    [RelayCommand]
    public void OpenDownloadedFolder()
    {
        if (!string.IsNullOrEmpty(DownloadedFilePath) && File.Exists(DownloadedFilePath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{DownloadedFilePath}\"");
            }
            catch { }
        }
    }

    [RelayCommand]
    public void OpenUpdateDownload()
    {
        UpdateService.OpenReleaseUrl(LatestReleaseUrl);
    }

    partial void OnSelectedThemeChanged(string value)
    {
        SaveSettings();
    }

    partial void OnSelectedScaleChanged(int value)
    {
        SaveSettings();
    }

    private bool _isUpdatingLanguage;

    partial void OnSelectedLanguageChanged(string value)
    {
        if (_isUpdatingLanguage) return;
        string targetPref = "Auto";
        if (value.Contains("zh-CN")) targetPref = "zh-CN";
        else if (value.Contains("en-US")) targetPref = "en-US";
        else if (value.Contains("ja-JP")) targetPref = "ja-JP";
        else if (value.Contains("zh-TW")) targetPref = "zh-TW";
        else targetPref = "Auto";

        LocalizationService.Instance.ApplyLanguagePreference(targetPref);
        SettingsService.Instance.Current.Language = targetPref;
        RefreshLocalizedOptions();
        SaveSettings();
    }

    partial void OnSelectedFontOptionChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        IsCustomFontInputVisible = value.StartsWith("自訂") || value.StartsWith("自定义") || value.StartsWith("Custom") || value.StartsWith("カスタム");
        if (s_fontOptionToFamily.TryGetValue(value, out var fontFam))
        {
            CustomFontFamily = fontFam;
        }

        SaveSettings();
    }

    partial void OnCustomFontFamilyChanged(string value)
    {
        SaveSettings();
    }

    partial void OnCustomFontScaleChanged(double value)
    {
        SaveSettings();
    }

    partial void OnSelectedWorkspacePresetChanged(string value)
    {
        SaveSettings();
    }

    [RelayCommand]
    public async Task ExportDiagnosticsAsync()
    {
        try
        {
            StatusMessage = "正在蒐集全系統診斷與除錯資料...";
            var path = await DiagnosticExportService.Instance.ExportReportToFileAsync(autoOpenFile: true);
            DiagnosticExportMessage = $"✅ 診斷報告已生成並開啟：{System.IO.Path.GetFileName(path)}";
            StatusMessage = "診斷報告匯出完成！";
        }
        catch (Exception ex)
        {
            DiagnosticExportMessage = $"❌ 匯出失敗：{ex.Message}";
            StatusMessage = $"匯出失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RefreshTcpSettingsAsync()
    {
        try
        {
            var tcp = await _tcpOptimizer.GetTcpSettingsAsync();
            TcpSettingsSummary = tcp.Summary;
        }
        catch (Exception ex)
        {
            TcpSettingsSummary = $"查詢失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ApplyTcpThroughputPresetAsync()
    {
        try
        {
            StatusMessage = "正在套用極速網路設定...";
            var isExperimental = TcpAutoTuningLevel.Contains("Experimental", StringComparison.OrdinalIgnoreCase);
            var (success, msg) = await _tcpOptimizer.ApplyExtremeThroughputPresetAsync(isExperimental);
            StatusMessage = msg;
            await RefreshTcpSettingsAsync();
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "套用 TCP 極速最佳化");
            StatusMessage = $"套用失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RestoreTcpDefaultsAsync()
    {
        try
        {
            StatusMessage = "正在還原官方網路設定...";
            var (success, msg) = await _tcpOptimizer.RestoreWindowsDefaultsAsync();
            StatusMessage = msg;
            await RefreshTcpSettingsAsync();
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "還原 TCP 預設值");
            StatusMessage = $"還原失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task FlushDnsAsync()
    {
        try
        {
            var res = await _tcpOptimizer.FlushDnsAsync();
            StatusMessage = res;
        }
        catch (Exception ex)
        {
            StatusMessage = $"清除 DNS 失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task BrowseBackgroundImageAsync()
    {
        var file = await FilePickerHelper.PickSingleFileAsync([".jpg", ".jpeg", ".png", ".webp", ".bmp"]);
        if (!string.IsNullOrWhiteSpace(file))
        {
            BackgroundImagePath = file;
            SaveSettings();
        }
    }

    [RelayCommand]
    public void ClearBackgroundImage()
    {
        BackgroundImagePath = "";
        SaveSettings();
    }

    [RelayCommand]
    public async Task BrowseCompanionModelAsync()
    {
        var file = await FilePickerHelper.PickSingleFileAsync([".vrm", ".model3.json", ".zip", ".png", ".webp", ".jpg"]);
        if (!string.IsNullOrWhiteSpace(file))
        {
            CompanionModelPath = file;
            SaveSettings();
        }
    }

    [RelayCommand]
    public async Task BrowseGlobalDownloadFolderAsync()
    {
        var folder = await FilePickerHelper.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            GlobalDownloadPath = folder;
            SaveSettings();
        }
    }

    [RelayCommand]
    public void ResetToDefaults()
    {
        var s = new AppSettings();
        SelectedTheme = s.Theme;
        SelectedScale = s.ScalePercent;
        SelectedLanguage = "🌐 跟隨 Windows 系統語言 (Auto)";
        EnableSafetyConfirmations = s.EnableSafetyConfirmations;
        DefaultPartitionStyle = s.DefaultPartitionStyle;
        DefaultFileSystem = s.DefaultFileSystem;
        DefaultBackupFolder = s.DefaultBackupFolder;
        MaxLogBufferLines = s.MaxLogBufferLines;

        BackgroundImagePath = s.BackgroundImagePath;
        BackgroundOpacity = s.BackgroundOpacity;
        BackgroundBlur = s.BackgroundBlur;

        EnableCompanionAvatar = s.EnableCompanionAvatar;
        CompanionAvatarType = s.CompanionAvatarType;
        CompanionModelPath = s.CompanionModelPath;

        EnableGlassTransparency = s.EnableGlassTransparency;
        CardOpacity = s.CardOpacity;

        HttpRangeParallelEnabled = s.HttpRangeParallelEnabled;
        ParallelThreadCount = s.ParallelThreadCount;
        ChunkBufferSizeKB = s.ChunkBufferSizeKB;
        KeepResumeCache = s.KeepResumeCache;
        GlobalDownloadPath = WindowsDownloadService.DefaultDownloadDirectory;
        ProxyMode = s.ProxyMode;
        CustomProxyUrl = s.CustomProxyUrl;

        EnablePromptWindows = true;
        CustomFontFamily = "Segoe UI Variable";
        CustomFontScale = 100;

        EnableDebugLogging = s.EnableDebugLogging;
        DebugLogService.Instance.IsEnabled = s.EnableDebugLogging;
        UpdateDebugLogStats();

        SaveSettings();
        StatusMessage = "已重設為預設值。(Reset to defaults)";
    }

    partial void OnEnableDebugLoggingChanged(bool value)
    {
        DebugLogService.Instance.IsEnabled = value;
        SettingsService.Instance.Current.EnableDebugLogging = value;
        SettingsService.Instance.Save();
        if (value)
        {
            DebugLogService.Instance.Info("使用者已啟用即時偵錯日誌紀錄 (Debug logging enabled by user)", "Settings");
        }
        UpdateDebugLogStats();
    }

    [RelayCommand]
    public void UpdateDebugLogStats()
    {
        try
        {
            var stats = DebugLogService.Instance.GetLogStats();
            DebugLogStatsText = stats.FormattedText;
        }
        catch (Exception ex)
        {
            DebugLogStatsText = $"無法讀取統計: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenDebugLogFolder()
    {
        DebugLogService.Instance.OpenLogsFolder();
    }

    [RelayCommand]
    public void ViewCurrentDebugLog()
    {
        DebugLogService.Instance.ViewCurrentLog();
    }

    [RelayCommand]
    public void ClearDebugLogs()
    {
        try
        {
            DebugLogService.Instance.ClearLogs();
            UpdateDebugLogStats();
            StatusMessage = LocalizationService.T("已清空偵錯日誌檔案！", "已清空调试日志文件！", "Debug logs cleared successfully!", "デバッグログファイルをクリアしました！");
        }
        catch (Exception ex)
        {
            StatusMessage = $"清理日誌失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CopyDebugLogPath()
    {
        try
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(DebugLogService.Instance.LogFilePath);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            StatusMessage = LocalizationService.T("日誌檔案路徑已複製到剪貼簿！", "日志文件路径已复制到剪贴板！", "Log file path copied to clipboard!", "ログファイルのパスをクリップボードにコピーしました！");
        }
        catch (Exception ex)
        {
            StatusMessage = $"複製失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task CleanTempCacheAsync()
    {
        try
        {
            var cleaner = new CacheCleanService();
            var (summary, _) = await cleaner.PerformFullCleanupAsync();
            StatusMessage = summary;
        }
        catch (Exception ex)
        {
            StatusMessage = $"清理失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    public void AutoDetectScale()
    {
        var recommended = DisplayHelper.GetRecommendedScalePercent(WindowHelper.CurrentHwnd);
        SelectedScale = recommended;
        SaveSettings();
        StatusMessage = $"已自動套用螢幕最佳縮放比例: {recommended}%";
    }

    [RelayCommand]
    public void SendTestNotification()
    {
        try
        {
            string title = LocalizationService.T("DiskMaster Pro 旗艦版", "DiskMaster Pro 旗舰版", "DiskMaster Pro Flagship", "DiskMaster Pro Flagship");
            string message = LocalizationService.T("🔔 已成功連線至 Windows 通知中心！點擊此處可開啟偏好設定。",
                                                   "🔔 已成功连线至 Windows 通知中心！点击此处可开启偏好设置。",
                                                   "🔔 Successfully connected to Windows Notification Center! Click to open Settings.",
                                                   "🔔 Windows 通知センターに接続しました！クリックして設定を開きます。");

            WindowsNotificationService.Instance.ShowNotification(title, message, "Settings");
            StatusMessage = LocalizationService.T("已發送測試通知至 Windows 通知清單！",
                                                  "已发送测试通知至 Windows 通知列表！",
                                                  "Test notification sent to Windows Notification Center!",
                                                  "Windows 通知リストへテスト通知を送信しました！");
        }
        catch (Exception ex)
        {
            StatusMessage = $"發送測試通知失敗: {ex.Message}";
        }
    }
}
