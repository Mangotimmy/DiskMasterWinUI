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

    // ── CPU Scheduling (FPSHeaven & Presets) ──
    [ObservableProperty] private int _selectedCpuPresetIndex;
    [ObservableProperty] private bool _isPowerThrottlingDisabled;

    // ── MMCSS & Network Latency ──
    [ObservableProperty] private bool _isSystemResponsivenessZero;
    [ObservableProperty] private bool _isNetworkThrottlingDisabled;

    // ── Gaming Tweaks: GameDVR, HAGS, Latency ──
    [ObservableProperty] private bool _isGameDvrDisabled;
    [ObservableProperty] private bool _isHagsEnabled;
    [ObservableProperty] private bool _isLowInputLatencyEnabled;

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
    [ObservableProperty] private bool _isOneDriveInstalled;
    [ObservableProperty] private bool _isOneDriveRunning;
    [ObservableProperty] private bool _isOneDriveFoldersRedirected;
    [ObservableProperty] private bool _hasOneDriveCloudOnlyFiles;
    [ObservableProperty] private int _oneDriveCloudOnlyCount;
    [ObservableProperty] private bool _isOneDrivePinned;
    [ObservableProperty] private bool _isOneDrivePolicyBlocked;
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

    // ── CPU PPM Controls (EPP, Core Parking, Boost) ──
    [ObservableProperty] private int _cpuEppSliderValue = 0;
    [ObservableProperty] private int _cpuCoreParkingMinPercent = 100;
    [ObservableProperty] private int _selectedCpuBoostModeIndex = 2;

    public SystemOptimizerViewModel()
    {
        AppendLog("⚡ 系統最佳化與遊戲模式引擎已啟動 (FPSHeaven & PowerPlan Suite)");
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

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        try
        {
            IsLoading = true;
            IsUpdatingProgrammatically = true;
            var lang = LocalizationService.Instance.CurrentLanguage;
            StatusMessage = lang switch
            {
                "zh-CN" => "正在读取当前注册表与电源计划配置...",
                "en-US" => "Reading current registry and power plan configuration...",
                "ja-JP" => "現在のレジストリと電源プラン構成を読み込み中...",
                _ => "正在讀取當前登錄檔與電源計畫配置..."
            };

            // 1. Fast in-memory / registry reads
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
            IsSystemResponsivenessZero = _optimizer.GetSystemResponsiveness() == 0;
            IsNetworkThrottlingDisabled = _optimizer.GetNetworkThrottlingDisabled();
            IsGameDvrDisabled = _optimizer.GetGameDvrDisabled();
            IsHagsEnabled = _optimizer.GetHagsStatus();
            IsNagleDisabled = _optimizer.GetNagleDisabled();
            IsDisablePagingExecutive = _optimizer.GetDisablePagingExecutive();
            IsGroupPolicyTelemetryDisabled = _optimizer.GetTelemetryDisabled();

            IsGamingModeEnabled = (SelectedCpuPresetIndex == 0 || SelectedCpuPresetIndex == 1)
                                  && IsSystemResponsivenessZero
                                  && IsNetworkThrottlingDisabled
                                  && IsGameDvrDisabled;

            // 2. Read Backups (fast filesystem directory scan)
            Backups.Clear();
            var backupList = _optimizer.ListBackups();
            foreach (var b in backupList) Backups.Add(b);

            // 3. Parallel background execution for external processes
            var powerTask = Task.Run(async () =>
            {
                try
                {
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
            });

            var mmTask = Task.Run(async () =>
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
                try
                {
                    var rps = await _optimizer.ListSystemRestorePointsAsync();
                    DispatcherHelper.RunOnUIThread(() =>
                    {
                        RestorePoints.Clear();
                        foreach (var r in rps) RestorePoints.Add(r);
                    });
                }
                catch { }
            });

            var oneDriveTask = Task.Run(async () =>
            {
                try { await RefreshOneDriveStatusAsync(); } catch { }
            });

            var driversTask = Task.Run(async () =>
            {
                try { await RefreshDriversAsync(); } catch { }
            });

            await Task.WhenAll(powerTask, mmTask, wakeTask, blockersTask, restorePointsTask, oneDriveTask, driversTask);

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
        catch (Exception ex)
        {
            DispatcherHelper.RunOnUIThread(() =>
            {
                var curLang = LocalizationService.Instance.CurrentLanguage;
                var errPrefix = curLang switch
                {
                    "zh-CN" => "读取失败: ",
                    "en-US" => "Read failed: ",
                    "ja-JP" => "読み取り失敗: ",
                    _ => "讀取失敗: "
                };
                StatusMessage = $"{errPrefix}{ex.Message}";
            });
            AppendLog($"[ERROR] {ex.Message}");
        }
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
    //  1-Click Gaming Mode & Factory Defaults
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task ApplyGamingProfileAsync()
    {
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

            var log = await _optimizer.ApplyGamingProfileAsync(38); // 0x26 default
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

            var log = await _optimizer.RestoreWindowsDefaultsAsync();
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
        _optimizer.SetWin32PrioritySeparation(val);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ CPU 排程量子已套用: 0x{val:X} ({val})");
        StatusMessage = $"已設定 Win32PrioritySeparation = 0x{val:X}";
    }

    [RelayCommand]
    public async Task TogglePowerThrottlingAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_PowerThrottling");
        IsPowerThrottlingDisabled = !IsPowerThrottlingDisabled;
        _optimizer.SetPowerThrottlingDisabled(IsPowerThrottlingDisabled);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ CPU 能源節流狀態: {(IsPowerThrottlingDisabled ? "已停用 (全速無降頻)" : "已啟用 (節能)")}");
    }

    [RelayCommand]
    public async Task ToggleSystemResponsivenessAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_SystemResponsiveness");
        IsSystemResponsivenessZero = !IsSystemResponsivenessZero;
        _optimizer.SetSystemResponsiveness(IsSystemResponsivenessZero ? 0 : 20);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🚀 MMCSS 系統響應保留: {(IsSystemResponsivenessZero ? "0% (100% 前台獨享算力)" : "20% (預設保留)")}");
    }

    [RelayCommand]
    public async Task ToggleNetworkThrottlingAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_NetworkThrottling");
        IsNetworkThrottlingDisabled = !IsNetworkThrottlingDisabled;
        _optimizer.SetNetworkThrottlingDisabled(IsNetworkThrottlingDisabled);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🌐 網路封包節流佇列: {(IsNetworkThrottlingDisabled ? "已停用 (防 Ping 突增/丟包)" : "已啟用 (預設)")}");
    }

    [RelayCommand]
    public async Task ToggleGameDvrAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_GameDVR");
        IsGameDvrDisabled = !IsGameDvrDisabled;
        _optimizer.SetGameDvrDisabled(IsGameDvrDisabled);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛡️ Game DVR 背景開銷: {(IsGameDvrDisabled ? "已關閉 (釋放顯卡與CPU)" : "已啟用 (預設)")}");
    }

    [RelayCommand]
    public async Task ToggleHagsAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_HAGS");
        IsHagsEnabled = !IsHagsEnabled;
        _optimizer.SetHagsStatus(IsHagsEnabled);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🖥️ 硬體加速 GPU 排程 (HAGS): {(IsHagsEnabled ? "已開啟 (需重新開機生效)" : "已關閉")}");
    }

    [RelayCommand]
    public async Task ToggleInputLatencyAsync()
    {
        await _optimizer.CreatePreTweakBackupAsync("Before_InputLatency");
        IsLowInputLatencyEnabled = !IsLowInputLatencyEnabled;
        _optimizer.ConfigureInputLatency(IsLowInputLatencyEnabled);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 🖱️ 滑鼠 1:1 感應器追蹤與按鍵零延遲: {(IsLowInputLatencyEnabled ? "已優化" : "已還原預設")}");
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
            IsCoreUnparked = !IsCoreUnparked;
            var res = await _powerService.ConfigureCoreUnparkingAsync(IsCoreUnparked);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ CPU 核心防停泊 (Core Unparking): {(IsCoreUnparked ? "已開啟 (隨時全核待命)" : "已關閉")}");
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    [RelayCommand]
    public async Task ToggleMinProcessorStateAsync()
    {
        try
        {
            IsMinProcessorState100 = !IsMinProcessorState100;
            var res = await _powerService.ConfigureMinProcessorStateAsync(IsMinProcessorState100 ? 100 : 5);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ⚡ 插電時最低處理器頻率: {(IsMinProcessorState100 ? "鎖定 100% (防轉場掉頻)" : "預設 5%")}");
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
            StatusMessage = "登錄檔備份還原完成！";
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void DeleteBackup(RegistryBackupItem? item)
    {
        if (item == null) return;
        _optimizer.DeleteBackup(item.FilePath);
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
            IsLoadingWakeDevices = true;
            WakeDevices.Clear();
            var devices = await _powerCfg.GetWakeDevicesAsync();
            foreach (var d in devices) WakeDevices.Add(d);
        }
        catch { }
        finally { IsLoadingWakeDevices = false; }
    }

    [RelayCommand]
    public async Task RefreshSleepBlockersAsync()
    {
        try
        {
            IsLoadingSleepBlockers = true;
            SleepBlockers.Clear();
            var blockers = await _powerCfg.GetSleepBlockersAsync();
            foreach (var b in blockers) SleepBlockers.Add(b);
            HasSleepBlockers = SleepBlockers.Count > 0;
        }
        catch { }
        finally { IsLoadingSleepBlockers = false; }
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
            IsLoadingDrivers = true;
            var drivers = await _driverService.EnumDriversStructuredAsync();
            DispatcherHelper.RunOnUIThread(() =>
            {
                OemDrivers.Clear();
                foreach (var d in drivers) OemDrivers.Add(d);
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] 列舉驅動失敗: {ex.Message}");
        }
        finally
        {
            IsLoadingDrivers = false;
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
            IsOneDrivePolicyBlocked = newTarget;
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛡️ {msg}");
            if (success) AudioFeedbackService.PlaySuccess();
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
            IsNagleDisabled = !IsNagleDisabled;
            _optimizer.ConfigureNagleAlgorithm(IsNagleDisabled);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🌐 Nagle's Algorithm (TcpAckFrequency / TCPNoDelay): {(IsNagleDisabled ? "已停用 (超低延遲模式)" : "已啟用 (標準模式)")}");
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    [RelayCommand]
    public void ToggleDisablePagingExecutive()
    {
        try
        {
            IsDisablePagingExecutive = !IsDisablePagingExecutive;
            _optimizer.ConfigureMemoryManagement(IsDisablePagingExecutive);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🧠 核心常駐實體 RAM (DisablePagingExecutive): {(IsDisablePagingExecutive ? "已開啟 (常駐記憶體無分頁延遲)" : "已關閉")}");
        }
        catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}"); }
    }

    [RelayCommand]
    public void ToggleGroupPolicyTelemetry()
    {
        try
        {
            IsGroupPolicyTelemetryDisabled = !IsGroupPolicyTelemetryDisabled;
            _optimizer.ConfigureGroupPolicyPrivacy(IsGroupPolicyTelemetryDisabled);
            AppendLog($"[{DateTime.Now:HH:mm:ss}] 🛡️ 本機群組原則隱私與遙測防護: {(IsGroupPolicyTelemetryDisabled ? "已套用極致隱私原則 (關閉遙測/廣告/P2P上傳)" : "已還原預設")}");
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
}
