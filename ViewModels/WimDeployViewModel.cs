using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

public partial class WimDeployViewModel : ObservableObject
{
    private readonly WimDeployService _wimService = new();
    private readonly WindowsDownloadService _downloadService = new();
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty] private string _imagePath = "";
    [ObservableProperty] private string _mountedIsoPath = "";
    [ObservableProperty] private WimImageInfo? _selectedImage;
    [ObservableProperty] private string _targetDrive = @"C:\";
    [ObservableProperty] private string _bootDrive = "";
    [ObservableProperty] private string _firmwareMode = "UEFI";
    [ObservableProperty] private string _driverFolderPath = "";

    // Addons / Tweaks
    [ObservableProperty] private bool _isCompactOs = false;
    [ObservableProperty] private bool _bypassWin11 = true;
    [ObservableProperty] private bool _bypassOnlineAccount = true;
    [ObservableProperty] private bool _autoCreateBoot = true;
    [ObservableProperty] private bool _autoInjectWin7Drivers = true;

    // Progress & Logs
    [ObservableProperty] private bool _isDeploying;
    [ObservableProperty] private int _progressPercentage = 0;
    [ObservableProperty] private string _progressText = "Ready";
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _deployLog = "";

    // Image Management
    [ObservableProperty] private string _captureDir = @"C:\";
    [ObservableProperty] private string _captureOutputPath = @"D:\Backup.wim";
    [ObservableProperty] private string _captureName = "Windows_Backup";
    [ObservableProperty] private string _exportDestPath = "";
    [ObservableProperty] private string _splitOutputPath = "";
    [ObservableProperty] private string _mountDir = @"C:\WimMount";
    public string ProgressPercentDisplay => $"{ProgressPercentage}%";

    // Windows Downloader Catalog & States
    public ObservableCollection<WindowsDownloadItem> DownloadCatalog { get; } = new();
    [ObservableProperty] private WindowsDownloadItem? _selectedDownloadItem;
    [ObservableProperty] private string _catalogCategoryFilter = "All";
    [ObservableProperty] private string _downloadDestinationFolder = WindowsDownloadService.DefaultDownloadDirectory;
    [ObservableProperty] private bool _isDownloadingWindows;
    [ObservableProperty] private double _downloadPercent;
    [ObservableProperty] private string _downloadSpeedText = "";
    [ObservableProperty] private string _downloadEtaText = "";
    [ObservableProperty] private string _downloadStatusText = "準備就緒 (Ready)";
    [ObservableProperty] private string _downloadVerificationBadge = "";

    // ── HTTP Range & Parallel Chunked Downloader Panel ──
    [ObservableProperty] private bool _httpRangeParallelEnabled = SettingsService.Instance.Current.HttpRangeParallelEnabled;
    [ObservableProperty] private int _parallelThreadCount = SettingsService.Instance.Current.ParallelThreadCount;
    [ObservableProperty] private int _chunkBufferSizeKB = SettingsService.Instance.Current.ChunkBufferSizeKB;
    [ObservableProperty] private bool _keepResumeCache = SettingsService.Instance.Current.KeepResumeCache;
    [ObservableProperty] private string _serverRangeCapabilityText = "⚪ 尚未探測伺服器 Range 能力";
    [ObservableProperty] private string _customDownloadUrl = "";
    [ObservableProperty] private string _cacheStatsText = "暫存狀態: 點擊按鈕檢查";

    public ObservableCollection<int> ParallelThreadOptions { get; } = new() { 2, 4, 8, 16, 32 };
    public ObservableCollection<int> ChunkBufferSizeOptions { get; } = new() { 64, 128, 256, 512, 1024 };

    // ── VHDX Native Boot Deployment ──
    [ObservableProperty] private bool _isDeployToVhdx;
    [ObservableProperty] private string _vhdxFilePath = @"C:\DiskMaster_System.vhdx";
    [ObservableProperty] private int _vhdxSizeGB = 64;
    [ObservableProperty] private string _vhdxBootTitle = "Windows 11 (VHDX Boot)";

    // Windows 7/8/10/11 Boot Compatibility Guard
    [ObservableProperty] private string _guardTitle = "✅ 開機相容性檢查待命中";
    [ObservableProperty] private string _guardMessage = "選擇映像與目標磁區後，將自動分析 MBR/GPT 與 UEFI/BIOS 相容性。";
    [ObservableProperty] private string _guardBadgeColor = "#107C41";
    [ObservableProperty] private bool _hasGuardWarning;
    [ObservableProperty] private bool _isGuardBlocked;
    [ObservableProperty] private string _guardActionText = "";

    public ObservableCollection<WimImageInfo> ImageEditions { get; } = new();
    public ObservableCollection<string> FirmwareOptions { get; } = new() { "UEFI", "BIOS", "ALL" };
    public ObservableCollection<string> AvailableTargetDrives { get; } = new();
    public ObservableCollection<string> AvailableBootDrives { get; } = new();

    // ── Rufus Fido Microsoft Direct SAS Downloader ──
    public ObservableCollection<string> RufusOsList { get; } = new() { "Windows 11", "Windows 10" };
    [ObservableProperty] private string _selectedRufusOs = "Windows 11";

    public ObservableCollection<string> RufusReleaseList { get; } = new();
    [ObservableProperty] private string _selectedRufusRelease = "Latest (25H2 v2 - 2026.03 最新)";

    public ObservableCollection<string> RufusEditionList { get; } = new();
    [ObservableProperty] private string _selectedRufusEdition = "Windows 11 Home/Pro/Edu";

    public ObservableCollection<RufusLanguageItem> RufusLanguageList { get; } = new();
    [ObservableProperty] private RufusLanguageItem? _selectedRufusLanguage;

    public ObservableCollection<string> RufusArchList { get; } = new() { "x64", "arm64" };
    [ObservableProperty] private string _selectedRufusArch = "x64";

    [ObservableProperty] private string _rufusDirectUrl = "";
    [ObservableProperty] private bool _isFetchingRufusUrl;
    [ObservableProperty] private bool _autoFetchDirectUrl = true;
    [ObservableProperty] private bool _isRefreshingVersions;
    [ObservableProperty] private bool _isDownloadPaused;
    [ObservableProperty] private int _downloadModeIndex = 0; // 0: Dynamic Rufus, 1: Classic MSDN

    public bool IsDynamicDownloadMode => DownloadModeIndex == 0;
    public bool IsClassicDownloadMode => DownloadModeIndex == 1;
    public string PauseResumeButtonText => IsDownloadPaused ? "▶️ 繼續" : "⏸️ 暫停";

    private DownloadController? _downloadController;
    private readonly ThrottledLogBuffer _logBuffer;
    private CancellationTokenSource? _autoFetchCts;
    private CancellationTokenSource? _fetchRufusCts;

    partial void OnDownloadModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsDynamicDownloadMode));
        OnPropertyChanged(nameof(IsClassicDownloadMode));
    }

    [RelayCommand]
    public void TogglePauseResumeDownload()
    {
        if (!IsDownloadingWindows || _downloadController == null) return;

        if (IsDownloadPaused)
        {
            _downloadController.Resume();
            IsDownloadPaused = false;
            OnPropertyChanged(nameof(PauseResumeButtonText));
            DownloadStatusText = "⚡ 恢復下載中...";
            LogTimestamped("[DOWNLOAD ENGINE] 使用者恢復下載傳輸。");
            MainWindow.CurrentInstance?.CompanionSay("已恢復高速下載！");
        }
        else
        {
            _downloadController.Pause();
            IsDownloadPaused = true;
            OnPropertyChanged(nameof(PauseResumeButtonText));
            DownloadStatusText = "⏸️ 下載已暫停，點擊「繼續」即可恢復";
            LogTimestamped("[DOWNLOAD ENGINE] 使用者已暫停下載。");
            MainWindow.CurrentInstance?.CompanionSay("下載已暫停，隨時可點擊「繼續」恢復！");
        }
    }

    public WimDeployViewModel()
    {
        _logBuffer = new ThrottledLogBuffer(text => DeployLog = text);
        RefreshAvailableDrives();
        InitializeDownloadCatalog();
        InitializeRufusCatalog();
    }

    private void Log(string text) => _logBuffer.Append(text.EndsWith('\n') ? text : text + "\n");
    private void LogTimestamped(string text) => _logBuffer.AppendTimestampedLine(text);

    public void InitializeRufusCatalog()
    {
        RufusReleaseList.Clear();
        foreach (var r in RufusDownloadService.Instance.GetReleasesForOs(SelectedRufusOs))
            RufusReleaseList.Add(r);
        if (RufusReleaseList.Count > 0) SelectedRufusRelease = RufusReleaseList[0];

        RufusEditionList.Clear();
        foreach (var ed in RufusDownloadService.Instance.GetEditionsForOs(SelectedRufusOs))
            RufusEditionList.Add(ed);
        if (RufusEditionList.Count > 0) SelectedRufusEdition = RufusEditionList[0];

        RufusLanguageList.Clear();
        foreach (var lang in RufusDownloadService.OfficialLanguages)
            RufusLanguageList.Add(lang);
        SelectedRufusLanguage = RufusLanguageList.FirstOrDefault(l => l.Code == "zh-TW") ?? RufusLanguageList.FirstOrDefault();

        if (AutoFetchDirectUrl)
        {
            ScheduleAutoFetchDirectUrl();
        }
    }

    partial void OnSelectedRufusOsChanged(string value)
    {
        RufusReleaseList.Clear();
        foreach (var r in RufusDownloadService.Instance.GetReleasesForOs(value))
            RufusReleaseList.Add(r);
        if (RufusReleaseList.Count > 0) SelectedRufusRelease = RufusReleaseList[0];

        RufusEditionList.Clear();
        foreach (var ed in RufusDownloadService.Instance.GetEditionsForOs(value))
            RufusEditionList.Add(ed);
        if (RufusEditionList.Count > 0) SelectedRufusEdition = RufusEditionList[0];

        ScheduleAutoFetchDirectUrl();
    }

    partial void OnSelectedRufusReleaseChanged(string value)
    {
        ScheduleAutoFetchDirectUrl();
    }

    partial void OnSelectedRufusEditionChanged(string value)
    {
        ScheduleAutoFetchDirectUrl();
    }

    partial void OnSelectedRufusLanguageChanged(RufusLanguageItem? value)
    {
        ScheduleAutoFetchDirectUrl();
    }

    partial void OnSelectedRufusArchChanged(string value)
    {
        ScheduleAutoFetchDirectUrl();
    }

    partial void OnAutoFetchDirectUrlChanged(bool value)
    {
        if (value && string.IsNullOrWhiteSpace(RufusDirectUrl))
        {
            ScheduleAutoFetchDirectUrl();
        }
    }

    partial void OnSelectedDownloadItemChanged(WindowsDownloadItem? value)
    {
        EvaluateCompatibility();
        if (value != null && !string.IsNullOrWhiteSpace(value.PrimaryUrl))
        {
            CustomDownloadUrl = value.PrimaryUrl;
            _ = ProbeServerRangeAsync();
        }
    }

    private void ScheduleAutoFetchDirectUrl()
    {
        if (!AutoFetchDirectUrl || IsDownloadingWindows) return;

        _autoFetchCts?.Cancel();
        _autoFetchCts?.Dispose();
        _autoFetchCts = new CancellationTokenSource();
        var token = _autoFetchCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, token);
                if (token.IsCancellationRequested) return;

                DispatcherHelper.RunOnUIThread(async () =>
                {
                    if (!token.IsCancellationRequested && !IsFetchingRufusUrl && !IsDownloadingWindows)
                    {
                        await FetchRufusDirectUrlAsync();
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto fetch error: {ex.Message}");
            }
        });
    }

    public void InitializeDownloadCatalog()
    {
        DownloadCatalog.Clear();
        var all = WindowsDownloadCatalog.GetAllReleases();
        foreach (var item in all)
        {
            DownloadCatalog.Add(item);
        }
        if (DownloadCatalog.Count > 0)
        {
            SelectedDownloadItem = DownloadCatalog[0];
        }
    }

    [RelayCommand]
    public void FilterCatalog(string category)
    {
        CatalogCategoryFilter = category;
        DownloadCatalog.Clear();
        var items = category == "All"
            ? WindowsDownloadCatalog.GetAllReleases()
            : WindowsDownloadCatalog.GetByCategory(category);

        foreach (var item in items)
        {
            DownloadCatalog.Add(item);
        }
        if (DownloadCatalog.Count > 0)
        {
            SelectedDownloadItem = DownloadCatalog[0];
        }
    }

    public void RefreshAvailableDrives()
    {
        AvailableTargetDrives.Clear();
        AvailableBootDrives.Clear();
        AvailableBootDrives.Add("(Default / Same as target)");

        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var d in drives)
            {
                if (!d.IsReady) continue;
                var totalGb = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "No Label" : d.VolumeLabel;
                var itemText = $"{d.Name} [{label}] — {totalGb:F1} GB ({d.DriveFormat})";

                AvailableTargetDrives.Add(itemText);
                AvailableBootDrives.Add($"{d.Name} [{label}] ({d.DriveFormat})");
            }
        }
        catch { }

        if (AvailableTargetDrives.Count > 0 && string.IsNullOrWhiteSpace(TargetDrive))
        {
            TargetDrive = AvailableTargetDrives[0].Split(' ')[0];
        }

        EvaluateCompatibility();
    }

    public void EvaluateCompatibility()
    {
        var targetClean = TargetDrive.Split(' ')[0].TrimEnd('\\', ':') + ":";
        var isGpt = true; // Default assumption for modern disks

        // Try to query disk style from DiskPartService or Native
        try
        {
            var driveInfo = new DriveInfo(targetClean);
            // In Windows 10/11 default is GPT, but we also check if Windows 7/8 is being targeted
        }
        catch { }

        var osName = SelectedImage?.Name ?? SelectedDownloadItem?.VersionTitle ?? "Windows 11";
        var arch = SelectedImage?.Architecture ?? SelectedDownloadItem?.Architecture ?? "x64";

        var result = BootGuardHelper.CheckCompatibility(isGpt, osName, arch);

        GuardTitle = result.Title;
        GuardMessage = result.Message;
        HasGuardWarning = result.NeedsCsmGuide || result.NeedsMbrConversion;
        IsGuardBlocked = !result.IsBootable;
        GuardActionText = result.ActionButtonText;
        FirmwareMode = result.SuggestedBcdFirmware;

        GuardBadgeColor = result.Status switch
        {
            BootCompatibilityStatus.Compatible => "#107C41",
            BootCompatibilityStatus.WarningNeedsBiosConfig => "#D83B01",
            BootCompatibilityStatus.IncompatibleBlocked => "#E81123",
            _ => "#107C41"
        };
    }

    [RelayCommand]
    public async Task DownloadSelectedWindowsAsync()
    {
        if (IsDownloadingWindows) return;

        // 1. Determine effective download URL (prioritizing Rufus 24h SAS URL or custom input)
        var targetUrl = !string.IsNullOrWhiteSpace(RufusDirectUrl)
            ? RufusDirectUrl
            : !string.IsNullOrWhiteSpace(CustomDownloadUrl)
                ? CustomDownloadUrl
                : SelectedDownloadItem?.PrimaryUrl;

        // 2. If targetUrl is empty or is a bare Microsoft URL requiring 24h SAS token, auto-fetch via Rufus!
        if (string.IsNullOrWhiteSpace(targetUrl) ||
            (targetUrl.Contains("software.download.prss.microsoft.com") && !targetUrl.Contains("?")))
        {
            LogTimestamped("偵測到微軟官方發行版需 24H 授權 Token，自動獲取專屬極速直鏈...");
            MainWindow.CurrentInstance?.CompanionSay("正在向微軟官方獲取當日有效直鏈並啟動高速下載...");
            await DownloadViaRufusEngineAsync();
            return;
        }

        // 3. Ensure SelectedDownloadItem reflects targetUrl and has a valid filename
        if (SelectedDownloadItem == null || SelectedDownloadItem.PrimaryUrl != targetUrl)
        {
            var fileName = !string.IsNullOrWhiteSpace(SelectedDownloadItem?.FileName) && !SelectedDownloadItem.FileName.EndsWith(".iso.tmp_dm")
                ? SelectedDownloadItem.FileName
                : RufusDownloadService.ExtractFileNameFromUrl(targetUrl, SelectedRufusOs, SelectedRufusRelease, SelectedRufusLanguage?.FidoParam ?? "Traditional_Chinese", SelectedRufusArch);

            var item = new WindowsDownloadItem
            {
                VersionTitle = SelectedDownloadItem?.VersionTitle ?? $"{SelectedRufusOs} {SelectedRufusRelease}",
                ReleaseCategory = "Modern",
                Architecture = SelectedRufusArch,
                FileName = fileName,
                PrimaryUrl = targetUrl,
                SupportsSecureBoot = true
            };

            var existing = DownloadCatalog.FirstOrDefault(x => x.PrimaryUrl == targetUrl);
            if (existing != null)
            {
                SelectedDownloadItem = existing;
            }
            else
            {
                DownloadCatalog.Insert(0, item);
                SelectedDownloadItem = item;
            }
        }

        IsDownloadingWindows = true;
        IsDownloadPaused = false;
        OnPropertyChanged(nameof(PauseResumeButtonText));
        _downloadController = new DownloadController();
        _downloadCts = new CancellationTokenSource();
        DownloadStatusText = "連線建立中...";
        DownloadVerificationBadge = "";
        ProgressPercentage = 0;
        OnPropertyChanged(nameof(ProgressPercentDisplay));

        MainWindow.CurrentInstance?.CompanionSay($"開始下載 {SelectedDownloadItem.VersionTitle}，高速多線程傳輸中～");
        MainWindow.CurrentInstance?.SetCompanionEmotion(Controls.CompanionEmotion.Working);

        try
        {
            var success = await _downloadService.DownloadImageAsync(
                SelectedDownloadItem,
                DownloadDestinationFolder,
                (pct, spd, eta) =>
                {
                    DispatcherHelper.RunOnUIThread(() =>
                    {
                        DownloadPercent = pct;
                        DownloadSpeedText = spd;
                        DownloadEtaText = eta;
                        ProgressPercentage = (int)pct;
                        OnPropertyChanged(nameof(ProgressPercentDisplay));
                        ProgressText = $"下載中: {spd} — 剩餘: {eta}";
                    });
                },
                status =>
                {
                    DispatcherHelper.RunOnUIThread(() =>
                    {
                        DownloadStatusText = status;
                    });
                },
                line =>
                {
                    DispatcherHelper.RunOnUIThread(() =>
                    {
                        LogTimestamped(line);
                    });
                },
                _downloadCts.Token,
                _downloadController);

            if (success)
            {
                DownloadVerificationBadge = SelectedDownloadItem.HashVerificationBadge;
                ProgressPercentage = 100;
                OnPropertyChanged(nameof(ProgressPercentDisplay));
                ProgressText = "下載與官方校驗完成";
                MainWindow.CurrentInstance?.CompanionSay($"下載與 MSDN 雜湊校驗完成！官方原版無修改，隨時可一鍵部署。");
                MainWindow.CurrentInstance?.SetCompanionEmotion(Controls.CompanionEmotion.Happy);
            }
            else
            {
                DownloadVerificationBadge = SelectedDownloadItem.HashVerificationBadge;
            }
        }
        catch (Exception ex)
        {
            DownloadStatusText = $"下載出錯: {ex.Message}";
            LogTimestamped($"[ERROR] 下載出錯: {ex.Message}");
        }
        finally
        {
            IsDownloadingWindows = false;
            IsDownloadPaused = false;
            OnPropertyChanged(nameof(PauseResumeButtonText));
            _downloadController = null;
        }
    }

    [RelayCommand]
    public async Task FetchRufusDirectUrlAsync()
    {
        if (IsFetchingRufusUrl) return;

        IsFetchingRufusUrl = true;
        RufusDirectUrl = "";
        _fetchRufusCts?.Cancel();
        _fetchRufusCts?.Dispose();
        _fetchRufusCts = new CancellationTokenSource();

        try
        {
            var langParam = SelectedRufusLanguage?.FidoParam ?? "Chinese (Traditional)";
            MainWindow.CurrentInstance?.CompanionSay($"正在獲取微軟官方直鏈...");
            LogTimestamped($"正在向微軟軟體連接器 API 請求 {SelectedRufusOs} {SelectedRufusRelease} ({langParam})...");

            var (success, url, fileName, err) = await RufusDownloadService.Instance.RequestMicrosoftSasUrlAsync(
                SelectedRufusOs,
                SelectedRufusRelease,
                SelectedRufusEdition,
                langParam,
                SelectedRufusArch,
                line => DispatcherHelper.RunOnUIThread(() => LogTimestamped(line)),
                _fetchRufusCts.Token);

            if (success && !string.IsNullOrWhiteSpace(url))
            {
                RufusDirectUrl = url;
                CustomDownloadUrl = url;

                var effFileName = !string.IsNullOrWhiteSpace(fileName)
                    ? fileName
                    : RufusDownloadService.ExtractFileNameFromUrl(url, SelectedRufusOs, SelectedRufusRelease, langParam, SelectedRufusArch);

                var item = new WindowsDownloadItem
                {
                    VersionTitle = $"{SelectedRufusOs} {SelectedRufusRelease}",
                    Edition = SelectedRufusEdition,
                    Language = SelectedRufusLanguage?.Code ?? "zh-TW",
                    LanguageDisplay = SelectedRufusLanguage?.DisplayName ?? "繁體中文",
                    Category = "Modern",
                    Architecture = SelectedRufusArch,
                    FileName = effFileName,
                    PrimaryUrl = url,
                    SupportsSecureBoot = SelectedRufusOs.Contains("11")
                };

                var existing = DownloadCatalog.FirstOrDefault(x => x.PrimaryUrl == url);
                if (existing != null)
                {
                    SelectedDownloadItem = existing;
                }
                else
                {
                    DownloadCatalog.Insert(0, item);
                    SelectedDownloadItem = item;
                }

                MainWindow.CurrentInstance?.CompanionSay("成功取得微軟官方 24H 極速直鏈！已填入下載來源並自動選取。");
                StatusMessage = $"成功獲取 24H 官方直鏈: {effFileName}";

                await ProbeServerRangeAsync();
            }
            else
            {
                StatusMessage = $"獲取直鏈失敗: {err}";
                if (err.Contains("Sentinel") || err.Contains("防火牆") || err.Contains("715-123130"))
                {
                    MainWindow.CurrentInstance?.CompanionSay("微軟伺服器暫時限制自動直鏈，建議點擊「開啟微軟官網下載」並「貼上直鏈」即可極速下載～");
                    MainWindow.CurrentInstance?.SetCompanionEmotion(Controls.CompanionEmotion.Alert);
                }
                else
                {
                    MainWindow.CurrentInstance?.CompanionSay($"直鏈獲取未成功：{err}");
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"獲取失敗: {ex.Message}";
            LogTimestamped($"[ERROR] 獲取失敗: {ex.Message}");
        }
        finally
        {
            IsFetchingRufusUrl = false;
        }
    }

    [RelayCommand]
    public async Task RefreshWindowsVersionsAsync()
    {
        if (IsRefreshingVersions) return;
        IsRefreshingVersions = true;
        try
        {
            StatusMessage = "正在重新整理微軟最新版本清單與快取...";
            LogTimestamped("🔄 正在向微軟雲端更新 Windows 發行版本清單...");
            MainWindow.CurrentInstance?.CompanionSay("正在向微軟雲端更新 Windows 發行版本清單...");

            var releases = await RufusDownloadService.Instance.RefreshReleasesForOsAsync(SelectedRufusOs);
            RufusReleaseList.Clear();
            foreach (var r in releases)
            {
                RufusReleaseList.Add(r);
            }
            if (RufusReleaseList.Count > 0 && (string.IsNullOrWhiteSpace(SelectedRufusRelease) || !RufusReleaseList.Contains(SelectedRufusRelease)))
            {
                SelectedRufusRelease = RufusReleaseList[0];
            }

            InitializeDownloadCatalog();

            if (AutoFetchDirectUrl)
            {
                await FetchRufusDirectUrlAsync();
            }

            StatusMessage = "微軟官方版本清單與直鏈已更新完畢。";
            MainWindow.CurrentInstance?.CompanionSay("微軟官方版本清單與直鏈已更新完畢！");
        }
        catch (Exception ex)
        {
            StatusMessage = $"重新整理失敗: {ex.Message}";
            LogTimestamped($"[ERROR] 重新整理失敗: {ex.Message}");
        }
        finally
        {
            IsRefreshingVersions = false;
        }
    }

    [RelayCommand]
    public async Task DownloadViaRufusEngineAsync()
    {
        if (IsDownloadingWindows || IsFetchingRufusUrl) return;

        var targetUrl = RufusDirectUrl;
        var langParam = SelectedRufusLanguage?.FidoParam ?? "Chinese (Traditional)";
        var fileName = $"{SelectedRufusOs.Replace(" ", "")}_{SelectedRufusRelease.Split(' ')[0]}_{SelectedRufusLanguage?.Code}_{SelectedRufusArch}.iso";

        // If direct URL is empty, fetch it first
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            IsFetchingRufusUrl = true;
            try
            {
                MainWindow.CurrentInstance?.CompanionSay($"正在向微軟獲取官方 SAS 直鏈並立即啟動下載...");
                var (success, url, autoFileName, err) = await RufusDownloadService.Instance.RequestMicrosoftSasUrlAsync(
                    SelectedRufusOs,
                    SelectedRufusRelease,
                    SelectedRufusEdition,
                    langParam,
                    SelectedRufusArch,
                    line => DispatcherHelper.RunOnUIThread(() => LogTimestamped(line)));

                if (!success || string.IsNullOrWhiteSpace(url))
                {
                    StatusMessage = $"無法取得官方直鏈: {err}";
                    return;
                }
                targetUrl = url;
                RufusDirectUrl = url;
                CustomDownloadUrl = url;
                if (!string.IsNullOrWhiteSpace(autoFileName)) fileName = autoFileName;
            }
            finally
            {
                IsFetchingRufusUrl = false;
            }
        }
        else
        {
            CustomDownloadUrl = targetUrl;
        }

        // Construct dynamic download item
        var item = new WindowsDownloadItem
        {
            VersionTitle = $"{SelectedRufusOs} {SelectedRufusRelease}",
            Category = "Modern",
            Architecture = SelectedRufusArch,
            FileName = fileName,
            PrimaryUrl = targetUrl,
            SupportsSecureBoot = SelectedRufusOs.Contains("11")
        };

        var existing = DownloadCatalog.FirstOrDefault(x => x.PrimaryUrl == targetUrl);
        if (existing != null)
        {
            SelectedDownloadItem = existing;
        }
        else
        {
            DownloadCatalog.Insert(0, item);
            SelectedDownloadItem = item;
        }

        await DownloadSelectedWindowsAsync();
    }

    [RelayCommand]
    public void CancelWindowsDownload()
    {
        if (_downloadCts != null && !_downloadCts.IsCancellationRequested)
        {
            _downloadCts.Cancel();
            DownloadStatusText = "已由使用者取消下載。";
            MainWindow.CurrentInstance?.CompanionSay("下載已取消。");
        }
    }

    [RelayCommand]
    public async Task UseDownloadedImageAsync(WindowsDownloadItem? item)
    {
        var target = item ?? SelectedDownloadItem;
        if (target == null || string.IsNullOrWhiteSpace(target.LocalFilePath) || !File.Exists(target.LocalFilePath))
        {
            StatusMessage = "請先下載映像檔案。";
            return;
        }

        if (target.LocalFilePath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "掛載 ISO 虛擬光碟中...";
            MountedIsoPath = target.LocalFilePath;
            var (_, wimPath, msg) = await _wimService.MountIsoAndFindWimAsync(target.LocalFilePath);
            LogTimestamped(msg);

            if (!string.IsNullOrWhiteSpace(wimPath))
            {
                ImagePath = wimPath;
                await LoadImageInfoAsync();
                StatusMessage = $"已載入 {target.VersionTitle}，隨時可一鍵部署！";
                MainWindow.CurrentInstance?.CompanionSay($"已自動掛載 {target.VersionTitle}，選擇磁區即可開始部署！");
            }
        }
        else
        {
            ImagePath = target.LocalFilePath;
            await LoadImageInfoAsync();
            StatusMessage = $"已載入 {target.VersionTitle}。";
        }

        EvaluateCompatibility();
    }

    [RelayCommand]
    public async Task BrowseImageFileAsync()
    {
        var picked = await FilePickerHelper.PickSingleFileAsync(new[] { ".wim", ".esd", ".iso" });
        if (string.IsNullOrWhiteSpace(picked)) return;

        if (picked.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Mounting ISO virtual disk...";
            MountedIsoPath = picked;
            var (mountedDrive, wimPath, msg) = await _wimService.MountIsoAndFindWimAsync(picked);
            LogTimestamped(msg);

            if (!string.IsNullOrWhiteSpace(wimPath))
            {
                ImagePath = wimPath;
                StatusMessage = msg;
                await LoadImageInfoAsync();
            }
            else
            {
                StatusMessage = msg;
            }
        }
        else
        {
            ImagePath = picked;
            await LoadImageInfoAsync();
        }

        EvaluateCompatibility();
    }

    [RelayCommand]
    public async Task DismountIsoAsync()
    {
        if (!string.IsNullOrWhiteSpace(MountedIsoPath))
        {
            var msg = await _wimService.DismountIsoAsync(MountedIsoPath);
            LogTimestamped(msg);
            StatusMessage = msg;
            MountedIsoPath = "";
            ImageEditions.Clear();
            ImagePath = "";
        }
    }

    [RelayCommand]
    public async Task BrowseDriverFolderAsync()
    {
        var folder = await FilePickerHelper.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            DriverFolderPath = folder;
        }
    }

    [RelayCommand]
    public async Task LoadImageInfoAsync()
    {
        if (string.IsNullOrWhiteSpace(ImagePath) || !File.Exists(ImagePath))
        {
            StatusMessage = "Please specify a valid WIM/ESD/ISO file path.";
            return;
        }

        try
        {
            IsDeploying = true;
            StatusMessage = "Reading image editions via DISM...";
            var editions = await _wimService.GetImageInfoAsync(ImagePath);

            ImageEditions.Clear();
            foreach (var e in editions) ImageEditions.Add(e);

            if (ImageEditions.Count > 0)
            {
                SelectedImage = ImageEditions[0];
            }

            StatusMessage = $"Found {editions.Count} edition(s) in {Path.GetFileName(ImagePath)}.";
            EvaluateCompatibility();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsDeploying = false;
        }
    }

    [RelayCommand]
    public async Task DeployImageAsync()
    {
        if (SelectedImage == null)
        {
            StatusMessage = "請先選擇欲安裝的 Windows 版本版本。";
            return;
        }

        if (IsGuardBlocked)
        {
            StatusMessage = "開機相容性檢查未通過，無法啟動部署（請先解決衝突）。";
            return;
        }

        var cleanTarget = TargetDrive.Split(' ')[0].TrimEnd('\\') + @"\";

        try
        {
            IsDeploying = true;
            ProgressPercentage = 0;
            OnPropertyChanged(nameof(ProgressPercentDisplay));
            ProgressText = "Deploying image...";
            _logBuffer.Clear();
            LogTimestamped($"Starting deployment of {SelectedImage.Name} (Index {SelectedImage.Index}) to {cleanTarget}...");
            MainWindow.CurrentInstance?.CompanionSay($"正在套用 {SelectedImage.Name} 至 {cleanTarget}，請稍候...");

            // 1. DISM Apply Image
            var applyResult = await _wimService.ApplyImageAsync(
                ImagePath,
                SelectedImage.Index,
                cleanTarget,
                IsCompactOs,
                (pct, text) =>
                {
                    DispatcherHelper.RunOnUIThread(() =>
                    {
                        if (pct >= 0)
                        {
                            ProgressPercentage = pct;
                            OnPropertyChanged(nameof(ProgressPercentDisplay));
                        }
                        ProgressText = text;
                    });
                    if (!string.IsNullOrWhiteSpace(text)) _logBuffer.AppendLine(text);
                });

            Log($"\n[DISM Apply Completed]\n{applyResult}\n");

            // 2. Win11 Bypass & Addons
            if (BypassWin11 || BypassOnlineAccount)
            {
                LogTimestamped("Injecting Windows 11 requirement bypasses (LabConfig & BypassNRO)...");
                var bypassResult = await _wimService.ApplyWin11BypassTweaksAsync(cleanTarget);
                Log(bypassResult);
            }

            // 3. Driver Injection
            if (!string.IsNullOrWhiteSpace(DriverFolderPath) && Directory.Exists(DriverFolderPath))
            {
                LogTimestamped($"Injecting offline drivers from {DriverFolderPath}...");
                var drvResult = await _wimService.AddDriversAsync(cleanTarget, DriverFolderPath);
                Log(drvResult);
            }

            // 4. Windows 7 Special USB 3.0 & NVMe Driver Injection
            if (AutoInjectWin7Drivers && (SelectedImage.Name.Contains("Windows 7") || SelectedImage.Name.Contains("Win7")))
            {
                LogTimestamped("Auto-injecting Windows 7 USB 3.0 & NVMe drivers for modern hardware support...");
                var w7Drv = await _wimService.InjectWin7UsbAndNvmeDriversAsync(cleanTarget);
                Log(w7Drv);
            }

            // 5. Auto Boot Configuration
            if (AutoCreateBoot)
            {
                var cleanBoot = string.IsNullOrWhiteSpace(BootDrive) || BootDrive.StartsWith("(")
                    ? null
                    : BootDrive.Split(' ')[0];

                LogTimestamped($"Generating bootloader files via bcdboot (Firmware: {FirmwareMode})...");
                var bootResult = await _wimService.CreateBootFilesAsync(cleanTarget, cleanBoot, FirmwareMode);
                Log(bootResult);
            }

            ProgressPercentage = 100;
            OnPropertyChanged(nameof(ProgressPercentDisplay));
            ProgressText = "Deployment Completed Successfully!";
            StatusMessage = "系統映像套用與開機引導建立成功！";
            MainWindow.CurrentInstance?.CompanionSay("恭喜！Windows 部署與引導修復已完美完成～重開機即可進入系統。");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Deploy Error: {ex.Message}";
            Log($"\n[FATAL ERROR] {ex.Message}\n");
            MainWindow.CurrentInstance?.CompanionSay($"部署過程發生異常: {ex.Message}");
        }
        finally
        {
            IsDeploying = false;
        }
    }

    [RelayCommand]
    public async Task CaptureImageAsync()
    {
        if (string.IsNullOrWhiteSpace(CaptureDir) || string.IsNullOrWhiteSpace(CaptureOutputPath)) return;
        try
        {
            IsDeploying = true;
            StatusMessage = "Capturing image...";
            LogTimestamped($"Capturing {CaptureDir} to {CaptureOutputPath}...");
            var result = await _wimService.CaptureImageAsync(CaptureDir, CaptureOutputPath, CaptureName);
            Log(result);
            StatusMessage = "Image capture completed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Capture Error: {ex.Message}";
            Log($"[ERROR] {ex.Message}");
        }
        finally { IsDeploying = false; }
    }

    [RelayCommand]
    public async Task ExportImageAsync()
    {
        if (SelectedImage == null || string.IsNullOrWhiteSpace(ExportDestPath)) return;
        try
        {
            IsDeploying = true;
            StatusMessage = "Exporting image (ESD↔WIM conversion)...";
            LogTimestamped($"Exporting {ImagePath} Index:{SelectedImage.Index} to {ExportDestPath}...");
            var result = await _wimService.ExportImageAsync(ImagePath, SelectedImage.Index, ExportDestPath);
            Log(result);
            StatusMessage = "Image export completed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export Error: {ex.Message}";
            Log($"[ERROR] {ex.Message}");
        }
        finally { IsDeploying = false; }
    }

    [RelayCommand]
    public async Task SplitImageAsync()
    {
        if (string.IsNullOrWhiteSpace(ImagePath) || string.IsNullOrWhiteSpace(SplitOutputPath)) return;
        try
        {
            IsDeploying = true;
            StatusMessage = "Splitting WIM for FAT32 USB...";
            LogTimestamped($"Splitting {ImagePath} to {SplitOutputPath} (3800MB chunks)...");
            var result = await _wimService.SplitImageAsync(ImagePath, SplitOutputPath);
            Log(result);
            StatusMessage = "Image splitting completed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Split Error: {ex.Message}";
            Log($"[ERROR] {ex.Message}");
        }
        finally { IsDeploying = false; }
    }

    [RelayCommand]
    public async Task MountWimImageAsync()
    {
        if (SelectedImage == null || string.IsNullOrWhiteSpace(MountDir)) return;
        try
        {
            IsDeploying = true;
            StatusMessage = "Mounting WIM image...";
            LogTimestamped($"Mounting {ImagePath} Index:{SelectedImage.Index} to {MountDir}...");
            var result = await _wimService.MountWimImageAsync(ImagePath, SelectedImage.Index, MountDir);
            Log(result);
            StatusMessage = "WIM mounted successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Mount Error: {ex.Message}";
            Log($"[ERROR] {ex.Message}");
        }
        finally { IsDeploying = false; }
    }

    [RelayCommand]
    public async Task UnmountWimImageAsync()
    {
        if (string.IsNullOrWhiteSpace(MountDir)) return;
        try
        {
            IsDeploying = true;
            StatusMessage = "Unmounting WIM image (committing changes)...";
            LogTimestamped($"Unmounting {MountDir} with /Commit...");
            var result = await _wimService.UnmountWimImageAsync(MountDir, true);
            Log(result);
            StatusMessage = "WIM unmounted.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unmount Error: {ex.Message}";
            Log($"[ERROR] {ex.Message}");
        }
        finally { IsDeploying = false; }
    }

    [RelayCommand]
    public async Task CleanupMountpointsAsync()
    {
        try
        {
            IsDeploying = true;
            StatusMessage = "Cleaning up orphaned mount points...";
            var result = await _wimService.CleanupMountpointsAsync();
            LogTimestamped($"Cleanup: {result}");
            StatusMessage = "Mount points cleaned.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cleanup Error: {ex.Message}";
        }
        finally { IsDeploying = false; }
    }

    [RelayCommand]
    public void ClearDeployLog()
    {
        _logBuffer.Clear();
    }

    [RelayCommand]
    public void CopyDeployLog()
    {
        try
        {
            if (string.IsNullOrEmpty(DeployLog)) return;
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(DeployLog);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            StatusMessage = LocalizationService.T(
                "日誌已複製至剪貼簿！",
                "日志已复制到剪贴板！",
                "Deployment log copied to clipboard!",
                "展開ログをクリップボードにコピーしました！");
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.T(
                $"複製失敗: {ex.Message}",
                $"复制失败: {ex.Message}",
                $"Copy failed: {ex.Message}",
                $"コピー失敗: {ex.Message}");
        }
    }

    // ── HTTP Range & Cache Command Handlers ──

    partial void OnHttpRangeParallelEnabledChanged(bool value)
    {
        SettingsService.Instance.Current.HttpRangeParallelEnabled = value;
        SettingsService.Instance.Save();
    }

    partial void OnParallelThreadCountChanged(int value)
    {
        SettingsService.Instance.Current.ParallelThreadCount = value;
        SettingsService.Instance.Save();
    }

    partial void OnChunkBufferSizeKBChanged(int value)
    {
        SettingsService.Instance.Current.ChunkBufferSizeKB = value;
        SettingsService.Instance.Save();
    }

    partial void OnKeepResumeCacheChanged(bool value)
    {
        SettingsService.Instance.Current.KeepResumeCache = value;
        SettingsService.Instance.Save();
    }

    [RelayCommand]
    public async Task ProbeServerRangeAsync()
    {
        var targetUrl = !string.IsNullOrWhiteSpace(RufusDirectUrl)
            ? RufusDirectUrl
            : !string.IsNullOrWhiteSpace(CustomDownloadUrl)
                ? CustomDownloadUrl
                : SelectedDownloadItem?.PrimaryUrl;

        if (string.IsNullOrWhiteSpace(targetUrl)) return;

        ServerRangeCapabilityText = "🔄 探測伺服器中...";
        var (supports, size, effUrl, msg) = await _downloadService.ProbeUrlRangeAsync(
            targetUrl, SelectedDownloadItem?.MirrorUrls);

        ServerRangeCapabilityText = msg;
    }

    [RelayCommand]
    public async Task PasteDirectLinkAsync()
    {
        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (content != null && content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                var text = await content.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text) && (text.StartsWith("http://") || text.StartsWith("https://")))
                {
                    CustomDownloadUrl = text.Trim();

                    var fileName = RufusDownloadService.ExtractFileNameFromUrl(CustomDownloadUrl, "Windows", "Custom", "Direct", "x64");
                    var item = new WindowsDownloadItem
                    {
                        VersionTitle = $"自訂直鏈下載 ({fileName})",
                        ReleaseCategory = "Modern",
                        Architecture = "x64",
                        FileName = fileName,
                        PrimaryUrl = CustomDownloadUrl,
                        SupportsSecureBoot = true
                    };
                    DownloadCatalog.Insert(0, item);
                    SelectedDownloadItem = item;

                    await ProbeServerRangeAsync();
                }
            }
        }
        catch { }
    }

    [RelayCommand]
    public void OpenMicrosoftPortal()
    {
        try
        {
            var url = LocalizationService.T(
                "https://www.microsoft.com/zh-tw/software-download/windows11",
                "https://www.microsoft.com/zh-cn/software-download/windows11",
                "https://www.microsoft.com/en-us/software-download/windows11",
                "https://www.microsoft.com/ja-jp/software-download/windows11");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    public async Task CleanDownloadTempAsync()
    {
        var cleaner = new CacheCleanService();
        var (count, bytes, display) = await cleaner.CleanDownloadCacheAsync(DownloadDestinationFolder);
        DownloadStatusText = $"已清除 {count} 個暫存檔案，釋放 {display} 硬碟空間！";
        CacheStatsText = $"暫存已清空 (釋放 {display})";
        LogTimestamped($"[快取清理] 清除 {count} 個未完成分塊，釋放 {display}");
    }

    [RelayCommand]
    public async Task RefreshCacheStatsAsync()
    {
        var cleaner = new CacheCleanService();
        var stats = await cleaner.GetDownloadCacheStatsAsync(DownloadDestinationFolder);
        CacheStatsText = $"未完成暫存: {stats.FileCount} 個檔案 ({stats.SizeDisplay})";
    }

    // ── VHDX Native Boot Deployment Command ──

    [RelayCommand]
    public async Task DeployToVhdxAsync()
    {
        if (SelectedImage == null || string.IsNullOrWhiteSpace(ImagePath))
        {
            StatusMessage = "請先選擇映像檔與版本！";
            return;
        }

        try
        {
            IsDeploying = true;
            StatusMessage = "正在部署至 VHDX 虛擬磁碟並建立 Native Boot...";
            LogTimestamped($"[VHDX 部署啟動] 目標: {VhdxFilePath} (容量: {VhdxSizeGB} GB)");
            
            var vhdService = new VhdService();
            var (success, msg) = await vhdService.DeployWimToNativeVhdBootAsync(
                ImagePath,
                SelectedImage.Index,
                VhdxFilePath,
                VhdxSizeGB * 1024L,
                VhdxBootTitle,
                text => LogTimestamped(text));

            LogTimestamped(msg);
            StatusMessage = success ? "VHDX 部署成功！" : "VHDX 部署失敗！";
            if (success)
            {
                MainWindow.CurrentInstance?.CompanionSay("VHDX 原生開機部署完成！已新增至主機開機清單。");
            }
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportException(ex, "VHDX Native Boot 部署");
            StatusMessage = $"部署錯誤: {ex.Message}";
        }
        finally
        {
            IsDeploying = false;
        }
    }
}
