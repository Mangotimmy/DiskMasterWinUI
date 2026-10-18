using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

public partial class SystemOptimizerViewModel : ObservableObject
{
    private readonly SystemOptimizerService _optimizer = new();
    private readonly PowerPlanService _powerService = new();
    private readonly MemoryManagerService _memoryManager = new();
    private readonly PowerCfgService _powerCfg = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isUpdatingProgrammatically;
    [ObservableProperty] private string _statusMessage = "就緒";
    [ObservableProperty] private string _consoleLog = "";

    // ── Gaming Mode Master State ──
    [ObservableProperty] private bool _isGamingModeEnabled;

    // ── MMAgent (Memory Management Agent) ──
    [ObservableProperty] private bool _isMemoryCompressionEnabled = true;
    [ObservableProperty] private bool _isPageCombiningEnabled = true;
    [ObservableProperty] private bool _isAppPreLaunchEnabled = true;
    [ObservableProperty] private bool _isAppLaunchProfilingEnabled = true;
    [ObservableProperty] private bool _isOperationRecordingEnabled = true;

    // ── PowerCfg & Sleep Management ──
    [ObservableProperty] private bool _isHibernationEnabled;
    [ObservableProperty] private string _hiberfilSizeDisplay = "未知";
    [ObservableProperty] private bool _isFastStartupReduced;
    [ObservableProperty] private bool _isProcessorBoostUnhidden;
    public ObservableCollection<PowerWakeDeviceItem> WakeDevices { get; } = new();
    public ObservableCollection<SleepBlockerItem> SleepBlockers { get; } = new();
    [ObservableProperty] private bool _hasSleepBlockers;
    [ObservableProperty] private bool _isLoadingWakeDevices;
    [ObservableProperty] private bool _isLoadingSleepBlockers;
    [ObservableProperty] private bool _hasScannedSleepBlockers;
    [ObservableProperty] private bool _isRebuildingIconCache;
    [ObservableProperty] private string _iconCacheStatus = "";

    // ── CPU Scheduling (FPSHeaven & Presets) ──
    [ObservableProperty] private int _selectedCpuPresetIndex;
    [ObservableProperty] private bool _isPowerThrottlingDisabled;

    // ── MMCSS & Network Latency ──
    [ObservableProperty] private bool _isSystemResponsivenessZero;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SystemResponsivenessDisplay))]
    private int _systemResponsivenessValue = 20;
    [ObservableProperty] private bool _isNetworkThrottlingDisabled;

    public string SystemResponsivenessDisplay => $"{SystemResponsivenessValue} (0x{SystemResponsivenessValue:X2})";

    // ── Gaming Tweaks: GameDVR, HAGS, Latency ──
    [ObservableProperty] private bool _isGameDvrDisabled;
    [ObservableProperty] private bool _isHagsEnabled;
    [ObservableProperty] private bool _isLowInputLatencyEnabled;

    // ── Khronos Vulkan Layer Diagnostics & Pure Gaming Mode ──
    private readonly VulkanOptimizerService _vulkanService = VulkanOptimizerService.Instance;
    public ObservableCollection<VulkanLayerItem> VulkanLayers { get; } = new();
    [ObservableProperty] private bool _isPureVulkanMode;
    [ObservableProperty] private int _totalVulkanLayersCount;
    [ObservableProperty] private int _orphanedVulkanLayersCount;
    [ObservableProperty] private bool _hasOrphanedVulkanLayers;
    [ObservableProperty] private bool _isLoadingVulkanLayers;
    [ObservableProperty] private string _vulkanStatusSummary = "就緒";

    // ── Windows Explorer Advanced & Latency Optimization (/boost) ──
    private readonly ExplorerAdvancedOptimizerService _explorerAdvService = ExplorerAdvancedOptimizerService.Instance;
    [ObservableProperty] private ExplorerAdvancedState _explorerAdvState = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExplorerOptimizationScoreText))]
    private int _explorerOptimizationScore;
    public string ExplorerOptimizationScoreText => $"{ExplorerOptimizationScore}% 優化達成率";
    [ObservableProperty] private string _explorerBoostStatus = "就緒";
    [ObservableProperty] private bool _isApplyingExplorerBoost;

    // ── Power Plans ──
    public ObservableCollection<PowerPlanItem> PowerPlans { get; } = new();
    [ObservableProperty] private PowerPlanItem? _selectedPowerPlan;
    [ObservableProperty] private bool _isCoreUnparked;
    [ObservableProperty] private bool _isMinProcessorState100;

    // ── Backups & Restore Points ──
    public ObservableCollection<RegistryBackupItem> Backups { get; } = new();
    [ObservableProperty] private RegistryBackupItem? _selectedBackup;
    public ObservableCollection<SystemRestorePointItem> RestorePoints { get; } = new();
    [ObservableProperty] private string _manualBackupDescription = "自訂優化備份";

    // ── OEM Driver Management (PnPUtil) ──
    private readonly DriverService _driverService = new();
    public ObservableCollection<OemDriverItem> OemDrivers { get; } = new();
    [ObservableProperty] private bool _isLoadingDrivers;
    [ObservableProperty] private string _driverFilterText = "";

    // ── OneDrive Deep Management & Repair ──
    private readonly OneDriveService _oneDriveService = new();
    [ObservableProperty] private OneDriveStatusInfo _oneDriveStatus = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OneDriveInstalledStatusText))]
    private bool _isOneDriveInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OneDriveRunningStatusText))]
    private bool _isOneDriveRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OneDriveRedirectStatusText))]
    private bool _isOneDriveFoldersRedirected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OneDriveCloudFilesStatusText))]
    private bool _hasOneDriveCloudOnlyFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OneDriveCloudFilesStatusText))]
    private int _oneDriveCloudOnlyCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OneDriveGhostIconStatusText))]
    private bool _isOneDrivePinned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OneDrivePolicyStatusText))]
    private bool _isOneDrivePolicyBlocked;

    [ObservableProperty] private bool _isOneDriveOperating;

    public string OneDriveInstalledStatusText => IsOneDriveInstalled ? "已安裝 (Installed)" : "未安裝 / 已徹底清除 (Clean)";
    public string OneDriveRunningStatusText => IsOneDriveRunning ? "🟢 行程運作中 (Running)" : "⚪ 未執行 (Stopped)";
    public string OneDriveRedirectStatusText => IsOneDriveFoldersRedirected ? "⚠️ 個人資料夾遭重定向" : "✅ 原生個人資料夾路徑正常";
    public string OneDriveCloudFilesStatusText => HasOneDriveCloudOnlyFiles ? $"⚠️ 偵測到 {OneDriveCloudOnlyCount} 個雲端脫機檔案" : "✅ 無脫機檔案遺失風險";
    public string OneDriveGhostIconStatusText => IsOneDrivePinned ? "⚠️ 檔案總管側邊欄存在圖示" : "✅ 側邊欄無幽靈圖示殘留";
    public string OneDrivePolicyStatusText => IsOneDrivePolicyBlocked ? "🛡️ 已透過原則封鎖自動安裝" : "⚪ 群組原則未封鎖";

    // ── Advanced Latency, Memory & Group Policy Tweaks ──
    [ObservableProperty] private bool _isNagleDisabled;
    [ObservableProperty] private bool _isDisablePagingExecutive;
    [ObservableProperty] private bool _isGroupPolicyTelemetryDisabled;

    // ── Item 5, 7, 9: L2TP/IPsec VPN NAT-T Fix, DPS Memory Bloat & DiagTrack Service ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(L2tpVpnNatRuleDisplay))]
    private bool _isL2tpVpnNatFixEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(L2tpVpnNatRuleDisplay))]
    private int _l2tpVpnNatRuleValue;

    public string L2tpVpnNatRuleDisplay => L2tpVpnNatRuleValue switch
    {
        2 => "✅ 2 (REG_DWORD: 支援伺服器與客戶端雙向 NAT 穿透 - 已修復)",
        1 => "⚠️ 1 (僅支援伺服器端 NAT)",
        _ => "⚪ 0 (未設定 - L2TP/IPsec 經由路由器 NAT 時易發生 789/809 錯誤)"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DpsServiceStatusText))]
    private bool _isDpsServiceDisabled;

    public string DpsServiceStatusText => IsDpsServiceDisabled
        ? "🛡️ 已停用 (Start = 4，已杜絕背景診斷服務記憶體膨脹)"
        : "⚪ 預設啟用中 (Start = 2，背景持續監控可能造成 svchost 記憶體飆高)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiagTrackStatusText))]
    private bool _isDiagTrackDisabled;

    public string DiagTrackStatusText => IsDiagTrackDisabled
        ? "🛡️ 已徹底關閉 (sc config DiagTrack start=disabled，Windows Update 正常運作)"
        : "⚠️ 運作中 (Connected User Experiences and Telemetry 持續背景收集遙測)";

    // ── Item 11, 12, 13, 14: Explorer Context Menu, Folder Optimization, Custom Icons & Watchdog ──
    private readonly FolderAndIconCustomizerService _folderCustomizer = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextMenuStyleBadge))]
    private bool _isClassicContextMenu;

    public string ContextMenuStyleBadge => IsClassicContextMenu
        ? "⚡ Windows 10 經典完整右鍵選單 (免點「顯示更多選項」)"
        : "🪟 Windows 11 現代簡潔右鍵選單 (預設摺疊)";

    [ObservableProperty] private bool _isRightClickSpeedOptimized;
    [ObservableProperty] private bool _isGlobalFastFolderDiscoveryEnabled;

    // Folder Customize (Optimize this folder for + subfolders + folder picture)
    [ObservableProperty] private string _targetCustomizeFolderPath = "";
    [ObservableProperty] private int _selectedFolderTemplateIndex = 0; // 0=Generic(General items), 1=Documents, 2=Pictures, 3=Music, 4=Videos
    [ObservableProperty] private bool _applyTemplateToSubfolders = true;
    [ObservableProperty] private string _targetFolderPicturePath = "";

    // Custom Icon for Folders & File Extensions
    [ObservableProperty] private string _targetCustomIconResource = @"C:\Windows\System32\shell32.dll,43";
    [ObservableProperty] private int _selectedPopularFolderIconIndex = 0;
    [ObservableProperty] private string _targetFileExtension = ".txt";

    // Explorer Auto-Recovery Watchdog (Item 14)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExplorerWatchdogStatusBadge))]
    private bool _isExplorerWatchdogEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExplorerWatchdogStatusBadge))]
    private bool _isExplorerCurrentlyAlive = true;

    [ObservableProperty] private string _explorerWatchdogRecoverySummary = "尚未觸發自動救援 (0 次)";

    public string ExplorerWatchdogStatusBadge => !IsExplorerWatchdogEnabled
        ? "⚪ 自動守護已暫停"
        : IsExplorerCurrentlyAlive
            ? "🟢 守護運作中 (explorer.exe 正常執行)"
            : "🟡 偵測到異常關閉，正在自動重啟檔案總管...";

    // ── CPU PPM Controls (EPP, Core Parking, Boost) ──
    [ObservableProperty] private int _cpuEppSliderValue = 0;
    [ObservableProperty] private int _cpuCoreParkingMinPercent = 100;
    [ObservableProperty] private int _selectedCpuBoostModeIndex = 2;

    // ── CPU Priority Program Selection & IFEO Rules ──
    public ObservableCollection<RunningProcessPriorityItem> RunningProcesses { get; } = new();
    public ObservableCollection<PersistedPriorityRuleItem> PersistedPriorityRules { get; } = new();
    public ObservableCollection<PersistedPriorityRuleItem> PersistedRules => PersistedPriorityRules;

    [ObservableProperty] private RunningProcessPriorityItem? _selectedProcess;
    [ObservableProperty] private string _customProcessInput = "";
    [ObservableProperty] private int _selectedPriorityIndex = 1; // 0=RealTime, 1=High, 2=AboveNormal, 3=Normal
    [ObservableProperty] private bool _isLoadingProcesses;

    partial void OnSelectedProcessChanged(RunningProcessPriorityItem? value)
    {
        if (value != null && !string.IsNullOrWhiteSpace(value.ProcessName))
        {
            CustomProcessInput = value.ProcessName;
            SelectedPriorityIndex = value.PriorityClass switch
            {
                "RealTime" => 0,
                "High" => 1,
                "AboveNormal" => 2,
                "Normal" => 3,
                _ => 1
            };
        }
    }

    public SystemOptimizerViewModel()
    {
        AppendLog("⚡ 系統最佳化與遊戲模式引擎已啟動 (FPSHeaven & PowerPlan Suite)");
        ExplorerWatchdogService.Instance.StatusChanged += () =>
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsExplorerWatchdogEnabled = ExplorerWatchdogService.Instance.IsEnabled;
                IsExplorerCurrentlyAlive = ExplorerWatchdogService.Instance.IsExplorerAlive;
                ExplorerWatchdogRecoverySummary = ExplorerWatchdogService.Instance.LastRecoveryTimeText;
            });
        };
    }

    private void AppendLog(string message)
    {
        DispatcherHelper.RunOnUIThread(() =>
        {
            if (ConsoleLog.Length > 200000)
            {
                ConsoleLog = ConsoleLog.Substring(ConsoleLog.Length - 100000);
            }
            ConsoleLog += message + "\n";
        });
    }

    [ObservableProperty]
    private bool _isLoadingPowerPlans;

    [ObservableProperty]
    private bool _isLoadingRestorePoints;

    [RelayCommand]
    public async Task RefreshFastCoreSettingsAsync()
    {
        await Task.CompletedTask;
        try
        {
            IsUpdatingProgrammatically = true;
            IsLoading = false;
            var lang = LocalizationService.Instance.CurrentLanguage;
            StatusMessage = lang switch
            {
                "zh-CN" => "核心配置就绪",
                "en-US" => "Core settings ready",
                "ja-JP" => "基本設定準備完了",
                _ => "核心設定就緒"
            };

            // 1. Fast in-memory / registry reads (<5ms)
            var pSep = _optimizer.GetWin32PrioritySeparation();
            SelectedCpuPresetIndex = pSep switch
            {
                38 => 0, // 0x26
                42 => 1, // 0x2A
                40 => 2, // 0x28
                22 => 3, // 0x16
                24 => 4, // 0x18
                _ => 5   // 0x2 or default
            };
            IsPowerThrottlingDisabled = _optimizer.GetPowerThrottlingDisabled();
            SystemResponsivenessValue = _optimizer.GetSystemResponsiveness();
            IsSystemResponsivenessZero = SystemResponsivenessValue <= 10;
            IsNetworkThrottlingDisabled = _optimizer.GetNetworkThrottlingDisabled();
            IsGameDvrDisabled = _optimizer.GetGameDvrDisabled();
            IsHagsEnabled = _optimizer.GetHagsStatus();
            IsNagleDisabled = _optimizer.GetNagleDisabled();
            IsDisablePagingExecutive = _optimizer.GetDisablePagingExecutive();
            IsGroupPolicyTelemetryDisabled = _optimizer.GetTelemetryDisabled();

            // Items 5, 7, 9, 11, 12, 14
            L2tpVpnNatRuleValue = _optimizer.GetL2tpVpnNatRuleValue();
            IsL2tpVpnNatFixEnabled = L2tpVpnNatRuleValue == 2;
            IsDpsServiceDisabled = _optimizer.GetDpsServiceDisabled();
            IsDiagTrackDisabled = _optimizer.GetDiagTrackDisabled();
            IsClassicContextMenu = _folderCustomizer.IsClassicContextMenuEnabled();
            IsRightClickSpeedOptimized = _folderCustomizer.IsRightClickSpeedOptimized();
            IsGlobalFastFolderDiscoveryEnabled = _folderCustomizer.IsGlobalFastFolderDiscoveryEnabled();
            IsExplorerWatchdogEnabled = ExplorerWatchdogService.Instance.IsEnabled;
            IsExplorerCurrentlyAlive = ExplorerWatchdogService.CheckExplorerRunning();
            ExplorerWatchdogRecoverySummary = ExplorerWatchdogService.Instance.LastRecoveryTimeText;

            IsGamingModeEnabled = (SelectedCpuPresetIndex == 0 || SelectedCpuPresetIndex == 1)
                                  && IsSystemResponsivenessZero
                                  && IsNetworkThrottlingDisabled
                                  && IsGameDvrDisabled;

            // 2. Read Backups (fast filesystem directory scan)
            Backups.Clear();
            var backupList = _optimizer.ListBackups();
            foreach (var b in backupList) Backups.Add(b);

            // 3. Explorer Advanced & Vulkan Pure Mode (fast registry checks)
            RefreshExplorerAdvancedState();
            IsPureVulkanMode = _vulkanService.IsPureVulkanModeEnabled();
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally
        {
            IsUpdatingProgrammatically = false;
        }

        // Parallel background fetch for lightweight memory compression and Vulkan layers
        _ = Task.Run(async () =>
        {
            try
            {
                var mm = await _memoryManager.GetStatusAsync();
                DispatcherHelper.RunOnUIThread(() =>
                {
                    IsMemoryCompressionEnabled = mm.MemoryCompression;
                    IsPageCombiningEnabled = mm.PageCombining;
                    IsAppPreLaunchEnabled = mm.ApplicationPreLaunch;
                    IsAppLaunchProfilingEnabled = mm.ApplicationLaunchProfiling;
                    IsOperationRecordingEnabled = mm.OperationRecording;
                });
            }
            catch { }
        });

        _ = Task.Run(async () =>
        {
            try { await RefreshVulkanLayersAsync(); } catch { }
        });
    }

    [RelayCommand]
    public async Task RefreshPowerPlansAsync()
    {
        if (IsLoadingPowerPlans) return;
        try
        {
            IsLoadingPowerPlans = true;
            var plans = await _powerService.GetPowerPlansAsync();
            var hiberOn = _powerCfg.IsHibernationEnabled();
            var hiberSize = _powerCfg.GetHiberfilSizeDisplay();
            DispatcherHelper.RunOnUIThread(() =>
            {
                PowerPlans.Clear();
                foreach (var p in plans)
                {
                    PowerPlans.Add(p);
                    if (p.IsActive) SelectedPowerPlan = p;
                }
                IsHibernationEnabled = hiberOn;
                HiberfilSizeDisplay = hiberSize;
            });
        }
        catch { }
        finally
        {
            IsLoadingPowerPlans = false;
        }
    }

    [RelayCommand]
    public async Task RefreshRestorePointsAsync()
    {
        if (IsLoadingRestorePoints) return;
        try
        {
            IsLoadingRestorePoints = true;
            var rps = await _optimizer.ListSystemRestorePointsAsync();
            DispatcherHelper.RunOnUIThread(() =>
            {
                RestorePoints.Clear();
                foreach (var r in rps) RestorePoints.Add(r);
            });
        }
        catch { }
        finally
        {
            IsLoadingRestorePoints = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        await RefreshFastCoreSettingsAsync();

        var powerTask = Task.Run(async () =>
        {
            try { await RefreshPowerPlansAsync(); } catch { }
        });

        var wakeTask = Task.Run(async () =>
        {
            try { await RefreshWakeDevicesAsync(); } catch { }
        });

        var blockersTask = Task.Run(async () =>
        {
            try { await RefreshSleepBlockersAsync(); } catch { }
        });

        var restorePointsTask = Task.Run(async () =>
        {
            try { await RefreshRestorePointsAsync(); } catch { }
        });

        var oneDriveTask = Task.Run(async () =>
        {
            try { await RefreshOneDriveStatusAsync(); } catch { }
        });

        var procTask = Task.Run(async () =>
        {
            try { await RefreshRunningProcessesAsync(); } catch { }
        });

        var rulesTask = Task.Run(async () =>
        {
            try { await RefreshPersistedRulesAsync(); } catch { }
        });

        var vulkanTask = Task.Run(async () =>
        {
            try { await RefreshVulkanLayersAsync(); } catch { }
        });

        RefreshExplorerAdvancedState();

        await Task.WhenAll(powerTask, wakeTask, blockersTask, restorePointsTask, oneDriveTask, procTask, rulesTask, vulkanTask);

        DispatcherHelper.RunOnUIThread(() =>
        {
            var curLang = LocalizationService.Instance.CurrentLanguage;
            StatusMessage = curLang switch
            {
                "zh-CN" => "状态刷新完成。",
                "en-US" => "Status refresh completed.",
                "ja-JP" => "ステータスの更新が完了しました。",
                _ => "狀態重新整理完成。"
            };
        });
    }

    // ══════════════════════════════════════════════════════════
    //  1-Click Gaming Mode & Factory Defaults
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task ApplyGamingProfileAsync()
    {
        DebugLogService.Instance.LogAction("系統優化 (SystemOptimizer)", "套用電競遊戲極致模式 (FPSHeaven 0x26)");
        try
        {
            IsLoading = true;
            var lang = LocalizationService.Instance.CurrentLanguage;
            StatusMessage = lang switch
            {
                "zh-CN" => "正在应用电竞极致模式 (FPSHeaven 0x26)...",
                "en-US" => "Applying Ultra Gaming Mode (FPSHeaven 0x26)...",
                "ja-JP" => "ウルトラゲーミングモードを適用中 (FPSHeaven 0x26)...",
                _ => "正在套用電競遊戲極致模式 (FPSHeaven 0x26)..."
            };

            var (profileApplied, log) = await _optimizer.ApplyGamingProfileWithResultAsync(38); // 0x26 default
            AppendLog(log);

            // Also unlock and activate Ultimate Performance
            var unlockMsg = lang switch
            {
                "zh-CN" => "🔋 正在解锁并启用卓越性能电源计划...",
                "en-US" => "🔋 Unlocking and activating Ultimate Performance power plan...",
                "ja-JP" => "🔋 究極パフォーマンス電源プランを有効化中...",
                _ => "🔋 正在解鎖並啟用終極效能電源計畫 (Ultimate Performance)..."
            };
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {unlockMsg}");
            var (success, msg, _) = await _powerService.UnlockUltimatePerformanceAsync();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}");

            await RefreshAllAsync();
            if (!profileApplied)
            {
                ReportTuningNotApplied("電競遊戲極致模式");
                return;
            }
            StatusMessage = lang switch
            {
                "zh-CN" => "电竞模式已启用！建议重启系统以获得最佳效能。",
                "en-US" => "Gaming Mode activated! Reboot recommended for peak performance.",
                "ja-JP" => "ゲーミングモードが有効化されました。再起動を推奨します。",
                _ => "電競模式已全面啟用！建議重新開機以發揮極限效能。"
            };
        }
        catch (Exception ex)
        {
            var curLang = LocalizationService.Instance.CurrentLanguage;
            var prefix = curLang switch
            {
                "zh-CN" => "应用失败: ",
                "en-US" => "Failed to apply: ",
                "ja-JP" => "適用失敗: ",
                _ => "套用失敗: "
            };
            StatusMessage = $"{prefix}{ex.Message}";
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task RestoreDefaultsAsync()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;
        var dlgTitle = lang switch
        {
            "zh-CN" => "还原 Windows 官方原生设置",
            "en-US" => "Restore Windows Factory Defaults",
            "ja-JP" => "Windows 工場出荷時デフォルトに戻す",
            _ => "還原 Windows 官方原生設定 (Restore Defaults)"
        };
        var dlgBody = lang switch
        {
            "zh-CN" => "即将把所有注册表微调、CPU调度、多媒体保留率与游戏模式还原为 Windows 官方出厂默认值。",
            "en-US" => "This will restore all registry tweaks, CPU scheduling, MMCSS responsiveness, and gaming mode settings to official Windows defaults.",
            "ja-JP" => "すべてのレジストリ最適化、CPUスケジューリング、MMCSS応答性、ゲーミングモード設定を公式デフォルトに戻します。",
            _ => "即將把所有登錄檔調節、CPU排程、多媒體保留率與遊戲模式還原為 Windows 10/11 官方出廠預設值。"
        };
        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            dlgTitle,
            dlgBody,
            "Restore Defaults");
        if (!confirmed) return;

        DebugLogService.Instance.LogAction("系統優化 (SystemOptimizer)", "還原 Windows 官方原生設定 (Restore Defaults)");
        try
        {
            IsLoading = true;
            StatusMessage = lang switch
            {
                "zh-CN" => "正在还原 Windows 官方原生设置...",
                "en-US" => "Restoring Windows factory defaults...",
                "ja-JP" => "Windows 工場出荷時デフォルトを復元中...",
                _ => "正在還原 Windows 官方原生設定..."
            };

            var (defaultsRestored, log) = await _optimizer.RestoreWindowsDefaultsWithResultAsync();
            AppendLog(log);

            // Switch to balanced power plan if available
            var balanced = PowerPlans.FirstOrDefault(p => p.Name.Contains("平衡") || p.Name.Contains("Balanced"));
            if (balanced != null)
            {
                await _powerService.SetActivePlanAsync(balanced.Guid);
                var planMsg = lang switch
                {
                    "zh-CN" => "🔋 已切换回平衡电源计划。",
                    "en-US" => "🔋 Switched back to Balanced power plan.",
                    "ja-JP" => "🔋 バランス電源プランに切り替えました。",
                    _ => "🔋 已切換回平衡電源計畫。"
                };
                AppendLog($"[{DateTime.Now:HH:mm:ss}] {planMsg}");
            }

            await RefreshAllAsync();
            if (!defaultsRestored)
            {
                ReportTuningNotApplied("還原 Windows 官方出廠預設值");
                return;
            }
            StatusMessage = lang switch
            {
                "zh-CN" => "已还原为 Windows 官方出厂原生设置。",
                "en-US" => "Restored to official Windows defaults.",
                "ja-JP" => "公式デフォルト設定に復元しました。",
                _ => "已還原為 Windows 官方出廠原生設定。"
            };
        }
        catch (Exception ex)
        {
            var curLang = LocalizationService.Instance.CurrentLanguage;
            var prefix = curLang switch
            {
                "zh-CN" => "还原失败: ",
                "en-US" => "Restore failed: ",
                "ja-JP" => "復元失敗: ",
                _ => "還原失敗: "
            };
            StatusMessage = $"{prefix}{ex.Message}";
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    // ══════════════════════════════════════════════════════════
    //  Registry write outcome handling (audit B1)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Restores a bound switch to its previous value without re-invoking the toggle command: every
    /// Toggled handler on the page ignores changes while IsUpdatingProgrammatically is set.
    /// </summary>
    private void RevertSwitch(Action revert)
    {
        bool wasUpdating = IsUpdatingProgrammatically;
        IsUpdatingProgrammatically = true;
        try
        {
            revert();
        }
        finally
        {
            IsUpdatingProgrammatically = wasUpdating;
        }
    }

    /// <summary>
    /// Reports a refused registry write. Callers revert the bound switch first and then return
    /// without appending a success line, so the UI never claims a tuning that was not applied.
    /// </summary>
    private void ReportTuningNotApplied(string itemName)
    {
        var lang = LocalizationService.Instance.CurrentLanguage;
        StatusMessage = lang switch
        {
            "zh-CN" => $"❌ {itemName}：此項目需要系統管理員權限，設定未套用。",
            "en-US" => $"❌ {itemName}: administrator rights are required, the setting was NOT applied.",
            "ja-JP" => $"❌ {itemName}：管理者権限が必要です。設定は適用されませんでした。",
            _ => $"❌ {itemName}：此項目需要系統管理員權限，設定未套用。"
        };
        AppendLog($"[{DateTime.Now:HH:mm:ss}] ❌ {itemName}: 登錄檔寫入失敗，需要系統管理員權限，設定未套用。");
        AudioFeedbackService.PlayError();
    }

    // ══════════════════════════════════════════════════════════
    //  CPU Scheduling Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task ApplyCpuPresetAsync()
    {
        int val = SelectedCpuPresetIndex switch
        {
            0 => 38, // 0x26
            1 => 42, // 0x2A
            2 => 40, // 0x28
            3 => 22, // 0x16
            4 => 24, // 0x18
            _ => 2   // 0x2
        };

        await _optimizer.CreatePreTweakBackupAsync("Before_CpuPreset");
        if (!_optimizer.SetWin32PrioritySeparation(val))
        {
            ReportTuningNotApplied($"Win32PrioritySeparation = 0x{val:X}");
            return;
        }
        AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ CPU 排程量子已套用: 0x{val:X} ({val})");
        StatusMessage = $"已設定 Win32PrioritySeparation = 0x{val:X}";
    }

    // ══════════════════════════════════════════════════════════
    //  CPU Priority Process Selection & IFEO Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task RefreshRunningProcessesAsync()
    {
        try
        {
            DispatcherHelper.RunOnUIThread(() => IsLoadingProcesses = true);
            var list = await Task.Run(() => _optimizer.GetRunningProcessesWithPriority());
            DispatcherHelper.RunOnUIThread(() =>
            {
                RunningProcesses.Clear();
                foreach (var p in list) RunningProcesses.Add(p);
                IsLoadingProcesses = false;
            });
        }
        catch
        {
            DispatcherHelper.RunOnUIThread(() => IsLoadingProcesses = false);
        }
    }

    [RelayCommand]
    public async Task RefreshPersistedRulesAsync()
    {
        try
        {
            var rules = await Task.Run(() => _optimizer.ListPersistedIfeoRules());
            DispatcherHelper.RunOnUIThread(() =>
            {
                PersistedPriorityRules.Clear();
                foreach (var r in rules) PersistedPriorityRules.Add(r);
            });
        }
        catch { }
    }

    [RelayCommand]
    public async Task ApplyProcessPriorityAsync()
    {
        var targetName = !string.IsNullOrWhiteSpace(CustomProcessInput)
            ? CustomProcessInput.Trim()
            : SelectedProcess?.ProcessName?.Trim();

        if (string.IsNullOrWhiteSpace(targetName))
        {
            StatusMessage = "請選擇或輸入目標處理程序名稱 (例如 cs2.exe)";
            AudioFeedbackService.PlayError();
            return;
        }

        var (prioClass, prioStr) = SelectedPriorityIndex switch
        {
            0 => (ProcessPriorityClass.RealTime, "RealTime"),
            1 => (ProcessPriorityClass.High, "High"),
            2 => (ProcessPriorityClass.AboveNormal, "AboveNormal"),
            3 => (ProcessPriorityClass.Normal, "Normal"),
            _ => (ProcessPriorityClass.High, "High")
        };

        var (success, msg, affected) = await Task.Run(() => _optimizer.SetImmediateProcessPriority(targetName, prioClass));
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🎯 {msg}");
        StatusMessage = msg;

        if (success)
        {
            AudioFeedbackService.PlaySuccess();
            await RefreshRunningProcessesAsync();
        }
        else
        {
            AudioFeedbackService.PlayError();
        }
    }

    [RelayCommand]
    public async Task SavePersistedRuleAsync()
    {
        var targetName = !string.IsNullOrWhiteSpace(CustomProcessInput)
            ? CustomProcessInput.Trim()
            : SelectedProcess?.ProcessName?.Trim();

        if (string.IsNullOrWhiteSpace(targetName))
        {
            StatusMessage = "請選擇或輸入目標處理程序名稱 (例如 cs2.exe)";
            AudioFeedbackService.PlayError();
            return;
        }

        var cleanExeName = targetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? targetName : targetName + ".exe";

        var (prioClass, prioStr) = SelectedPriorityIndex switch
        {
            0 => (ProcessPriorityClass.RealTime, "RealTime"),
            1 => (ProcessPriorityClass.High, "High"),
            2 => (ProcessPriorityClass.AboveNormal, "AboveNormal"),
            3 => (ProcessPriorityClass.Normal, "Normal"),
            _ => (ProcessPriorityClass.High, "High")
        };

        await _optimizer.CreatePreTweakBackupAsync($"Before_IFEO_{Path.GetFileNameWithoutExtension(cleanExeName)}");

        // 1. Write to IFEO registry
        bool ifeoWritten = await Task.Run(() => _optimizer.SetIfeoCpuPriority(cleanExeName, prioStr));

        // 2. Persist in user settings
        SettingsService.Instance.Current.ProcessCpuPriorities[cleanExeName] = prioStr;
        SettingsService.Instance.Save();

        // 3. Apply immediately to any running instance
        await Task.Run(() => _optimizer.SetImmediateProcessPriority(cleanExeName, prioClass));

        await RefreshPersistedRulesAsync();
        await RefreshRunningProcessesAsync();

        if (ifeoWritten)
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛡️ 已為 {cleanExeName} 建立 IFEO 常駐優先權規則 ({prioStr})");
            StatusMessage = $"已為 {cleanExeName} 建立 IFEO 優先權 ({prioStr})，開機與每次啟動自動生效！";
            AudioFeedbackService.PlaySuccess();
        }
        else
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚠️ IFEO 註冊表寫入需要系統管理員權限，已先儲存於系統設定。");
            StatusMessage = $"已先儲存 {cleanExeName} 於設定中，若要直接寫入 IFEO 建議以系統管理員權限執行。";
            AudioFeedbackService.PlaySuccess();
        }
    }

    [RelayCommand]
    public async Task DeleteRuleAsync(PersistedPriorityRuleItem? rule)
    {
        if (rule == null || string.IsNullOrWhiteSpace(rule.ProcessName)) return;

        var procName = rule.ProcessName.Trim();
        await _optimizer.CreatePreTweakBackupAsync($"Before_Delete_IFEO_{Path.GetFileNameWithoutExtension(procName)}");

        await Task.Run(() => _optimizer.DeleteIfeoCpuPriority(procName));

        SettingsService.Instance.Current.ProcessCpuPriorities.Remove(procName);
        var noExe = procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? procName.Substring(0, procName.Length - 4) : procName;
        SettingsService.Instance.Current.ProcessCpuPriorities.Remove(noExe);
        SettingsService.Instance.Save();

        await RefreshPersistedRulesAsync();
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🗑️ 已刪除 {procName} 之 IFEO 優先權規則");
        StatusMessage = $"已刪除 {procName} 之優先權規則";
        AudioFeedbackService.PlaySuccess();
    }

    [RelayCommand]
    public async Task TogglePowerThrottlingAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_PowerThrottling");
        bool targetPowerThrottling = !IsPowerThrottlingDisabled;
        if (!_optimizer.SetPowerThrottlingDisabled(targetPowerThrottling))
        {
            RevertSwitch(() => IsPowerThrottlingDisabled = !targetPowerThrottling);
            ReportTuningNotApplied("CPU 能源節流 (PowerThrottling)");
            return;
        }
        IsPowerThrottlingDisabled = targetPowerThrottling;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ CPU 能源節流狀態: {(targetPowerThrottling ? "已停用 (全速無降頻)" : "已啟用 (節能)")}");
    }

    [RelayCommand]
    public async Task ToggleSystemResponsivenessAsync()
    {
        int target = IsSystemResponsivenessZero ? 20 : 10;
        await SetSystemResponsivenessValueAsync(target);
    }

    [RelayCommand]
    public async Task SetSystemResponsivenessValueAsync(int val)
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_SystemResponsiveness");
        int previousResponsiveness = SystemResponsivenessValue;
        if (!_optimizer.SetSystemResponsiveness(val))
        {
            RevertSwitch(() =>
            {
                SystemResponsivenessValue = previousResponsiveness;
                IsSystemResponsivenessZero = previousResponsiveness <= 10;
            });
            ReportTuningNotApplied($"MMCSS SystemResponsiveness = {val}%");
            return;
        }
        SystemResponsivenessValue = val;
        IsSystemResponsivenessZero = val <= 10;
        string modeName = val switch
        {
            0 => "極限電競 (0% 保留，100% 前台獨占)",
            10 => "EvolveOpt 推薦 (10% 保留，防微卡頓最佳平衡)",
            _ => $"官方預設 ({val}% 保留)"
        };
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🚀 MMCSS SystemResponsiveness 已設為: {val} [{modeName}]");
    }

    [RelayCommand]
    public async Task ToggleNetworkThrottlingAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_NetworkThrottling");
        bool targetNetworkThrottling = !IsNetworkThrottlingDisabled;
        if (!_optimizer.SetNetworkThrottlingDisabled(targetNetworkThrottling))
        {
            RevertSwitch(() => IsNetworkThrottlingDisabled = !targetNetworkThrottling);
            ReportTuningNotApplied("網路封包節流佇列 (NetworkThrottlingIndex)");
            return;
        }
        IsNetworkThrottlingDisabled = targetNetworkThrottling;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🌐 網路封包節流佇列: {(targetNetworkThrottling ? "已停用 (防 Ping 突增/丟包)" : "已啟用 (預設)")}");
    }

    [RelayCommand]
    public async Task ToggleGameDvrAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_GameDVR");
        bool targetGameDvr = !IsGameDvrDisabled;
        if (!_optimizer.SetGameDvrDisabled(targetGameDvr))
        {
            RevertSwitch(() => IsGameDvrDisabled = !targetGameDvr);
            ReportTuningNotApplied("Game DVR / Game Bar 背景錄影");
            return;
        }
        IsGameDvrDisabled = targetGameDvr;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛡️ Game DVR 背景開銷: {(targetGameDvr ? "已關閉 (釋放顯卡與CPU)" : "已啟用 (預設)")}");
    }

    [RelayCommand]
    public async Task ToggleHagsAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_HAGS");
        bool targetHags = !IsHagsEnabled;
        if (!_optimizer.SetHagsStatus(targetHags))
        {
            RevertSwitch(() => IsHagsEnabled = !targetHags);
            ReportTuningNotApplied("硬體加速 GPU 排程 (HAGS)");
            return;
        }
        IsHagsEnabled = targetHags;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🖥️ 硬體加速 GPU 排程 (HAGS): {(targetHags ? "已開啟 (需重新開機生效)" : "已關閉")}");
    }

    [RelayCommand]
    public async Task ToggleInputLatencyAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_InputLatency");
        bool targetInputLatency = !IsLowInputLatencyEnabled;
        if (!_optimizer.ConfigureInputLatency(targetInputLatency))
        {
            RevertSwitch(() => IsLowInputLatencyEnabled = !targetInputLatency);
            ReportTuningNotApplied("滑鼠 1:1 感應器追蹤與按鍵零延遲");
            return;
        }
        IsLowInputLatencyEnabled = targetInputLatency;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🖱️ 滑鼠 1:1 感應器追蹤與按鍵零延遲: {(targetInputLatency ? "已優化" : "已還原預設")}");
    }

    // ══════════════════════════════════════════════════════════
    //  Power Plan Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task SetActivePowerPlanAsync(PowerPlanItem? item)
    {
        if (item == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"正在切換至電源計畫: {item.Name}...";
            var res = await _powerService.SetActivePlanAsync(item.Guid);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🔋 切換電源計畫: {item.Name} ({res})");
            await RefreshAllAsync();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task UnlockUltimatePerformanceAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在解鎖終極效能 (Ultimate Performance)...";
            var (success, msg, _) = await _powerService.UnlockUltimatePerformanceAsync();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ {msg}");
            await RefreshAllAsync();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task UnlockHighPerformanceAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在解鎖高效能 (High Performance)...";
            var (success, msg, _) = await _powerService.UnlockHighPerformanceAsync();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🚀 {msg}");
            await RefreshAllAsync();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task ToggleCoreUnparkingAsync()
    {
        try
        {
            bool targetUnpark = !IsCoreUnparked;
            var res = await _powerService.ConfigureCoreUnparkingAsync(targetUnpark);
            if (!string.Equals(res, "Success", StringComparison.OrdinalIgnoreCase))
            {
                RevertSwitch(() => IsCoreUnparked = !targetUnpark);
                StatusMessage = $"❌ CPU 核心防停泊設定未套用: {res}";
                AppendLog($"[{DateTime.Now:HH:mm:ss}] ❌ CPU 核心防停泊設定失敗: {res}");
                AudioFeedbackService.PlayError();
                return;
            }
            IsCoreUnparked = targetUnpark;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ CPU 核心防停泊 (Core Unparking): {(targetUnpark ? "已開啟 (隨時全核待命)" : "已關閉")}");
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    [RelayCommand]
    public async Task ToggleMinProcessorStateAsync()
    {
        try
        {
            bool targetMinState = !IsMinProcessorState100;
            var res = await _powerService.ConfigureMinProcessorStateAsync(targetMinState ? 100 : 5);
            if (!string.Equals(res, "Success", StringComparison.OrdinalIgnoreCase))
            {
                RevertSwitch(() => IsMinProcessorState100 = !targetMinState);
                StatusMessage = $"❌ 最低處理器頻率設定未套用: {res}";
                AppendLog($"[{DateTime.Now:HH:mm:ss}] ❌ 最低處理器頻率設定失敗: {res}");
                AudioFeedbackService.PlayError();
                return;
            }
            IsMinProcessorState100 = targetMinState;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ 插電時最低處理器頻率: {(targetMinState ? "鎖定 100% (防轉場掉頻)" : "預設 5%")}");
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    [RelayCommand]
    public async Task DeletePowerPlanAsync(PowerPlanItem? item)
    {
        if (item == null || item.IsActive) return;
        try
        {
            var res = await _powerService.DeletePowerPlanAsync(item.Guid);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🗑️ 刪除電源計畫: {item.Name} ({res})");
            await RefreshAllAsync();
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    // ══════════════════════════════════════════════════════════
    //  Backup & Restore Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task CreateManualBackupAsync()
    {
        try
        {
            var path = await _optimizer.CreateManualBackupAsync(ManualBackupDescription);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 💾 已建立手動登錄檔備份: {Path.GetFileName(path)}");
            await RefreshAllAsync();
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    [RelayCommand]
    public async Task RestoreBackupAsync(RegistryBackupItem? item)
    {
        if (item == null) return;
        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "還原登錄檔備份 (Restore Registry Backup)",
            $"即將匯入備份檔案: {item.FileName}，這將覆寫目前系統中對應的登錄檔鍵值。",
            item.FileName);
        if (!confirmed) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"正在還原 {item.FileName}...";
            var res = await _optimizer.RestoreRegistryFileAsync(item.FilePath);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🔄 還原備份: {item.FileName} ({res})");
            await RefreshAllAsync();
            StatusMessage = string.Equals(res, "Success", StringComparison.OrdinalIgnoreCase)
                ? "登錄檔備份還原完成！"
                : $"❌ 登錄檔備份還原失敗: {res}";
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void DeleteBackup(RegistryBackupItem? item)
    {
        if (item == null) return;
        if (!_optimizer.DeleteBackup(item.FilePath))
        {
            StatusMessage = $"❌ 刪除備份檔失敗: {item.FileName} (檔案可能正被其他程式使用)";
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ❌ 刪除備份檔失敗: {item.FileName}");
            AudioFeedbackService.PlayError();
            return;
        }
        Backups.Remove(item);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🗑️ 已刪除備份檔: {item.FileName}");
    }

    [RelayCommand]
    public async Task CreateRestorePointAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在建立 Windows 系統還原點 (這可能需要數十秒)...";
            var (success, msg) = await _optimizer.CreateSystemRestorePointAsync("DiskMaster Pro 系統優化還原點");
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛡️ {msg}");
            await RefreshAllAsync();
            StatusMessage = msg;
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void OpenBackupFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_optimizer.GetBackupDirectory()}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════
    //  MMAgent Memory Management Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task ToggleMemoryCompressionAsync(bool enable)
    {
        try
        {
            IsUpdatingProgrammatically = true;
            var (success, msg) = await _memoryManager.SetMemoryCompressionAsync(enable);
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsMemoryCompressionEnabled = enable;
                AppendLog($"[{DateTime.Now:HH:mm:ss}] 🧠 {msg}");
                StatusMessage = msg;
            });
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally
        {
            DispatcherHelper.RunOnUIThread(() => IsUpdatingProgrammatically = false);
        }
    }

    [RelayCommand]
    public async Task TogglePageCombiningAsync(bool enable)
    {
        try
        {
            IsUpdatingProgrammatically = true;
            var (success, msg) = await _memoryManager.SetPageCombiningAsync(enable);
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsPageCombiningEnabled = enable;
                AppendLog($"[{DateTime.Now:HH:mm:ss}] 🧠 {msg}");
                StatusMessage = msg;
            });
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally
        {
            DispatcherHelper.RunOnUIThread(() => IsUpdatingProgrammatically = false);
        }
    }

    [RelayCommand]
    public async Task ToggleAppPreLaunchAsync(bool enable)
    {
        try
        {
            IsUpdatingProgrammatically = true;
            var (success, msg) = await _memoryManager.SetApplicationPreLaunchAsync(enable);
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsAppPreLaunchEnabled = enable;
                AppendLog($"[{DateTime.Now:HH:mm:ss}] 🧠 {msg}");
                StatusMessage = msg;
            });
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally
        {
            DispatcherHelper.RunOnUIThread(() => IsUpdatingProgrammatically = false);
        }
    }

    [RelayCommand]
    public async Task ToggleAppLaunchProfilingAsync(bool enable)
    {
        try
        {
            IsUpdatingProgrammatically = true;
            var (success, msg) = await _memoryManager.SetApplicationLaunchProfilingAsync(enable);
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsAppLaunchProfilingEnabled = enable;
                AppendLog($"[{DateTime.Now:HH:mm:ss}] 🧠 {msg}");
                StatusMessage = msg;
            });
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally
        {
            DispatcherHelper.RunOnUIThread(() => IsUpdatingProgrammatically = false);
        }
    }

    [RelayCommand]
    public async Task ToggleOperationRecordingAsync(bool enable)
    {
        try
        {
            IsUpdatingProgrammatically = true;
            var (success, msg) = await _memoryManager.SetOperationRecordingAsync(enable);
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsOperationRecordingEnabled = enable;
                AppendLog($"[{DateTime.Now:HH:mm:ss}] 🧠 {msg}");
                StatusMessage = msg;
            });
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally
        {
            DispatcherHelper.RunOnUIThread(() => IsUpdatingProgrammatically = false);
        }
    }

    [RelayCommand]
    public async Task ApplyGamingRamProfileAsync()
    {
        try
        {
            IsLoading = true;
            IsUpdatingProgrammatically = true;
            StatusMessage = "正在套用電競低延遲記憶體 (0 壓縮 / 0 合併)...";
            var (success, msg) = await _memoryManager.ApplyGamingRamProfileAsync();
            var mm = await _memoryManager.GetStatusAsync();
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsMemoryCompressionEnabled = mm.MemoryCompression;
                IsPageCombiningEnabled = mm.PageCombining;
                AppendLog($"[{DateTime.Now:HH:mm:ss}] 🎮 {msg}");
                StatusMessage = msg;
            });
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsUpdatingProgrammatically = false;
                IsLoading = false;
            });
        }
    }

    [RelayCommand]
    public async Task RestoreStockRamProfileAsync()
    {
        try
        {
            IsLoading = true;
            IsUpdatingProgrammatically = true;
            StatusMessage = "正在還原官方預設記憶體配置...";
            var (success, msg) = await _memoryManager.RestoreStockDefaultsAsync();
            var mm = await _memoryManager.GetStatusAsync();
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsMemoryCompressionEnabled = mm.MemoryCompression;
                IsPageCombiningEnabled = mm.PageCombining;
                IsAppPreLaunchEnabled = mm.ApplicationPreLaunch;
                IsAppLaunchProfilingEnabled = mm.ApplicationLaunchProfiling;
                IsOperationRecordingEnabled = mm.OperationRecording;
                AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛡️ {msg}");
                StatusMessage = msg;
            });
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                IsUpdatingProgrammatically = false;
                IsLoading = false;
            });
        }
    }

    // ══════════════════════════════════════════════════════════
    //  PowerCfg & Sleep Management Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task RefreshWakeDevicesAsync()
    {
        try
        {
            DispatcherHelper.RunOnUIThread(() => IsLoadingWakeDevices = true);
            var devices = await _powerCfg.GetWakeDevicesAsync();
            DispatcherHelper.RunOnUIThread(() =>
            {
                WakeDevices.Clear();
                foreach (var d in devices) WakeDevices.Add(d);
            });
        }
        catch { }
        finally
        {
            DispatcherHelper.RunOnUIThread(() => IsLoadingWakeDevices = false);
        }
    }

    [RelayCommand]
    public async Task RefreshSleepBlockersAsync()
    {
        try
        {
            DispatcherHelper.RunOnUIThread(() => IsLoadingSleepBlockers = true);
            var blockers = await _powerCfg.GetSleepBlockersAsync();
            DispatcherHelper.RunOnUIThread(() =>
            {
                SleepBlockers.Clear();
                foreach (var b in blockers) SleepBlockers.Add(b);
                HasSleepBlockers = SleepBlockers.Count > 0;
                HasScannedSleepBlockers = true;
            });
        }
        catch { }
        finally
        {
            DispatcherHelper.RunOnUIThread(() => IsLoadingSleepBlockers = false);
        }
    }

    [RelayCommand]
    public async Task RebuildIconCacheAsync()
    {
        try
        {
            IsRebuildingIconCache = true;
            IconCacheStatus = "正在清理圖示快取並重啟檔案總管...";
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🖼️ 開始執行檔案總管圖示與縮圖快取深度重建...");

            var progress = new Progress<string>(msg =>
            {
                IconCacheStatus = msg;
                AppendLog($"[{DateTime.Now:HH:mm:ss}] 🖼️ {msg}");
            });

            var res = await IconCacheRepairService.Instance.RebuildIconCacheAsync(progress);
            IconCacheStatus = res.Message;
            StatusMessage = res.Message;
        }
        catch (Exception ex)
        {
            IconCacheStatus = $"❌ 重建過程發生異常: {ex.Message}";
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally
        {
            IsRebuildingIconCache = false;
        }
    }

    [RelayCommand]
    public async Task ToggleDeviceWakeAsync(PowerWakeDeviceItem? item)
    {
        if (item == null) return;
        try
        {
            var (success, msg) = await _powerCfg.SetDeviceWakeStateAsync(item.DeviceName, item.IsArmed);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🔌 {msg}");
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ToggleHibernationAsync(bool enable)
    {
        try
        {
            IsLoading = true;
            var (success, msg) = await _powerCfg.SetHibernationAsync(enable);
            IsHibernationEnabled = enable;
            HiberfilSizeDisplay = _powerCfg.GetHiberfilSizeDisplay();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ {msg}");
            StatusMessage = msg;
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task ToggleFastStartupReducedAsync(bool reduced)
    {
        try
        {
            IsLoading = true;
            var (success, msg) = await _powerCfg.SetFastStartupReducedModeAsync(reduced);
            IsFastStartupReduced = reduced;
            HiberfilSizeDisplay = _powerCfg.GetHiberfilSizeDisplay();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ {msg}");
            StatusMessage = msg;
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task ToggleProcessorBoostUnhideAsync(bool unhide)
    {
        try
        {
            IsLoading = true;
            var (success, msg) = await _powerCfg.SetProcessorBoostModeUnhideAsync(unhide);
            IsProcessorBoostUnhidden = unhide;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ {msg}");
            StatusMessage = msg;
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task GenerateBatteryReportAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在產生 Windows 電池健康診斷報告...";
            var (success, path) = await _powerCfg.GenerateBatteryReportAsync();
            if (success)
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] 🔋 電池報告已產生: {path}");
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                StatusMessage = "電池健康報告已於瀏覽器開啟。";
            }
            else
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] ❌ 電池報告失敗: {path}");
                StatusMessage = "電池報告失敗 (可能非筆記型電腦或不具備電池)。";
            }
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task GenerateEnergyReportAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "正在產生 Windows 能耗追蹤診斷報告 (需 5 秒鐘)...";
            var (success, path) = await _powerCfg.GenerateEnergyReportAsync();
            if (success)
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ 能耗報告已產生: {path}");
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                StatusMessage = "能耗診斷報告已於瀏覽器開啟。";
            }
            else
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] ❌ 能耗報告失敗: {path}");
                StatusMessage = "能耗診斷報告失敗。";
            }
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void ClearLog()
    {
        ConsoleLog = "";
    }

    // ══════════════════════════════════════════════════════════
    //  OEM Driver Management Commands (PnPUtil)
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task RefreshDriversAsync()
    {
        try
        {
            DispatcherHelper.RunOnUIThread(() => IsLoadingDrivers = true);
            var drivers = await Task.Run(async () => await _driverService.EnumDriversStructuredAsync());
            DispatcherHelper.RunOnUIThread(() =>
            {
                OemDrivers.Clear();
                foreach (var d in drivers) OemDrivers.Add(d);
                if (drivers.Count > 0)
                {
                    AppendLog($"[{DateTime.Now:HH:mm:ss}] 🏎️ 成功載入 {drivers.Count} 個 OEM 驅動程式封裝清單。");
                }
            });
        }
        catch (Exception ex)
        {
            var msg = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚠️ 列舉 OEM 驅動程式失敗: {msg}");
        }
        finally
        {
            DispatcherHelper.RunOnUIThread(() => IsLoadingDrivers = false);
        }
    }

    [RelayCommand]
    public async Task DeleteDriverAsync(OemDriverItem? driver)
    {
        if (driver == null) return;
        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "卸載 OEM 驅動程式 (Uninstall Driver)",
            $"即將強制移除驅動檔案: {driver.PublishedName} ({driver.OriginalFileName} - {driver.DriverClass})。\n若此驅動為系統關鍵裝置，可能導致硬體無法正常運作！",
            driver.PublishedName);
        if (!confirmed) return;

        try
        {
            IsLoading = true;
            var res = await _driverService.DeleteDriverAsync(driver.PublishedName, force: true);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🗑️ 卸載驅動 {driver.PublishedName}: {res}");
            AudioFeedbackService.PlaySuccess();
            await RefreshDriversAsync();
        }
        catch (Exception ex)
        {
            AudioFeedbackService.PlayError();
            AppendLog($"[ERROR] 卸載驅動失敗: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BatchDeleteSelectedDriversAsync()
    {
        var selected = OemDrivers.Where(d => d.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "批次卸載選取之 OEM 驅動程式",
            $"即將批次移除 {selected.Count} 個選取的驅動程式。請確認這些驅動並非當前運行所需之關鍵核心硬體！",
            "DELETE_BATCH");
        if (!confirmed) return;

        try
        {
            IsLoading = true;
            var res = await _driverService.BatchDeleteDriversAsync(selected.Select(s => s.PublishedName), force: true);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🗑️ 批次卸載驅動完成: 成功 {res.SuccessCount} 項，失敗 {res.FailureCount} 項");
            AudioFeedbackService.PlaySuccess();
            await RefreshDriversAsync();
        }
        catch (Exception ex)
        {
            AudioFeedbackService.PlayError();
            AppendLog($"[ERROR] 批次卸載失敗: {ex.Message}");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void SelectAllDrivers(bool select)
    {
        foreach (var d in OemDrivers) d.IsSelected = select;
    }

    // ══════════════════════════════════════════════════════════
    //  OneDrive Deep Management & Repair Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task RefreshOneDriveStatusAsync()
    {
        try
        {
            var status = await _oneDriveService.DetectOneDriveStatusAsync();
            DispatcherHelper.RunOnUIThread(() =>
            {
                OneDriveStatus = status;
                IsOneDriveInstalled = status.IsInstalled;
                IsOneDriveRunning = status.IsRunning;
                IsOneDriveFoldersRedirected = status.IsFoldersRedirected;
                HasOneDriveCloudOnlyFiles = status.HasCloudOnlyFiles;
                OneDriveCloudOnlyCount = status.CloudOnlyFileCount;
                IsOneDrivePinned = status.IsFileExplorerPinned;
                IsOneDrivePolicyBlocked = status.IsPolicyBlocked;

                OnPropertyChanged(nameof(OneDriveInstalledStatusText));
                OnPropertyChanged(nameof(OneDriveRunningStatusText));
                OnPropertyChanged(nameof(OneDriveRedirectStatusText));
                OnPropertyChanged(nameof(OneDriveCloudFilesStatusText));
                OnPropertyChanged(nameof(OneDriveGhostIconStatusText));
                OnPropertyChanged(nameof(OneDrivePolicyStatusText));
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] 偵測 OneDrive 狀態失敗: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task DeepUninstallOneDriveAsync()
    {
        if (HasOneDriveCloudOnlyFiles)
        {
            var warnConfirm = await DialogHelper.ConfirmDestructiveOperationAsync(
                WindowHelper.GetXamlRoot(),
                "⚠️ 偵測到雲端脫機檔案 (Files On-Demand)",
                $"您的 OneDrive 中有約 {OneDriveCloudOnlyCount} 個檔案僅存在於雲端 (尚未完全下載到本機硬碟)。\n解除安裝後這些檔案可能無法於本機開啟！\n強烈建議您先登入 OneDrive 下載所有檔案，或確認不需保留。\n\n確定仍要強制卸載嗎？",
                "CONFIRM_OFFLINE_RISK");
            if (!warnConfirm) return;
        }
        else
        {
            var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
                WindowHelper.GetXamlRoot(),
                "徹底解除安裝 OneDrive (Deep Uninstall)",
                "即將徹底移除 OneDrive、終止其背景程序、清理相關快取檔案，並自動將桌面/文件/圖片資料夾安全移回本機 %USERPROFILE% 原生路徑。\n本操作會永久封鎖 Windows Update 再次靜默安裝 OneDrive。",
                "UNINSTALL_ONEDRIVE");
            if (!confirmed) return;
        }

        try
        {
            IsOneDriveOperating = true;
            StatusMessage = "正在徹底卸載 OneDrive 並還原原生資料夾路徑...";
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🚀 開始執行 OneDrive 深度徹底解除安裝流程...");

            var (success, msg) = await _oneDriveService.DeepUninstallOneDriveAsync(
                restoreShellFolders: true,
                cleanResiduals: true,
                blockReinstall: true);

            AppendLog($"[{DateTime.Now:HH:mm:ss}] {(success ? "✅" : "⚠️")} {msg}");
            if (success) AudioFeedbackService.PlaySuccess();
            else AudioFeedbackService.PlayWarning();

            await RefreshOneDriveStatusAsync();
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            AudioFeedbackService.PlayError();
            AppendLog($"[ERROR] 解除安裝 OneDrive 發生例外: {ex.Message}");
        }
        finally { IsOneDriveOperating = false; }
    }

    [RelayCommand]
    public async Task RestoreOneDriveShellFoldersAsync()
    {
        try
        {
            IsOneDriveOperating = true;
            StatusMessage = "正在還原個人資料夾路徑至 %USERPROFILE%...";
            var (success, msg) = await _oneDriveService.RestoreUserShellFoldersAsync();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {(success ? "✅" : "⚠️")} {msg}");
            if (success) AudioFeedbackService.PlaySuccess();
            await RefreshOneDriveStatusAsync();
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            AudioFeedbackService.PlayError();
            AppendLog($"[ERROR] 資料夾還原失敗: {ex.Message}");
        }
        finally { IsOneDriveOperating = false; }
    }

    [RelayCommand]
    public async Task RemoveOneDriveGhostIconAsync()
    {
        try
        {
            IsOneDriveOperating = true;
            var (success, msg) = await _oneDriveService.RemoveExplorerGhostIconAsync();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {(success ? "✅" : "⚠️")} {msg}");
            if (success) AudioFeedbackService.PlaySuccess();
            await RefreshOneDriveStatusAsync();
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            AudioFeedbackService.PlayError();
            AppendLog($"[ERROR] 清除幽靈圖示失敗: {ex.Message}");
        }
        finally { IsOneDriveOperating = false; }
    }

    [RelayCommand]
    public async Task ResetOneDriveSyncEngineAsync()
    {
        try
        {
            IsOneDriveOperating = true;
            var (success, msg) = await _oneDriveService.ResetOneDriveSyncEngineAsync();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {(success ? "✅" : "⚠️")} {msg}");
            if (success) AudioFeedbackService.PlaySuccess();
            await RefreshOneDriveStatusAsync();
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            AudioFeedbackService.PlayError();
            AppendLog($"[ERROR] 重設同步引擎失敗: {ex.Message}");
        }
        finally { IsOneDriveOperating = false; }
    }

    [RelayCommand]
    public async Task ToggleOneDrivePolicyBlockAsync()
    {
        try
        {
            bool newTarget = !IsOneDrivePolicyBlocked;
            var (success, msg) = await _oneDriveService.ToggleOneDrivePolicyBlockAsync(newTarget);
            if (!success)
            {
                // keep the previous state; the service message explains the refusal
                AppendLog($"[{DateTime.Now:HH:mm:ss}] ❌ {msg}");
                StatusMessage = msg;
                AudioFeedbackService.PlayError();
                return;
            }
            IsOneDrivePolicyBlocked = newTarget;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛡️ {msg}");
            AudioFeedbackService.PlaySuccess();
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] 切換群組原則封鎖失敗: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  Advanced Latency, Memory & Group Policy Tweaks
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public void ToggleNagleAlgorithm()
    {
        try
        {
            bool targetNagle = !IsNagleDisabled;
            if (!_optimizer.ConfigureNagleAlgorithm(targetNagle))
            {
                RevertSwitch(() => IsNagleDisabled = !targetNagle);
                ReportTuningNotApplied("Nagle's Algorithm (TcpAckFrequency / TCPNoDelay)");
                return;
            }
            IsNagleDisabled = targetNagle;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🌐 Nagle's Algorithm (TcpAckFrequency / TCPNoDelay): {(targetNagle ? "已停用 (超低延遲模式)" : "已啟用 (標準模式)")}");
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    [RelayCommand]
    public void ToggleDisablePagingExecutive()
    {
        try
        {
            bool targetPagingExecutive = !IsDisablePagingExecutive;
            if (!_optimizer.ConfigureMemoryManagement(targetPagingExecutive))
            {
                RevertSwitch(() => IsDisablePagingExecutive = !targetPagingExecutive);
                ReportTuningNotApplied("核心常駐實體 RAM (DisablePagingExecutive)");
                return;
            }
            IsDisablePagingExecutive = targetPagingExecutive;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🧠 核心常駐實體 RAM (DisablePagingExecutive): {(targetPagingExecutive ? "已開啟 (常駐記憶體無分頁延遲)" : "已關閉")}");
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    [RelayCommand]
    public void ToggleGroupPolicyTelemetry()
    {
        try
        {
            bool targetGroupPolicy = !IsGroupPolicyTelemetryDisabled;
            if (!_optimizer.ConfigureGroupPolicyPrivacy(targetGroupPolicy))
            {
                RevertSwitch(() => IsGroupPolicyTelemetryDisabled = !targetGroupPolicy);
                ReportTuningNotApplied("本機群組原則隱私與遙測防護");
                return;
            }
            IsGroupPolicyTelemetryDisabled = targetGroupPolicy;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛡️ 本機群組原則隱私與遙測防護: {(targetGroupPolicy ? "已套用極致隱私原則 (關閉遙測/廣告/P2P上傳)" : "已還原預設")}");
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    [RelayCommand]
    public async Task ApplyCpuEppAsync()
    {
        try
        {
            var (ok, msg) = await _powerCfg.SetEnergyPerformancePreferenceAsync(CpuEppSliderValue);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ {msg}");
            StatusMessage = msg;
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    [RelayCommand]
    public async Task ApplyCpuCoreParkingAsync()
    {
        try
        {
            var (ok, msg) = await _powerCfg.SetCoreParkingAsync(CpuCoreParkingMinPercent);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ {msg}");
            StatusMessage = msg;
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    [RelayCommand]
    public async Task ApplyCpuBoostModeAsync()
    {
        try
        {
            var (ok, msg) = await _powerCfg.SetProcessorBoostModeValueAsync(SelectedCpuBoostModeIndex);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ {msg}");
            StatusMessage = msg;
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    [RelayCommand]
    public async Task UnhideAllCpuPowerAttributesAsync()
    {
        try
        {
            IsLoading = true;
            var (ok, msg) = await _powerCfg.UnhideAllProcessorAttributesAsync();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🎛️ {msg}");
            StatusMessage = msg;
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally { IsLoading = false; }
    }

    // ══════════════════════════════════════════════════════════
    //  Item 5, 7, 9: L2TP/IPsec VPN NAT-T Fix, DPS & DiagTrack
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task ToggleL2tpVpnNatFixAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_L2tpVpnNatFix");
        bool target = !IsL2tpVpnNatFixEnabled;
        if (!_optimizer.SetL2tpVpnNatFix(target))
        {
            RevertSwitch(() => IsL2tpVpnNatFixEnabled = !target);
            ReportTuningNotApplied("L2TP/IPsec VPN NAT-T (AssumeUDPEncapsulationContextOnSendRule = 2)");
            return;
        }

        L2tpVpnNatRuleValue = _optimizer.GetL2tpVpnNatRuleValue();
        IsL2tpVpnNatFixEnabled = L2tpVpnNatRuleValue == 2;
        string msg = target
            ? "✅ 已寫入 HKLM\\SYSTEM\\CurrentControlSet\\Services\\PolicyAgent\\AssumeUDPEncapsulationContextOnSendRule = 2 (REG_DWORD)！重新開機後即可修復 L2TP/IPsec VPN NAT 連線失敗。"
            : "🔄 已移除 AssumeUDPEncapsulationContextOnSendRule 鍵值，還原為 Windows 預設。";
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🌐 {msg}");
        StatusMessage = msg;
        AudioFeedbackService.PlaySuccess();
    }

    [RelayCommand]
    public async Task ToggleDpsServiceAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_DpsService");
        bool targetDisable = !IsDpsServiceDisabled;
        if (targetDisable)
        {
            var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
                WindowHelper.GetXamlRoot(),
                "停用 DPS 診斷原則服務 (Disable Diagnostic Policy Service)",
                "即將將 HKLM\\SYSTEM\\CurrentControlSet\\Services\\DPS 的 Start 設為 4 (Disabled) 並停止 DPS 服務。\n這能徹底防止背景疑難排解服務造成的 svchost 記憶體膨脹 (Memory Bloat)，但停用期間 Windows 內建自動疑難排解精靈將暫停運作。\n\n確定要停用 DPS 服務嗎？",
                "DPS Start=4");
            if (!confirmed)
            {
                RevertSwitch(() => IsDpsServiceDisabled = false);
                return;
            }
        }

        var (ok, msg) = await _optimizer.SetDpsServiceDisabledAsync(targetDisable);
        if (!ok)
        {
            RevertSwitch(() => IsDpsServiceDisabled = !targetDisable);
            ReportTuningNotApplied("DPS 診斷原則服務 (Start = 4)");
            return;
        }

        IsDpsServiceDisabled = targetDisable;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🧠 {msg}");
        StatusMessage = msg;
        AudioFeedbackService.PlaySuccess();
    }

    [RelayCommand]
    public async Task TurnOffDiagTrackAsync()
    {
        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "關閉 DiagTrack 遙測服務 (Turn Off DiagTrack)",
            "即將執行：\n• sc stop DiagTrack\n• sc config DiagTrack start= disabled\n\n關閉 Connected User Experiences and Telemetry 後可消除背景遙測上傳與磁碟/CPU 佔用，且 Windows Update 依然完全正常運作。",
            "sc stop DiagTrack");
        if (!confirmed) return;

        await _optimizer.CreatePreTweakBackupAsync("Before_DiagTrackOff");
        var (ok, msg) = await _optimizer.SetDiagTrackServiceDisabledAsync(true);
        if (!ok)
        {
            ReportTuningNotApplied("DiagTrack 服務關閉 (sc stop DiagTrack)");
            return;
        }

        IsDiagTrackDisabled = true;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛡️ {msg}");
        StatusMessage = msg;
        AudioFeedbackService.PlaySuccess();
    }

    [RelayCommand]
    public async Task TurnOnDiagTrackAsync()
    {
        var (ok, msg) = await _optimizer.SetDiagTrackServiceDisabledAsync(false);
        if (!ok)
        {
            ReportTuningNotApplied("DiagTrack 服務啟用 (sc start DiagTrack)");
            return;
        }

        IsDiagTrackDisabled = false;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🔄 {msg}");
        StatusMessage = msg;
        AudioFeedbackService.PlaySuccess();
    }

    // ══════════════════════════════════════════════════════════
    //  Item 11, 12, 13, 14: Explorer, Context Menu, Folders & Icons
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task SwitchToClassicContextMenuAsync()
    {
        IsLoading = true;
        try
        {
            var (ok, msg) = await _folderCustomizer.SetClassicContextMenuAsync(useClassicMenu: true, restartExplorer: true);
            IsClassicContextMenu = _folderCustomizer.IsClassicContextMenuEnabled();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🖱️ {msg}");
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task SwitchToModernContextMenuAsync()
    {
        IsLoading = true;
        try
        {
            var (ok, msg) = await _folderCustomizer.SetClassicContextMenuAsync(useClassicMenu: false, restartExplorer: true);
            IsClassicContextMenu = _folderCustomizer.IsClassicContextMenuEnabled();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🪟 {msg}");
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task FixSlowRightClickMenuAsync()
    {
        IsLoading = true;
        try
        {
            var (ok, msg, _) = await _folderCustomizer.FixSlowRightClickMenuAsync(restartExplorer: false);
            IsRightClickSpeedOptimized = _folderCustomizer.IsRightClickSpeedOptimized();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ {msg}");
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void ToggleGlobalFastFolderDiscovery()
    {
        bool target = !IsGlobalFastFolderDiscoveryEnabled;
        var (ok, msg) = _folderCustomizer.SetGlobalFastFolderDiscovery(target, resetShellBagsCache: false);
        if (!ok)
        {
            RevertSwitch(() => IsGlobalFastFolderDiscoveryEnabled = !target);
            StatusMessage = msg;
            return;
        }
        IsGlobalFastFolderDiscoveryEnabled = target;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 📂 {msg}");
        StatusMessage = msg;
        AudioFeedbackService.PlaySuccess();
    }

    [RelayCommand]
    public async Task ResetShellBagsAndEnableFastFoldersAsync()
    {
        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "重設全部資料夾檢視記憶並鎖定極速載入 (Reset Shell Bags)",
            "即將清除 Windows Shell Bags 歷史記憶並將全域資料夾預設鎖定為「一般項目 (NotSpecified)」，徹底修復大型資料夾開啟轉圈與綠條讀條卡死問題。\n檔案總管將會自動重啟一次。",
            "Reset ShellBags");
        if (!confirmed) return;

        var (ok, msg) = _folderCustomizer.SetGlobalFastFolderDiscovery(enableFastGeneralItems: true, resetShellBagsCache: true);
        IsGlobalFastFolderDiscoveryEnabled = _folderCustomizer.IsGlobalFastFolderDiscoveryEnabled();
        await ExplorerWatchdogService.Instance.RestartExplorerManuallyAsync();
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🚀 {msg}");
        StatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess();
    }

    [RelayCommand]
    public async Task BrowseCustomizeFolderAsync()
    {
        var path = await FilePickerHelper.PickFolderAsync("選擇要最佳化或自訂圖示的資料夾");
        if (!string.IsNullOrWhiteSpace(path))
        {
            TargetCustomizeFolderPath = path;
            StatusMessage = $"已選取目標資料夾: {path}";
        }
    }

    [RelayCommand]
    public async Task BrowseFolderPictureAsync()
    {
        var file = await FilePickerHelper.PickSingleFileAsync([".png", ".jpg", ".jpeg", ".bmp", ".ico"]);
        if (!string.IsNullOrWhiteSpace(file))
        {
            TargetFolderPicturePath = file;
            StatusMessage = $"已選取資料夾圖片: {file}";
        }
    }

    [RelayCommand]
    public void ClearFolderPicture()
    {
        TargetFolderPicturePath = "";
    }

    [RelayCommand]
    public async Task ApplyFolderOptimizationAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetCustomizeFolderPath))
        {
            StatusMessage = "請先選擇或輸入目標資料夾路徑！";
            AudioFeedbackService.PlayWarning();
            return;
        }

        IsLoading = true;
        try
        {
            var templates = FolderAndIconCustomizerService.FolderTemplates;
            int idx = Math.Clamp(SelectedFolderTemplateIndex, 0, templates.Count - 1);
            string templateKey = templates[idx].Key;
            string? logo = string.IsNullOrWhiteSpace(TargetFolderPicturePath) ? "" : TargetFolderPicturePath;

            var (ok, msg, _) = await _folderCustomizer.OptimizeFolderTemplateAsync(
                TargetCustomizeFolderPath,
                templateKey,
                ApplyTemplateToSubfolders,
                logo);

            AppendLog($"[{DateTime.Now:HH:mm:ss}] 📂 {msg}");
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task BrowseCustomIconFileAsync()
    {
        var file = await FilePickerHelper.PickSingleFileAsync([".ico", ".exe", ".dll"]);
        if (!string.IsNullOrWhiteSpace(file))
        {
            TargetCustomIconResource = $"{file},0";
            StatusMessage = $"已選擇自訂圖示來源: {TargetCustomIconResource}";
        }
    }

    [RelayCommand]
    public void SelectPopularIconPreset()
    {
        var presets = FolderAndIconCustomizerService.PopularFolderIcons;
        int idx = Math.Clamp(SelectedPopularFolderIconIndex, 0, presets.Count - 1);
        TargetCustomIconResource = presets[idx].IconResource;
    }

    [RelayCommand]
    public async Task ApplyFolderCustomIconAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetCustomizeFolderPath))
        {
            StatusMessage = "請先選擇要變更圖示的目標資料夾！";
            AudioFeedbackService.PlayWarning();
            return;
        }

        IsLoading = true;
        try
        {
            var (ok, msg) = await _folderCustomizer.SetFolderCustomIconAsync(
                TargetCustomizeFolderPath,
                TargetCustomIconResource,
                ApplyTemplateToSubfolders);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🎨 {msg}");
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task RestoreFolderDefaultIconAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetCustomizeFolderPath))
        {
            StatusMessage = "請先選擇要還原預設圖示的目標資料夾！";
            AudioFeedbackService.PlayWarning();
            return;
        }

        IsLoading = true;
        try
        {
            var (ok, msg) = await _folderCustomizer.SetFolderCustomIconAsync(
                TargetCustomizeFolderPath,
                iconResource: null,
                ApplyTemplateToSubfolders);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🔄 {msg}");
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void ApplyFileExtensionIcon()
    {
        var (ok, msg) = _folderCustomizer.SetFileExtensionCustomIcon(TargetFileExtension, TargetCustomIconResource);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 📄 {msg}");
        StatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
    }

    [RelayCommand]
    public void RestoreFileExtensionIcon()
    {
        var (ok, msg) = _folderCustomizer.SetFileExtensionCustomIcon(TargetFileExtension, iconResource: null);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🔄 {msg}");
        StatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess();
    }

    [RelayCommand]
    public void QuickRefreshShellIcons()
    {
        IconCacheRepairService.NotifyShellIconChange();
        string msg = "✅ 已呼叫 SHChangeNotify 與 ie4uinit.exe -show 即時刷新桌面與資料夾圖示 (免重啟檔案總管)！";
        IconCacheStatus = msg;
        StatusMessage = msg;
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🖼️ {msg}");
        AudioFeedbackService.PlaySuccess();
    }

    [RelayCommand]
    public void ToggleExplorerWatchdog() => SetExplorerWatchdogEnabled(!IsExplorerWatchdogEnabled);

    private bool _isApplyingWatchdogChange;

    /// <summary>
    /// Applies an explicit watchdog state. Deliberately value-driven (not a flip) and re-entrancy guarded:
    /// setting <see cref="IsExplorerWatchdogEnabled"/> updates the bound ToggleSwitch, which raises Toggled
    /// synchronously. A flip-style handler there recursed until a (silent, uncatchable) stack overflow.
    /// </summary>
    public void SetExplorerWatchdogEnabled(bool target)
    {
        if (_isApplyingWatchdogChange) return;
        _isApplyingWatchdogChange = true;
        try
        {
            ExplorerWatchdogService.Instance.IsEnabled = target;
            if (target)
            {
                ExplorerWatchdogService.Instance.Start(force: true);
            }
            else
            {
                ExplorerWatchdogService.SetWatchdogGloballyDisabled(true);
            }

            IsExplorerWatchdogEnabled = target;
            string msg = target
                ? "🛡️ 已啟用 Explorer.exe 背景自動守護服務 (當檔案總管崩潰或被強制關閉時將自動重啟恢復桌面)。"
                : "⚪ 已暫停 Explorer.exe 背景自動守護服務。";
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}");
            StatusMessage = msg;
        }
        finally
        {
            _isApplyingWatchdogChange = false;
        }
    }

    [RelayCommand]
    public async Task RestartExplorerManuallyAsync()
    {
        IsLoading = true;
        try
        {
            var (ok, msg) = await ExplorerWatchdogService.Instance.RestartExplorerManuallyAsync();
            IsExplorerCurrentlyAlive = ExplorerWatchdogService.CheckExplorerRunning();
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🔄 {msg}");
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
        }
        finally { IsLoading = false; }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Vulkan Layer Diagnostics & Zero-Overlay Mode (/boost)
    // ════════════════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task RefreshVulkanLayersAsync()
    {
        if (IsLoadingVulkanLayers) return;
        IsLoadingVulkanLayers = true;
        try
        {
            var layers = await _vulkanService.ScanAllLayersAsync();
            var pureMode = _vulkanService.IsPureVulkanModeEnabled();

            DispatcherHelper.RunOnUIThread(() =>
            {
                VulkanLayers.Clear();
                int orphaned = 0;
                foreach (var l in layers)
                {
                    VulkanLayers.Add(l);
                    if (!l.ExistsOnDisk) orphaned++;
                }

                TotalVulkanLayersCount = layers.Count;
                OrphanedVulkanLayersCount = orphaned;
                HasOrphanedVulkanLayers = orphaned > 0;
                IsPureVulkanMode = pureMode;

                VulkanStatusSummary = orphaned > 0
                    ? $"⚠️ 偵測到 {orphaned} 個遺失檔案的幽靈圖形層 (易導致遊戲啟動閃退 VK_ERROR_LAYER_NOT_PRESENT)！"
                    : $"✅ 已偵測 {layers.Count} 個圖形層，所有檔案路徑正常";
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[VULKAN ERROR] {ex.Message}");
        }
        finally
        {
            IsLoadingVulkanLayers = false;
        }
    }

    [RelayCommand]
    public async Task PurgeOrphanedVulkanLayersAsync()
    {
        IsLoadingVulkanLayers = true;
        try
        {
            var (purgedCount, paths) = await _vulkanService.PurgeOrphanedLayersAsync();
            if (purgedCount > 0)
            {
                string msg = $"🧹 成功清除 {purgedCount} 個殘留幽靈 Vulkan 圖形層登錄項目！";
                AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}");
                foreach (var p in paths) AppendLog($"   - {p}");
                StatusMessage = msg;
                AudioFeedbackService.PlaySuccess();
            }
            else
            {
                string msg = "🛡️ 系統未發現任何殘留或損毀的 Vulkan 圖形層。";
                AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}");
                StatusMessage = msg;
            }

            await RefreshVulkanLayersAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[VULKAN ERROR] {ex.Message}");
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsLoadingVulkanLayers = false;
        }
    }

    [RelayCommand]
    public async Task TogglePureVulkanModeAsync()
    {
        bool target = !IsPureVulkanMode;
        bool ok = await _vulkanService.SetPureVulkanModeAsync(target);
        if (ok)
        {
            IsPureVulkanMode = target;
            string msg = target
                ? "🚀 已啟用純淨 Vulkan 電競零延遲模式 (已設定 VK_LOADER_LAYERS_DISABLE = ~implicit~，已阻斷第三方覆蓋層干擾，提升 1% low FPS)！"
                : "⚪ 已關閉純淨 Vulkan 模式 (已恢復第三方遊戲覆蓋層正常載入)。";
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {msg}");
            StatusMessage = msg;
            AudioFeedbackService.PlaySuccess();
        }
        else
        {
            AudioFeedbackService.PlayError();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Explorer Advanced & Zero Delay Boost (/boost)
    // ════════════════════════════════════════════════════════════════════

    [RelayCommand]
    public void RefreshExplorerAdvancedState()
    {
        try
        {
            var state = _explorerAdvService.GetState();
            bool wasUpdating = IsUpdatingProgrammatically;
            IsUpdatingProgrammatically = true;
            try
            {
                ExplorerAdvState = state;
                ExplorerOptimizationScore = state.OptimizationScorePercent;
            }
            finally
            {
                IsUpdatingProgrammatically = wasUpdating;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[EXPLORER ADV ERROR] {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ApplyExplorerBoostPresetAsync()
    {
        if (IsApplyingExplorerBoost) return;
        IsApplyingExplorerBoost = true;
        try
        {
            var (ok, msg) = await _explorerAdvService.ApplyBoostAsync(iconsOnly: false, restartExplorer: true);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🚀 {msg}");
            StatusMessage = msg;
            ExplorerBoostStatus = msg;
            RefreshExplorerAdvancedState();
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        }
        finally
        {
            IsApplyingExplorerBoost = false;
        }
    }

    [RelayCommand]
    public async Task RestoreExplorerDefaultsAsync()
    {
        if (IsApplyingExplorerBoost) return;
        IsApplyingExplorerBoost = true;
        try
        {
            var (ok, msg) = await _explorerAdvService.RestoreDefaultsAsync(restartExplorer: true);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛡️ {msg}");
            StatusMessage = msg;
            ExplorerBoostStatus = msg;
            RefreshExplorerAdvancedState();
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        }
        finally
        {
            IsApplyingExplorerBoost = false;
        }
    }

    [RelayCommand]
    public async Task ToggleExplorerAdvancedSettingAsync(string settingName)
    {
        try
        {
            var state = _explorerAdvService.GetState();
            switch (settingName)
            {
                case "TaskbarAnimations":
                    await _explorerAdvService.SetSettingAsync("TaskbarAnimations", state.TaskbarAnimationsDisabled ? 1 : 0);
                    break;
                case "DesktopLivePreview":
                    await _explorerAdvService.SetSettingAsync("DesktopLivePreviewHoverTime", state.DesktopLivePreviewInstant ? 500 : 0);
                    break;
                case "DisallowShaking":
                    await _explorerAdvService.SetSettingAsync("DisallowShaking", state.DisallowShakingEnabled ? 0 : 1);
                    break;
                case "SeparateProcess":
                    await _explorerAdvService.SetSettingAsync("SeparateProcess", state.SeparateProcessEnabled ? 0 : 1);
                    break;
                case "nonetcrawling":
                    await _explorerAdvService.SetSettingAsync("nonetcrawling", state.NonetCrawlingDisabled ? 0 : 1);
                    break;
                case "IconsOnly":
                    await _explorerAdvService.SetSettingAsync("IconsOnly", state.IconsOnlyEnabled ? 0 : 1);
                    break;
                case "HideFileExt":
                    await _explorerAdvService.SetSettingAsync("HideFileExt", state.HideFileExtDisabled ? 1 : 0);
                    break;
                case "TaskbarDa":
                    await _explorerAdvService.SetSettingAsync("TaskbarDa", state.TaskbarDaDisabled ? 1 : 0);
                    break;
                case "ShowCopilotButton":
                    await _explorerAdvService.SetSettingAsync("ShowCopilotButton", state.ShowCopilotButtonDisabled ? 1 : 0);
                    break;
                case "Start_IrisRecommendations":
                    await _explorerAdvService.SetSettingAsync("Start_IrisRecommendations", state.StartIrisRecommendationsDisabled ? 1 : 0);
                    break;
                case "Start_TrackDocs":
                    await _explorerAdvService.SetSettingAsync("Start_TrackDocs", state.StartTrackDocsDisabled ? 1 : 0);
                    break;
                case "ShowSecondsInSystemClock":
                    await _explorerAdvService.SetSettingAsync("ShowSecondsInSystemClock", state.ShowSecondsInSystemClockDisabled ? 1 : 0);
                    break;
                case "QuickAccessRecent":
                    int target = state.QuickAccessRecentDisabled ? 1 : 0;
                    await _explorerAdvService.SetSettingAsync("ShowRecent", target, isExplorerRoot: true);
                    await _explorerAdvService.SetSettingAsync("ShowFrequent", target, isExplorerRoot: true);
                    break;
            }
            RefreshExplorerAdvancedState();
            AudioFeedbackService.PlaySuccess();
        }
        catch (Exception ex)
        {
            AppendLog($"[TOGGLE ERROR] {ex.Message}");
        }
    }
}
