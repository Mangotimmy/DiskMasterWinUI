using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

/// <summary>
/// ViewModel managing DNS presets, UPnP port forwarding, 5-stage smart network diagnostics, and Hosts editor.
/// </summary>
public partial class NetworkToolsViewModel : ObservableObject
{
    private readonly UpnpService _upnpService = new();
    private readonly NetworkDiagnosticService _diagService = new();
    private readonly HostsService _hostsService = new();

    [ObservableProperty] private string _statusMessage = "就緒";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isAdmin = AdminHelper.IsRunningAsAdmin();

    // ── 1. UPnP Port Forwarding ──
    [ObservableProperty] private bool _isUpnpSupported = true;
    [ObservableProperty] private bool _isLoadingUpnp;
    [ObservableProperty] private string _localIpAddress = "127.0.0.1";
    [ObservableProperty] private int _newExternalPort = 25565;
    [ObservableProperty] private int _newInternalPort = 25565;
    [ObservableProperty] private string _newProtocol = "TCP";
    [ObservableProperty] private string _newDescription = "Minecraft Server";
    public ObservableCollection<UpnpPortMappingItem> PortMappings { get; } = new();
    public ObservableCollection<UpnpPortMappingItem> GamePresets { get; } = new();
    [ObservableProperty] private UpnpPortMappingItem? _selectedGamePreset;

    // ── 2. Recommended DNS Settings & Adapters ──
    public ObservableCollection<NetworkAdapterItem> NetworkAdapters { get; } = new();
    [ObservableProperty] private NetworkAdapterItem? _selectedAdapter;
    public ObservableCollection<DnsPreset> DnsPresets { get; } = new();
    [ObservableProperty] private DnsPreset? _selectedDnsPreset;
    [ObservableProperty] private string _customPrimaryDns = "1.1.1.1";
    [ObservableProperty] private string _customSecondaryDns = "1.0.0.1";
    [ObservableProperty] private bool _isApplyingDns;
    [ObservableProperty] private bool _isPingingDns;
    [ObservableProperty] private string _dnsPingSummary = "尚未測試 Ping 延遲";

    // ── 3. Smart Network Diagnostic ──
    public ObservableCollection<DiagnosticStepResult> DiagnosticResults { get; } = new();
    [ObservableProperty] private bool _isRunningDiagnostics;
    [ObservableProperty] private string _diagnosticSummary = "點擊「開始智慧診斷」全面探測閘道器、DNS、連線延遲與網卡健康。";

    // ── 4. Hosts File Editor & Smart Detector & Backups ──
    public ObservableCollection<HostsEntryItem> HostsEntries { get; } = new();
    public ObservableCollection<HostsBackupItem> HostsBackups { get; } = new();
    [ObservableProperty] private string _rawHostsText = "";
    [ObservableProperty] private bool _isLoadingHosts;
    [ObservableProperty] private string _newHostIp = "127.0.0.1";
    [ObservableProperty] private string _newHostDomain = "";
    [ObservableProperty] private string _newHostComment = "";

    // Notepad editor status properties
    [ObservableProperty] private int _cursorLine = 1;
    [ObservableProperty] private int _cursorColumn = 1;
    [ObservableProperty] private int _totalCharacters;
    [ObservableProperty] private int _lineCount = 1;
    [ObservableProperty] private int _editorZoomPercent = 100;
    [ObservableProperty] private double _editorFontSize = 13.0;
    [ObservableProperty] private bool _isWordWrapEnabled = false;
    [ObservableProperty] private bool _isModifiedUnsaved = false;
    [ObservableProperty] private string _saveStatusDisplay = "🟢 已儲存";

    // Smart Detector and Backups
    [ObservableProperty] private HostsAnalysisReport _hostsAnalysis = new();
    [ObservableProperty] private bool _showSmartAuditPanel;
    [ObservableProperty] private bool _showBackupDrawer;
    [ObservableProperty] private string _newBackupNote = "";
    [ObservableProperty] private HostsBackupItem? _selectedBackup;

    // ── Notepad-grade editor plumbing (never touched from the per-keystroke path) ──
    // While the user types, the editor TextBox is the single source of truth and the document is
    // never bound back into it. Commands that genuinely need the live text pull it exactly once
    // through this provider (see SyncHostsEditorText); the editor calls ApplyEditorSnapshotAsync
    // from a debounce timer only, after typing has paused.
    public Func<string>? HostsEditorTextProvider { get; set; }

    // First-character offset of every line of the last applied snapshot. Maps the caret to a
    // line/column with a binary search instead of a full-document character scan.
    private int[] _editorLineStarts = new[] { 0 };
    private int _editorSnapshotLength;
    private int _editorSnapshotGeneration;

    /// <summary>
    /// Copies the live editor text into <see cref="RawHostsText"/> once, for explicit actions
    /// (save / format / audit). Must not be called from a per-keystroke path.
    /// </summary>
    public void SyncHostsEditorText()
    {
        var provider = HostsEditorTextProvider;
        if (provider == null) return;
        try
        {
            var live = provider();
            if (live != null && !ReferenceEquals(live, RawHostsText)) RawHostsText = live;
        }
        catch { }
    }

    /// <summary>
    /// Applies a debounced snapshot of the editor text: character/line counts and the structural
    /// health report are computed on a background thread and then marshalled to the UI thread.
    /// </summary>
    public Task ApplyEditorSnapshotAsync(string snapshot)
    {
        snapshot ??= "";
        int generation = ++_editorSnapshotGeneration;
        return Task.Run(() =>
        {
            try
            {
                int[] lineStarts = BuildLineIndex(snapshot, out int lineCount);
                HostsAnalysisReport report = _hostsService.AnalyzeHosts(snapshot);
                DispatcherHelper.RunOnUIThread(() =>
                {
                    if (generation != _editorSnapshotGeneration) return; // a newer snapshot already won
                    _editorLineStarts = lineStarts;
                    _editorSnapshotLength = snapshot.Length;
                    TotalCharacters = snapshot.Length;
                    LineCount = lineCount;
                    HostsAnalysis = report;
                });
            }
            catch (Exception ex)
            {
                DebugLogService.Instance.Error("Hosts editor debounced analysis failed", ex, "NetworkTools");
            }
        });
    }

    /// <summary>
    /// Maps a caret offset onto line/column using the cached line index (O(log n) binary search).
    /// Deliberately allocation-free: it never reads the document text out of the control.
    /// </summary>
    public void UpdateCursorPosition(int selectionStart)
    {
        var lineStarts = _editorLineStarts;
        if (lineStarts == null || lineStarts.Length == 0) return;

        int offset = selectionStart < 0 ? 0 : selectionStart;
        int searchOffset = offset > _editorSnapshotLength ? _editorSnapshotLength : offset;

        int index = Array.BinarySearch(lineStarts, searchOffset);
        if (index < 0) index = ~index - 1;
        if (index < 0) index = 0;
        if (index >= lineStarts.Length) index = lineStarts.Length - 1;

        int column = offset - lineStarts[index] + 1;
        CursorLine = index + 1;
        CursorColumn = column < 1 ? 1 : column;
    }

    /// <summary>
    /// Records the first-character offset of every line with two flat passes and no per-line
    /// string allocations (unlike <c>text.Split('\n')</c>, which allocates one string per line).
    /// </summary>
    private static int[] BuildLineIndex(string text, out int lineCount)
    {
        int capacity = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') capacity++;
        }

        var lineStarts = new int[capacity];
        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') lineStarts[count++] = i + 1;
        }

        lineCount = count;
        return lineStarts;
    }

    // ── 5. NetShare & File Sharing Wizard ──
    private readonly NetShareService _netShareService = new();
    public ObservableCollection<NetShareItem> NetShares { get; } = new();
    [ObservableProperty] private string _newShareName = "SharedFolder";
    [ObservableProperty] private string _newSharePath = @"C:\Share";
    [ObservableProperty] private string _newShareRemark = "DiskMaster LAN Share";
    [ObservableProperty] private string _dedicatedUserName = "ShareUser";
    [ObservableProperty] private string _dedicatedUserPassword = "Password123!";
    [ObservableProperty] private string _newShareAccount = "";
    [ObservableProperty] private string _shareAccountDisplay = "";
    [ObservableProperty] private string _shareGrantAccount = "";
    [ObservableProperty] private string _shareGrantAccountDisplay = "";
    [ObservableProperty] private string _selectedShareAccessRight = "Full";
    [ObservableProperty] private bool _applyNtfsPermission;
    [ObservableProperty] private NetShareItem? _selectedNetShare;
    [ObservableProperty] private bool _isNetShareBusy;
    [ObservableProperty] private string _selectedShareDetailsText = "";
    [ObservableProperty] private string _shareSearchText = "";
    [ObservableProperty] private bool _showAdminShares;
    [ObservableProperty] private ShareDetailsItem? _selectedShareDetails;
    [ObservableProperty] private bool _isShareDetailsLoading;
    [ObservableProperty] private string _sharePermissionSummary = "";
    public ObservableCollection<NetShareItem> VisibleNetShares { get; } = new();
    public ObservableCollection<ShareAclItem> SelectedShareAcl { get; } = new();
    public bool HasSelectedShare => SelectedNetShare != null;

    partial void OnShareSearchTextChanged(string value) => ApplyShareFilter();
    partial void OnShowAdminSharesChanged(bool value) => ApplyShareFilter();
    partial void OnSelectedNetShareChanged(NetShareItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedShare));
        _ = LoadSelectedShareDetailsAsync();
    }
    public List<string> ShareAccessRightOptions { get; } = ["Full", "Change", "Read"];
    [ObservableProperty] private bool _isMsAccountPolicyFixed;
    [ObservableProperty] private string _netShareStatusMessage = "";

    // ── 6. Remote Desktop (RDP Server) ──
    private readonly RdpServerService _rdpService = new();
    [ObservableProperty] private RdpStatusInfo _rdpStatus = new();
    public ObservableCollection<RdpSessionItem> RdpSessions { get; } = new();
    [ObservableProperty] private string _rdpStatusMessage = "";
    [ObservableProperty] private int _customRdpPort = 3389;
    [ObservableProperty] private bool _isSingleSessionPerUser = true;
    [ObservableProperty] private bool _isNlaRequired = false;
    [ObservableProperty] private int _selectedShadowMode = 1;
    [ObservableProperty] private bool _isUpdatingRdpIni = false;
    [ObservableProperty] private string _rdpEnableButtonText = "⚡ 一鍵啟用遠端桌面 (3389)";

    // ── 7. CMD CLI Network Tools (Ping, Netstat, OpenFiles, Nbtstat) ──
    private readonly CliToolsService _cliToolsService = new();
    [ObservableProperty] private PingOptionsModel _pingOptions = new();
    [ObservableProperty] private string _pingOutputLog = "";
    [ObservableProperty] private bool _isPinging;
    [ObservableProperty] private int _detectedMtu = 1500;
    [ObservableProperty] private bool _isDetectingMtu;

    public ObservableCollection<NetstatConnectionItem> NetstatConnections { get; } = new();
    [ObservableProperty] private int? _filterPort;
    [ObservableProperty] private bool _isLoadingNetstat;
    [ObservableProperty] private bool _hasScannedNetstat;
    [ObservableProperty] private string _netstatEmptyStatus = "";

    // ── Remote Port Reachability & Telnet ──
    [ObservableProperty] private string _remoteTestHost = "google.com";
    [ObservableProperty] private int _remoteTestPort = 443;
    [ObservableProperty] private bool _isTestingRemotePort;
    [ObservableProperty] private string _remotePortTestResult = "";
    [ObservableProperty] private string _telnetInstallStatus = "";
    [ObservableProperty] private bool _isInstallingTelnet;

    public ObservableCollection<OpenSharedFileItem> OpenFiles { get; } = new();
    [ObservableProperty] private bool _isLoadingOpenFiles;

    // ── 8. DNS Pollution Detection & Anti-Pollution ──
    private readonly DnsPollutionService _dnsPollutionService = new();
    public ObservableCollection<DnsPollutionItem> PollutionResults { get; } = new();
    [ObservableProperty] private string _testPollutionDomain = "github.com";
    [ObservableProperty] private bool _isTestingPollution;

    // ── 9. Global TCP Auto-Tuning & Stack CLI (netsh int tcp set global autotuninglevel) ──
    private readonly TcpOptimizerService _tcpOptimizerService = new();
    public List<string> TcpAutoTuningLevels { get; } = ["normal", "experimental", "restricted", "highlyrestricted", "disabled"];
    [ObservableProperty] private string _tcpAutoTuningLevel = "normal";
    [ObservableProperty] private string _tcpCongestionProvider = "CUBIC";
    [ObservableProperty] private string _tcpEcnState = "disabled";
    [ObservableProperty] private string _tcpRssState = "enabled";
    [ObservableProperty] private string _tcpRscState = "enabled";
    [ObservableProperty] private string _tcpTimestampsState = "disabled";
    [ObservableProperty] private string _tcpGlobalRawOutput = "";
    [ObservableProperty] private string _tcpGeneratedCommand = "netsh int tcp set global autotuninglevel=normal";
    [ObservableProperty] private string _tcpStatusMessage = "尚未讀取全域 TCP 自動微調狀態";
    [ObservableProperty] private bool _isApplyingTcpGlobal;

    partial void OnTcpAutoTuningLevelChanged(string value)
    {
        var lvl = string.IsNullOrWhiteSpace(value) ? "normal" : value.Trim().ToLowerInvariant();
        TcpGeneratedCommand = $"netsh int tcp set global autotuninglevel={lvl}";
    }

    private bool _isInitialized;
    private bool _isLoadingData;
    private int _refreshSharesBusy;

    // Single-flight bookkeeping. Without these, deactivation cleared _isLoadingData while the
    // abandoned load was still running, so a fast re-activation started a second full load on top
    // of the first (measured: up to 4 concurrent loads in the shipped v1.6.3 log).
    private int _loadGeneration;
    private bool _reloadRequested;

    // ── Page-lifecycle awareness ──
    // The page reports when it is attached/detached. Work started while the page was visible is
    // cancelled when the user switches away, so a slow background refresh can never keep marshalling
    // UI updates into a page that is being detached (that churn fed the tab-switch freezes).
    // Defaults to true so the page keeps working exactly as before if the lifecycle hooks are not
    // wired; the page only has to report deactivation to gain the abandoned-work cancellation.
    private volatile bool _isPageActive = true;
    private CancellationTokenSource? _lifecycleCts;

    /// <summary>True once the first successful load has completed.</summary>
    public bool IsDataInitialized => _isInitialized;

    /// <summary>True while the owning page is attached to the visual tree.</summary>
    public bool IsPageActive => _isPageActive;

    /// <summary>Called by the page when it is loaded/attached.</summary>
    public void OnPageActivated()
    {
        _isPageActive = true;
        if (_isLoadingData)
        {
            // A load is still running: remember that the data must be refreshed once it ends
            // instead of stacking another concurrent load on top of it.
            _reloadRequested = true;
            return;
        }
        if (!_isInitialized)
        {
            _ = InitializeAsync();
        }
    }

    /// <summary>Called by the page when it is unloaded/detached. Cancels abandoned background work.</summary>
    public void OnPageDeactivated()
    {
        _isPageActive = false;
        CancelPendingWork();
    }

    /// <summary>
    /// Cancels in-flight lifecycle work. The refresh will run again the next time the page becomes
    /// active, so no data is lost — only work whose UI updates target a page that is going away.
    /// </summary>
    public void CancelPendingWork()
    {
        try
        {
            var cts = Interlocked.Exchange(ref _lifecycleCts, null);
            cts?.Cancel();
            cts?.Dispose();
        }
        catch { }

        Volatile.Write(ref _refreshSharesBusy, 0);
        // Do not clear _isLoadingData here: the abandoned load owns that flag and clears it in its
        // own finally block. Clearing it early is what allowed overlapping loads.
        IsLoading = false;
    }

    /// <summary>
    /// Marshals a UI update only while the owning page is still attached. Data stays in the ViewModel
    /// either way; this only stops background refreshes from touching the controls of a page that is
    /// being detached, which is what used to pile UI work on the thread during rapid tab switching.
    /// </summary>
    private void RunOnUIThreadIfActive(Action action)
    {
        if (action == null || !_isPageActive) return;
        DispatcherHelper.RunOnUIThread(action);
    }

    public NetworkToolsViewModel()
    {
        // 0ms instant initialization: load in-memory presets without blocking network or WMI
        foreach (var preset in UpnpService.GetGamePresets())
        {
            GamePresets.Add(preset);
        }
        foreach (var dns in NetworkDiagnosticService.GetRecommendedDnsPresets())
        {
            DnsPresets.Add(dns);
        }
        SelectedDnsPreset = DnsPresets.FirstOrDefault();
        RefreshL2tpVpnNatTStatus();
    }

    [RelayCommand]
    public async Task RefreshNetworkAdaptersAsync()
    {
        var nics = await Task.Run(() => NetworkDiagnosticService.GetAvailableNetworkAdapters());
        DispatcherHelper.RunOnUIThread(() =>
        {
            NetworkAdapters.Clear();
            foreach (var nic in nics)
            {
                NetworkAdapters.Add(nic);
            }
            SelectedAdapter = NetworkAdapters.FirstOrDefault(a => a.IsPrimary) ?? NetworkAdapters.FirstOrDefault();
        });
    }

    public void RefreshNetworkAdapters()
    {
        _ = RefreshNetworkAdaptersAsync();
    }

    [RelayCommand]
    public async Task InitializeAsync(bool forceReload = false)
    {
        // Coalescing: a burst of re-activations (rapid tab switching) starts exactly one load.
        if (_isLoadingData)
        {
            _reloadRequested = true;
            return;
        }
        if (!forceReload && _isInitialized) return;
        _reloadRequested = false;
        _isLoadingData = true;
        IsLoading = true;
        int generation = Interlocked.Increment(ref _loadGeneration);

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _lifecycleCts, cts);
        var token = cts.Token;

        var sw = Stopwatch.StartNew();
        DebugLogService.Instance.Debug("Starting non-blocking NetworkTools background initialization...", "NetworkTools");

        try
        {
            // 1. Resolve local IP and network adapters in background
            var nicTask = Task.Run(async () =>
            {
                var ip = await _upnpService.GetLocalIpAddressAsync();
                var nics = NetworkDiagnosticService.GetAvailableNetworkAdapters();
                var isMsPolicy = await _netShareService.IsMicrosoftAccountSharingPolicyFixedAsync();
                var rdp = await _rdpService.GetStatusAsync();

                RunOnUIThreadIfActive(() =>
                {
                    LocalIpAddress = ip;
                    IsMsAccountPolicyFixed = isMsPolicy;
                    RdpStatus = rdp;
                    NetworkAdapters.Clear();
                    foreach (var nic in nics)
                    {
                        NetworkAdapters.Add(nic);
                    }
                    SelectedAdapter = NetworkAdapters.FirstOrDefault(a => a.IsPrimary) ?? NetworkAdapters.FirstOrDefault();
                });
            });

            // 2. Fetch independent features in parallel
            var upnpTask = RefreshUpnpAsync();
            var hostsTask = RefreshHostsAsync();
            var sharesTask = RefreshSharesAsync();
            var rdpTask = RefreshRdpStatusAsync();
            var tcpTask = RefreshTcpGlobalAsync();
            RefreshL2tpVpnNatTStatus();

            await Task.WhenAll(nicTask, upnpTask, hostsTask, sharesTask, rdpTask, tcpTask);
            if (token.IsCancellationRequested || !_isPageActive)
            {
                // The user switched away: keep the partial data but let the next activation retry,
                // and never report a "completed" state for an abandoned load.
                DebugLogService.Instance.Debug("NetworkTools initialization abandoned (page detached)", "NetworkTools");
                return;
            }
            _isInitialized = true;
            StatusMessage = LocalizationService.T("網路工具資料已同步完成", "网络工具数据已同步完成", "Network tools data synchronized", "ネットワークツールの同期完了");
        }
        catch (OperationCanceledException)
        {
            DebugLogService.Instance.Debug("NetworkTools initialization cancelled", "NetworkTools");
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Error("NetworkTools background initialization failed", ex, "NetworkTools");
        }
        finally
        {
            // Only the newest load may clear the shared flags; a stale load finishing late must not
            // clobber the state of the load that replaced it.
            if (generation == Volatile.Read(ref _loadGeneration))
            {
                _isLoadingData = false;
                IsLoading = false;
            }
            sw.Stop();
            DebugLogService.Instance.Info($"NetworkTools background initialization completed in {sw.ElapsedMilliseconds}ms", "NetworkTools");

            if (_reloadRequested && _isPageActive && !_isInitialized &&
                generation == Volatile.Read(ref _loadGeneration))
            {
                _reloadRequested = false;
                _ = InitializeAsync(forceReload: true);
            }
        }
    }

    [RelayCommand]
    public async Task LoadInitialDataAsync()
    {
        await InitializeAsync(forceReload: true);
    }

    // ══════════════════════════════════════════════════════════
    //  UPnP Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task RefreshUpnpAsync()
    {
        try
        {
            IsLoadingUpnp = true;
            LocalIpAddress = await _upnpService.GetLocalIpAddressAsync();
            IsUpnpSupported = await _upnpService.IsUpnpSupportedAsync();

            if (!IsUpnpSupported)
            {
                RunOnUIThreadIfActive(() => PortMappings.Clear());
                StatusMessage = "路由器未啟用 UPnP 或無回應。";
                return;
            }

            var (success, list, err) = await _upnpService.GetPortMappingsAsync();
            RunOnUIThreadIfActive(() =>
            {
                PortMappings.Clear();
                foreach (var item in list) PortMappings.Add(item);
            });

            if (!success && !string.IsNullOrEmpty(err))
            {
                StatusMessage = $"UPnP 讀取: {err}";
            }
            else
            {
                StatusMessage = $"UPnP 轉發規則已載入 ({PortMappings.Count} 條)";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"UPnP 例外: {ex.Message}";
        }
        finally
        {
            IsLoadingUpnp = false;
        }
    }

    [RelayCommand]
    public async Task AddPortMappingAsync()
    {
        if (NewExternalPort <= 0 || NewInternalPort <= 0)
        {
            StatusMessage = "請輸入有效的通訊埠號碼 (1-65535)。";
            return;
        }

        try
        {
            IsLoadingUpnp = true;
            var item = new UpnpPortMappingItem
            {
                ExternalPort = NewExternalPort,
                InternalPort = NewInternalPort,
                Protocol = NewProtocol,
                InternalClient = LocalIpAddress,
                Description = string.IsNullOrWhiteSpace(NewDescription) ? $"Port_{NewExternalPort}" : NewDescription,
                Enabled = true
            };

            var (ok, msg) = await _upnpService.AddPortMappingAsync(item);
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
            else AudioFeedbackService.PlayWarning();

            await RefreshUpnpAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"新增通訊埠失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsLoadingUpnp = false;
        }
    }

    [RelayCommand]
    public async Task DeletePortMappingAsync(UpnpPortMappingItem? item)
    {
        if (item == null) return;
        try
        {
            IsLoadingUpnp = true;
            var (ok, msg) = await _upnpService.DeletePortMappingAsync(item.ExternalPort, item.Protocol);
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
            await RefreshUpnpAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"刪除轉發規則失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsLoadingUpnp = false;
        }
    }

    [RelayCommand]
    public void ApplyGamePreset(UpnpPortMappingItem? preset)
    {
        if (preset == null) return;
        NewExternalPort = preset.ExternalPort;
        NewInternalPort = preset.InternalPort;
        NewProtocol = preset.Protocol;
        NewDescription = preset.Description;
        StatusMessage = $"已載入範本: {preset.Description} ({preset.ExternalPort} {preset.Protocol})";
    }

    // ══════════════════════════════════════════════════════════
    //  DNS Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task ApplySelectedDnsAsync()
    {
        if (SelectedDnsPreset == null) return;
        string targetName = SelectedAdapter?.Name ?? "";
        DebugLogService.Instance.LogAction("網路工具 (NetworkTools)", $"套用 DNS ({SelectedDnsPreset.Name}) 至網卡 '{targetName}'");
        try
        {
            IsApplyingDns = true;
            StatusMessage = $"正在套用 DNS ({SelectedDnsPreset.Name}) 至網卡 '{targetName}'...";
            var (ok, msg) = await _diagService.ApplyDnsAsync(SelectedDnsPreset.PrimaryDns, SelectedDnsPreset.SecondaryDns, targetName);
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
            else AudioFeedbackService.PlayWarning();
        }
        catch (Exception ex)
        {
            StatusMessage = $"DNS 設定失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsApplyingDns = false;
        }
    }

    [RelayCommand]
    public async Task ApplyCustomDnsAsync()
    {
        try
        {
            IsApplyingDns = true;
            string targetName = SelectedAdapter?.Name ?? "";
            DebugLogService.Instance.LogAction("網路工具 (NetworkTools)", $"套用自訂 DNS ({CustomPrimaryDns}, {CustomSecondaryDns}) 至網卡 '{targetName}'");
            StatusMessage = $"正在套用自訂 DNS 至網卡 '{targetName}'...";
            var (ok, msg) = await _diagService.ApplyDnsAsync(CustomPrimaryDns, CustomSecondaryDns, targetName);
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
            else AudioFeedbackService.PlayWarning();
        }
        catch (Exception ex)
        {
            StatusMessage = $"DNS 設定失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsApplyingDns = false;
        }
    }

    [RelayCommand]
    public async Task ResetDnsToDhcpAsync()
    {
        try
        {
            IsApplyingDns = true;
            string targetName = SelectedAdapter?.Name ?? "";
            DebugLogService.Instance.LogAction("網路工具 (NetworkTools)", $"還原網卡 '{targetName}' 為自動 DHCP DNS");
            StatusMessage = $"正在還原網卡 '{targetName}' 為自動 DHCP DNS...";
            var (ok, msg) = await _diagService.ApplyDnsAsync("", "", targetName);
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
            else AudioFeedbackService.PlayWarning();
        }
        catch (Exception ex)
        {
            StatusMessage = $"還原 DHCP 失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsApplyingDns = false;
        }
    }

    [RelayCommand]
    public async Task TestDnsPingAsync()
    {
        if (IsPingingDns) return;
        try
        {
            IsPingingDns = true;
            DnsPingSummary = "正在並行探測全網 DNS 伺服器延遲 (ICMP Echo)...";

            var tasks = DnsPresets.Select(async preset =>
            {
                if (string.IsNullOrWhiteSpace(preset.PrimaryDns))
                {
                    preset.PingMs = -1;
                    preset.IsFastest = false;
                    return;
                }

                long latency = await NetworkDiagnosticService.PingDnsServerAsync(preset.PrimaryDns);
                preset.PingMs = latency;
                preset.IsFastest = false;
            }).ToList();

            await Task.WhenAll(tasks);

            // Determine fastest
            var validPresets = DnsPresets.Where(p => p.PingMs > 0).ToList();
            if (validPresets.Count > 0)
            {
                var fastest = validPresets.OrderBy(p => p.PingMs).First();
                fastest.IsFastest = true;
                DnsPingSummary = $"測速完成！最快 DNS: {fastest.Name} ({fastest.PingMs} ms)";
            }
            else
            {
                DnsPingSummary = "測速完成，但各伺服器暫無回應 (可能防火牆阻擋 ICMP)";
            }

            AudioFeedbackService.PlaySuccess();
        }
        catch (Exception ex)
        {
            DnsPingSummary = $"測速過程發生錯誤: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsPingingDns = false;
        }
    }

    [RelayCommand]
    public async Task ApplyFastestDnsAsync()
    {
        var fastest = DnsPresets.FirstOrDefault(p => p.IsFastest) ??
                      DnsPresets.Where(p => p.PingMs > 0).OrderBy(p => p.PingMs).FirstOrDefault();

        if (fastest != null)
        {
            SelectedDnsPreset = fastest;
            await ApplySelectedDnsAsync();
        }
        else
        {
            await TestDnsPingAsync();
            fastest = DnsPresets.FirstOrDefault(p => p.IsFastest);
            if (fastest != null)
            {
                SelectedDnsPreset = fastest;
                await ApplySelectedDnsAsync();
            }
        }
    }

    [RelayCommand]
    public void FlushDns()
    {
        DebugLogService.Instance.LogAction("網路工具 (NetworkTools)", "清除 Windows DNS 解析器快取 (ipconfig /flushdns)");
        HostsService.FlushDnsCache();
        StatusMessage = "已成功清除 Windows DNS 解析器快取 (ipconfig /flushdns)！";
        AudioFeedbackService.PlaySuccess();
    }

    // ══════════════════════════════════════════════════════════
    //  Smart Diagnostic Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task RunSmartDiagnosticsAsync()
    {
        DebugLogService.Instance.LogAction("網路工具 (NetworkTools)", "啟動 5 階段自動網路卡頓與延遲診斷");
        try
        {
            IsRunningDiagnostics = true;
            DiagnosticResults.Clear();
            DiagnosticSummary = "正在進行 5 階段自動網路卡頓與延遲診斷...";

            var results = await _diagService.RunSmartDiagnosticsAsync(stage =>
            {
                DispatcherHelper.RunOnUIThread(() => StatusMessage = stage);
            });

            foreach (var r in results) DiagnosticResults.Add(r);

            int failedCount = results.Count(r => !r.Success);
            if (failedCount == 0)
            {
                DiagnosticSummary = "🟢 網路狀態極佳：閘道器 Ping 正常、DNS 解析迅速、無封包遺失！";
                AudioFeedbackService.PlaySuccess();
            }
            else
            {
                DiagnosticSummary = $"⚠️ 診斷出 {failedCount} 項潛在網路問題，請參考下方各項建議。";
                AudioFeedbackService.PlayWarning();
            }
            StatusMessage = "診斷作業完成。";
        }
        catch (Exception ex)
        {
            DiagnosticSummary = $"診斷過程發生例外: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsRunningDiagnostics = false;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  Hosts Commands & Notepad Functions & Backups
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task RefreshHostsAsync()
    {
        try
        {
            IsLoadingHosts = true;
            RawHostsText = await _hostsService.ReadRawHostsAsync();
            IsModifiedUnsaved = false;
            SaveStatusDisplay = "🟢 已儲存";
            TotalCharacters = RawHostsText.Length;
            LineCount = RawHostsText.Split('\n').Length;
            HostsAnalysis = _hostsService.AnalyzeHosts(RawHostsText);

            var entries = await _hostsService.ReadHostsAsync();
            var backups = await _hostsService.GetBackupHistoryAsync();
            RunOnUIThreadIfActive(() =>
            {
                HostsEntries.Clear();
                foreach (var e in entries) HostsEntries.Add(e);

                HostsBackups.Clear();
                foreach (var b in backups) HostsBackups.Add(b);
            });
        }
        catch { }
        finally
        {
            IsLoadingHosts = false;
        }
    }

    [RelayCommand]
    public async Task SaveRawHostsAsync()
    {
        SyncHostsEditorText(); // persist exactly what is on screen, including unsaved edits
        try
        {
            IsLoadingHosts = true;
            var (ok, msg) = await _hostsService.SaveRawHostsAsync(RawHostsText, createBackup: true, backupNote: "手動儲存");
            StatusMessage = msg;
            if (ok)
            {
                IsModifiedUnsaved = false;
                SaveStatusDisplay = "🟢 已儲存";
                AudioFeedbackService.PlaySuccess();
            }
            else
            {
                AudioFeedbackService.PlayError();
            }
            await RefreshHostsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"儲存 Hosts 失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsLoadingHosts = false;
        }
    }

    [RelayCommand]
    public void RunHostsCheck()
    {
        SyncHostsEditorText();
        HostsAnalysis = _hostsService.AnalyzeHosts(RawHostsText);
        ShowSmartAuditPanel = !ShowSmartAuditPanel;
        StatusMessage = HostsAnalysis.SummaryText;
    }

    [RelayCommand]
    public void FormatHosts()
    {
        SyncHostsEditorText();
        RawHostsText = _hostsService.FormatAndAlignHosts(RawHostsText);
        IsModifiedUnsaved = true;
        SaveStatusDisplay = "🟡 已修改未儲存";
        TotalCharacters = RawHostsText.Length;
        LineCount = RawHostsText.Split('\n').Length;
        StatusMessage = "已將 Hosts 內容對齊格式化。";
    }

    [RelayCommand]
    public void DeduplicateHosts()
    {
        SyncHostsEditorText();
        RawHostsText = _hostsService.DeduplicateHosts(RawHostsText);
        IsModifiedUnsaved = true;
        SaveStatusDisplay = "🟡 已修改未儲存";
        TotalCharacters = RawHostsText.Length;
        LineCount = RawHostsText.Split('\n').Length;
        HostsAnalysis = _hostsService.AnalyzeHosts(RawHostsText);
        StatusMessage = "已清除重複之網域名稱對應。";
    }

    [RelayCommand]
    public void OpenInSystemNotepad()
    {
        _hostsService.OpenInSystemNotepad();
    }

    [RelayCommand]
    public void ToggleWordWrap()
    {
        IsWordWrapEnabled = !IsWordWrapEnabled;
    }

    [RelayCommand]
    public void ZoomIn()
    {
        if (EditorZoomPercent < 250)
        {
            EditorZoomPercent += 25;
            EditorFontSize = 13.0 * (EditorZoomPercent / 100.0);
        }
    }

    [RelayCommand]
    public void ZoomOut()
    {
        if (EditorZoomPercent > 50)
        {
            EditorZoomPercent -= 25;
            EditorFontSize = 13.0 * (EditorZoomPercent / 100.0);
        }
    }

    [RelayCommand]
    public void ResetZoom()
    {
        EditorZoomPercent = 100;
        EditorFontSize = 13.0;
    }

    [RelayCommand]
    public void ToggleBackupDrawer()
    {
        ShowBackupDrawer = !ShowBackupDrawer;
    }

    [RelayCommand]
    public async Task CreateHostsBackupAsync()
    {
        string note = string.IsNullOrWhiteSpace(NewBackupNote) ? "手動備份" : NewBackupNote.Trim();
        var (ok, msg) = await _hostsService.CreateManualBackupAsync(note);
        StatusMessage = msg;
        if (ok)
        {
            NewBackupNote = "";
            AudioFeedbackService.PlaySuccess();
            var backups = await _hostsService.GetBackupHistoryAsync();
            DispatcherHelper.RunOnUIThread(() =>
            {
                HostsBackups.Clear();
                foreach (var b in backups) HostsBackups.Add(b);
            });
        }
        else
        {
            AudioFeedbackService.PlayError();
        }
    }

    [RelayCommand]
    public async Task RestoreHostsBackupAsync(HostsBackupItem? item)
    {
        if (item == null) return;
        var (ok, msg) = await _hostsService.RestoreBackupAsync(item.FilePath);
        StatusMessage = msg;
        if (ok)
        {
            AudioFeedbackService.PlaySuccess();
            await RefreshHostsAsync();
        }
        else
        {
            AudioFeedbackService.PlayError();
        }
    }

    [RelayCommand]
    public async Task DeleteHostsBackupAsync(HostsBackupItem? item)
    {
        if (item == null) return;
        _hostsService.DeleteBackup(item.FilePath);
        var backups = await _hostsService.GetBackupHistoryAsync();
        DispatcherHelper.RunOnUIThread(() =>
        {
            HostsBackups.Clear();
            foreach (var b in backups) HostsBackups.Add(b);
        });
        StatusMessage = $"已刪除備份: {item.FileName}";
    }

    [RelayCommand]
    public async Task AddHostEntryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewHostDomain))
        {
            StatusMessage = "請輸入網域名稱。";
            return;
        }

        try
        {
            var currentRaw = await _hostsService.ReadRawHostsAsync();
            string lineToAdd = $"{NewHostIp} {NewHostDomain.Trim()}{(string.IsNullOrWhiteSpace(NewHostComment) ? "" : $" # {NewHostComment.Trim()}")}";
            string updated = currentRaw.TrimEnd() + "\r\n" + lineToAdd + "\r\n";

            var (ok, msg) = await _hostsService.SaveRawHostsAsync(updated);
            StatusMessage = msg;
            if (ok)
            {
                NewHostDomain = "";
                NewHostComment = "";
                AudioFeedbackService.PlaySuccess();
            }
            await RefreshHostsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"新增條目失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
    }

    [RelayCommand]
    public async Task RestoreDefaultHostsAsync()
    {
        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "還原 Windows 預設 Hosts 檔案 (Restore Default Hosts)",
            "即將覆寫 C:\\Windows\\System32\\drivers\\etc\\hosts 為 Windows 官方預設空白內容，現有自訂網域解析將會被清除 (系統會自動建立備份快照)。",
            "Restore Default Hosts");
        if (!confirmed) return;

        try
        {
            IsLoadingHosts = true;
            var (ok, msg) = await _hostsService.RestoreDefaultHostsAsync();
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
            await RefreshHostsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"還原 Hosts 失敗: {ex.Message}";
            AudioFeedbackService.PlayError();
        }
        finally
        {
            IsLoadingHosts = false;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  5. NetShare & File Sharing Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private void RestartAsAdmin() => AdminHelper.RestartAsAdmin();

    [RelayCommand]
    public async Task RefreshSharesAsync()
    {
        if (Interlocked.Exchange(ref _refreshSharesBusy, 1) != 0) return;
        try
        {
            var shares = await _netShareService.GetActiveSharesAsync();
            RunOnUIThreadIfActive(() =>
            {
                NetShares.Clear();
                foreach (var s in shares) NetShares.Add(s);
                ApplyShareFilter();
            });
            IsMsAccountPolicyFixed = await _netShareService.IsMicrosoftAccountSharingPolicyFixedAsync();
            NetShareStatusMessage = $"已列出 {NetShares.Count} 個共用資源";
        }
        catch (Exception ex)
        {
            NetShareStatusMessage = $"讀取共用失敗: {ex.Message}";
        }
        finally
        {
            Volatile.Write(ref _refreshSharesBusy, 0);
        }
    }


    public void ApplyShareFilter()
    {
        string q = ShareSearchText?.Trim() ?? "";
        VisibleNetShares.Clear();
        foreach (var share in NetShares)
        {
            if (!ShowAdminShares && share.IsAdminShare) continue;
            if (q.Length > 0 &&
                !share.ShareName.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !share.ResourcePath.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !share.AccessSummary.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            VisibleNetShares.Add(share);
        }
    }

    public async Task LoadSelectedShareDetailsAsync()
    {
        var share = SelectedNetShare;
        if (share == null)
        {
            SelectedShareDetails = null;
            SelectedShareAcl.Clear();
            SharePermissionSummary = "";
            return;
        }

        try
        {
            IsShareDetailsLoading = true;
            var details = await _netShareService.GetShareDetailsAsync(share.ShareName);
            if (!ReferenceEquals(SelectedNetShare, share)) return;
            SelectedShareDetails = details;
            SelectedShareAcl.Clear();
            if (details != null)
            {
                foreach (var acl in details.AclEntries) SelectedShareAcl.Add(acl);
                SharePermissionSummary = details.AclEntries.Count == 0
                    ? "No share permissions found."
                    : string.Join(" · ", details.AclEntries.Select(a => $"{a.Principal}: {a.AccessRight}"));
            }
            else
            {
                SharePermissionSummary = "Share details are unavailable.";
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(SelectedNetShare, share)) SharePermissionSummary = ex.Message;
        }
        finally
        {
            IsShareDetailsLoading = false;
        }
    }

    public void ApplyShareAccountSelection(LocalAccountItem? account)
    {
        NewShareAccount = account?.AccountName ?? "";
        ShareAccountDisplay = account == null
            ? ""
            : $"{account.AccountName} ({account.AccountType}, {account.StateText})";
    }

    public void ApplyShareGrantAccountSelection(LocalAccountItem? account)
    {
        ShareGrantAccount = account?.AccountName ?? "";
        ShareGrantAccountDisplay = account == null
            ? ""
            : $"{account.AccountName} ({account.AccountType}, {account.StateText})";
    }

    private async Task ShowShareResultAsync(string title, string message, bool success)
    {
        try
        {
            await DialogHelper.ShowOperationResultAsync(WindowHelper.GetXamlRoot(), title, message, success);
        }
        catch { }
    }

    [RelayCommand]
    public async Task CreateShareAsync()
    {
        if (IsNetShareBusy) return;
        try
        {
            IsNetShareBusy = true;
            var (ok, msg, _, _) = await _netShareService.CreateShareWithPermissionAsync(
                NewShareName, NewSharePath, NewShareAccount, SelectedShareAccessRight,
                ApplyNtfsPermission, NewShareRemark);
            NetShareStatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            if (ok) await RefreshSharesAsync();
            await ShowShareResultAsync(ok ? "共用建立完成 (Share created)" : "共用建立失敗 (Share failed)", msg, ok);
        }
        finally { IsNetShareBusy = false; }
    }

    [RelayCommand]
    public async Task DeleteShareAsync(NetShareItem? item)
    {
        if (item == null || IsNetShareBusy) return;
        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "停止網路共用資料夾 (Stop Network Share)",
            $"即將移除共用名稱「{item.ShareName}」({item.ResourcePath})，區域網路內其他電腦將無法再存取此共用路徑。",
            item.ShareName);
        if (!confirmed) return;

        try
        {
            IsNetShareBusy = true;
            var (ok, msg) = await _netShareService.DeleteShareAsync(item.ShareName);
            NetShareStatusMessage = msg;
            if (ok) await RefreshSharesAsync();
            await ShowShareResultAsync(ok ? "共用已刪除 (Share deleted)" : "刪除共用失敗 (Delete failed)", msg, ok);
        }
        finally { IsNetShareBusy = false; }
    }

    [RelayCommand]
    public async Task GrantSharePermissionAsync()
    {
        if (IsNetShareBusy) return;
        if (SelectedNetShare == null)
        {
            NetShareStatusMessage = "請先從共用清單選擇一個共用資源。";
            await ShowShareResultAsync("權限授予 (Grant)", NetShareStatusMessage, false);
            return;
        }
        if (string.IsNullOrWhiteSpace(ShareGrantAccount))
        {
            NetShareStatusMessage = "請先選擇要授權的帳號。";
            await ShowShareResultAsync("權限授予 (Grant)", NetShareStatusMessage, false);
            return;
        }

        try
        {
            IsNetShareBusy = true;
            var (ok, msg) = await _netShareService.GrantSharePermissionAsync(
                SelectedNetShare.ShareName, ShareGrantAccount, SelectedShareAccessRight,
                ApplyNtfsPermission, SelectedNetShare.ResourcePath);
            NetShareStatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await ShowShareResultAsync(ok ? "共用權限授予完成 (Grant complete)" : "共用權限授予失敗 (Grant failed)", msg, ok);
        }
        finally { IsNetShareBusy = false; }
    }

    [RelayCommand]
    public async Task RevokeSharePermissionAsync()
    {
        if (IsNetShareBusy) return;
        if (SelectedNetShare == null || string.IsNullOrWhiteSpace(ShareGrantAccount))
        {
            NetShareStatusMessage = "請先選擇共用資源與帳號。";
            await ShowShareResultAsync("權限移除 (Revoke)", NetShareStatusMessage, false);
            return;
        }

        try
        {
            IsNetShareBusy = true;
            var (ok, msg) = await _netShareService.RevokeSharePermissionAsync(SelectedNetShare.ShareName, ShareGrantAccount);
            NetShareStatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await ShowShareResultAsync(ok ? "共用權限已移除 (Revoked)" : "移除權限失敗 (Revoke failed)", msg, ok);
        }
        finally { IsNetShareBusy = false; }
    }

    [RelayCommand]
    public async Task ShowShareDetailsAsync()
    {
        if (SelectedNetShare == null) return;
        try
        {
            var details = await _netShareService.GetShareDetailsAsync(SelectedNetShare.ShareName);
            if (details == null)
            {
                await ShowShareResultAsync("共用詳細資料 (Share details)", "找不到共用詳細資料。", false);
                return;
            }
            var acl = details.AclEntries.Count > 0
                ? string.Join(Environment.NewLine, details.AclEntries.Select(a => $"{a.Principal} : {a.AccessRight} ({a.AccessType})"))
                : "No share ACL entries.";
            string text = $"Name: {details.ShareName}\nPath: {details.Path}\nDescription: {details.Description}\nConcurrent limit: {details.ConcurrentUserLimit}\nCaching: {details.CachingMode}\nEncrypt data: {details.EncryptData}\nFolder enumeration: {details.FolderEnumerationMode}\nAvailability: {details.AvailabilityType}\nScope: {details.ScopeName}\n\nShare ACL:\n{acl}";
            SelectedShareDetailsText = text;
            await ShowShareResultAsync("共用詳細資料 (Share details)", text, true);
        }
        catch (Exception ex)
        {
            await ShowShareResultAsync("共用詳細資料 (Share details)", ex.Message, false);
        }
    }

    [RelayCommand]
    public async Task ShowShareAclAsync()
    {
        if (SelectedNetShare == null) return;
        try
        {
            var acl = await _netShareService.GetShareAclAsync(SelectedNetShare.ShareName);
            string text = acl.Count == 0
                ? "No share ACL entries."
                : string.Join(Environment.NewLine, acl.Select(a => $"{a.Principal} : {a.AccessRight} ({a.AccessType})"));
            await ShowShareResultAsync("共用權限清單 (Share ACL)", text, true);
        }
        catch (Exception ex)
        {
            await ShowShareResultAsync("共用權限清單 (Share ACL)", ex.Message, false);
        }
    }

    [RelayCommand]
    public void FixMsAccountPolicy()
    {
        var (ok, msg) = _netShareService.FixMicrosoftAccountSharingPolicy();
        IsMsAccountPolicyFixed = _netShareService.IsMicrosoftAccountSharingPolicyFixed();
        NetShareStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        _ = ShowShareResultAsync(ok ? "微軟帳號修復完成" : "微軟帳號修復失敗", msg, ok);
    }

    [RelayCommand]
    public async Task SetPrivateNetworkAsync()
    {
        var (ok, msg) = await _netShareService.SetNetworkProfilePrivateAsync();
        NetShareStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        await ShowShareResultAsync(ok ? "私人網路設定完成" : "私人網路設定失敗", msg, ok);
    }

    [RelayCommand]
    public async Task EnableSharingFirewallAsync()
    {
        var (ok, msg) = await _netShareService.EnableFileSharingFirewallAsync();
        NetShareStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        await ShowShareResultAsync(ok ? "防火牆共用規則完成" : "防火牆共用規則失敗", msg, ok);
    }

    [RelayCommand]
    public async Task CreateDedicatedUserAsync()
    {
        var (ok, msg) = await _netShareService.CreateDedicatedShareUserAsync(DedicatedUserName, DedicatedUserPassword);
        NetShareStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        await ShowShareResultAsync(ok ? "專用帳號建立完成" : "專用帳號建立失敗", msg, ok);
    }

    // ══════════════════════════════════════════════════════════
    //  6. Remote Desktop (RDP Server) Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task RefreshRdpStatusAsync()
    {
        try
        {
            RdpStatus = await _rdpService.GetStatusAsync();
            var sessions = await _rdpService.GetActiveSessionsAsync();
            RunOnUIThreadIfActive(() =>
            {
                CustomRdpPort = RdpStatus.Port;
                IsSingleSessionPerUser = RdpStatus.SingleSessionPerUser;
                IsNlaRequired = RdpStatus.NetworkLevelAuthRequired;
                SelectedShadowMode = RdpStatus.ShadowMode;
                RdpEnableButtonText = $"⚡ 一鍵啟用遠端桌面 ({RdpStatus.Port})";

                RdpSessions.Clear();
                foreach (var s in sessions) RdpSessions.Add(s);
            });
            RdpStatusMessage = $"RDP 狀態已更新 ({RdpStatus.EditionName}，連接埠: {RdpStatus.Port})";
        }
        catch (Exception ex)
        {
            RdpStatusMessage = $"讀取 RDP 狀態失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task EnableRdpAsync()
    {
        RdpStatusMessage = $"正在啟用遠端桌面並設定防火牆 (連接埠: {RdpStatus.Port})...";
        var (ok, msg) = await _rdpService.EnableRdpServerAsync(RdpStatus.IsHomeEdition, RdpStatus.Port);
        RdpStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        await RefreshRdpStatusAsync();
    }

    [RelayCommand]
    public async Task DisableRdpAsync()
    {
        var (ok, msg) = await _rdpService.DisableRdpServerAsync();
        RdpStatusMessage = msg;
        await RefreshRdpStatusAsync();
    }

    [RelayCommand]
    public void LaunchRdpLoopback()
    {
        _rdpService.LaunchLoopbackTest(RdpStatus.Port);
    }

    [RelayCommand]
    public async Task ApplyCustomPortAsync()
    {
        if (CustomRdpPort < 1 || CustomRdpPort > 65535)
        {
            RdpStatusMessage = "請輸入有效的連接埠號 (1-65535)";
            AudioFeedbackService.PlayError();
            return;
        }

        RdpStatusMessage = $"正在切換連接埠至 {CustomRdpPort} 並更新防火牆規則...";
        var (ok, msg) = await _rdpService.SetRdpPortAsync(CustomRdpPort);
        RdpStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        await RefreshRdpStatusAsync();
    }

    [RelayCommand]
    public void SetPortPreset(string portStr)
    {
        if (int.TryParse(portStr, out int p))
        {
            CustomRdpPort = p;
        }
    }

    [RelayCommand]
    public async Task ToggleSingleSessionAsync()
    {
        var (ok, msg) = await _rdpService.SetSingleSessionPerUserAsync(IsSingleSessionPerUser);
        RdpStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        await RefreshRdpStatusAsync();
    }

    [RelayCommand]
    public async Task ToggleNlaAsync()
    {
        var (ok, msg) = await _rdpService.SetNetworkLevelAuthAsync(IsNlaRequired);
        RdpStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        await RefreshRdpStatusAsync();
    }

    [RelayCommand]
    public async Task ApplyShadowModeAsync()
    {
        var (ok, msg) = await _rdpService.SetShadowModeAsync(SelectedShadowMode);
        RdpStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        await RefreshRdpStatusAsync();
    }

    [RelayCommand]
    public async Task RestartTermServiceAsync()
    {
        RdpStatusMessage = "正在重啟 TermService 遠端桌面服務...";
        var (ok, msg) = await _rdpService.RestartTermServiceAsync();
        RdpStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        await RefreshRdpStatusAsync();
    }

    [RelayCommand]
    public async Task UpdateRdpWrapIniAsync()
    {
        IsUpdatingRdpIni = true;
        RdpStatusMessage = "正在從社群最新鏡像下載 rdpwrap.ini 並套用...";
        try
        {
            var (ok, msg) = await _rdpService.UpdateRdpWrapIniAsync();
            RdpStatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await RefreshRdpStatusAsync();
        }
        finally
        {
            IsUpdatingRdpIni = false;
        }
    }

    [RelayCommand]
    public async Task DisconnectSessionAsync(RdpSessionItem? session)
    {
        if (session == null) return;
        var (ok, msg) = await _rdpService.DisconnectSessionAsync(session.SessionId);
        RdpStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        await RefreshRdpStatusAsync();
    }

    [RelayCommand]
    public async Task LogoffSessionAsync(RdpSessionItem? session)
    {
        if (session == null) return;
        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "強制登出遠端桌面工作階段 (Logoff RDP Session)",
            $"即將強制登出使用者「{session.UserName}」(Session ID: {session.SessionId})，該工作階段中未儲存的工作將會遺失。",
            $"Session {session.SessionId}");
        if (!confirmed) return;

        var (ok, msg) = await _rdpService.LogoffSessionAsync(session.SessionId);
        RdpStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        await RefreshRdpStatusAsync();
    }

    // ══════════════════════════════════════════════════════════
    //  7. CMD CLI Network Tools Commands (Ping, Netstat, OpenFiles, Nbtstat)
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task RunPingAsync()
    {
        if (IsPinging) return;
        IsPinging = true;
        PingOutputLog = "";
        try
        {
            await _cliToolsService.RunPingAsync(PingOptions, line =>
            {
                DispatcherHelper.UIDispatcher?.TryEnqueue(() =>
                {
                    PingOutputLog += line + "\r\n";
                });
            });
        }
        finally
        {
            IsPinging = false;
        }
    }

    [RelayCommand]
    public async Task DetectMtuAsync()
    {
        if (IsDetectingMtu) return;
        IsDetectingMtu = true;
        PingOutputLog += $"[MTU] 開始探測 {PingOptions.TargetHost} 最佳路徑 MTU...\r\n";
        try
        {
            var (mtu, summary) = await _cliToolsService.DetectPathMtuAsync(PingOptions.TargetHost, line =>
            {
                DispatcherHelper.UIDispatcher?.TryEnqueue(() =>
                {
                    PingOutputLog += line + "\r\n";
                });
            });
            DetectedMtu = mtu;
        }
        finally
        {
            IsDetectingMtu = false;
        }
    }

    [RelayCommand]
    public async Task RefreshNetstatAsync()
    {
        IsLoadingNetstat = true;
        try
        {
            var list = await _cliToolsService.GetActiveConnectionsAsync(FilterPort);
            NetstatConnections.Clear();
            foreach (var c in list) NetstatConnections.Add(c);
            HasScannedNetstat = true;

            if (NetstatConnections.Count == 0 && FilterPort.HasValue)
            {
                NetstatEmptyStatus = $"✅ 連接埠 {FilterPort.Value} 目前閒置可用，無任何背景程式佔用！";
                StatusMessage = NetstatEmptyStatus;
            }
            else if (NetstatConnections.Count == 0)
            {
                NetstatEmptyStatus = "目前沒有符合條件的網路連線。";
                StatusMessage = NetstatEmptyStatus;
            }
            else
            {
                NetstatEmptyStatus = "";
                StatusMessage = $"連線清單已更新：共 {NetstatConnections.Count} 條記錄";
            }
        }
        finally
        {
            IsLoadingNetstat = false;
        }
    }

    [RelayCommand]
    public async Task KillPortOccupantAsync(int? port)
    {
        int p = port ?? FilterPort ?? 0;
        if (p <= 0) return;

        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "強制終止通訊埠佔用程序 (Kill Port Occupant)",
            $"即將強制終止目前佔用 Port {p} 的處理程序。請確認該程序非系統核心服務！",
            $"Port {p}");
        if (!confirmed) return;

        var (ok, msg) = await _cliToolsService.KillProcessHoldingPortAsync(p);
        StatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess();
        await RefreshNetstatAsync();
    }

    [RelayCommand]
    public async Task KillProcessPidAsync(int pid)
    {
        if (pid <= 0) return;

        var confirmed = await DialogHelper.ConfirmDestructiveOperationAsync(
            WindowHelper.GetXamlRoot(),
            "強制終止網路處理程序 (Terminate Process by PID)",
            $"即將強制終止 PID = {pid} 的處理程序，未儲存資料可能遺失。",
            $"PID {pid}");
        if (!confirmed) return;

        var (ok, msg) = await _cliToolsService.KillProcessByPidAsync(pid);
        StatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess();
        await RefreshNetstatAsync();
    }

    [RelayCommand]
    public async Task ApplyPortPresetAsync(int port)
    {
        FilterPort = port;
        await RefreshNetstatAsync();
    }

    [RelayCommand]
    public async Task TestRemotePortAsync()
    {
        if (string.IsNullOrWhiteSpace(RemoteTestHost) || RemoteTestPort <= 0)
        {
            RemotePortTestResult = "請輸入有效的主機名稱/IP 與連接埠。";
            return;
        }

        try
        {
            IsTestingRemotePort = true;
            RemotePortTestResult = $"正在探測 {RemoteTestHost}:{RemoteTestPort} 連通性...";
            var (success, latency, msg) = await _cliToolsService.TestRemotePortAsync(RemoteTestHost, RemoteTestPort);
            RemotePortTestResult = msg;
            StatusMessage = msg;
            if (success) AudioFeedbackService.PlaySuccess();
        }
        catch (Exception ex)
        {
            RemotePortTestResult = $"❌ 測試發生異常: {ex.Message}";
        }
        finally
        {
            IsTestingRemotePort = false;
        }
    }

    [RelayCommand]
    public async Task InstallTelnetClientAsync()
    {
        try
        {
            IsInstallingTelnet = true;
            TelnetInstallStatus = "正在透過 DISM 啟用 Windows 內建 Telnet 用戶端功能...";
            var (ok, msg) = await _cliToolsService.InstallTelnetClientAsync();
            TelnetInstallStatus = msg;
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
        }
        catch (Exception ex)
        {
            TelnetInstallStatus = $"❌ 安裝失敗: {ex.Message}";
        }
        finally
        {
            IsInstallingTelnet = false;
        }
    }

    [RelayCommand]
    public async Task RefreshOpenFilesAsync()
    {
        IsLoadingOpenFiles = true;
        try
        {
            var list = await _cliToolsService.GetOpenSharedFilesAsync();
            OpenFiles.Clear();
            foreach (var f in list) OpenFiles.Add(f);
            StatusMessage = $"遠端鎖定檔案清單已更新：共 {OpenFiles.Count} 筆";
        }
        finally
        {
            IsLoadingOpenFiles = false;
        }
    }

    [RelayCommand]
    public async Task DisconnectOpenFileAsync(OpenSharedFileItem? item)
    {
        if (item == null) return;
        var (ok, msg) = await _cliToolsService.DisconnectOpenFileAsync(item.Id);
        StatusMessage = msg;
        await RefreshOpenFilesAsync();
    }

    [RelayCommand]
    public async Task FlushNetBiosAsync()
    {
        var (ok, msg) = await _cliToolsService.FlushAndRefreshNetBiosCacheAsync();
        StatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess();
    }

    // ══════════════════════════════════════════════════════════
    //  8. DNS Pollution Detection & Anti-Pollution Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task TestPollutionAsync()
    {
        if (IsTestingPollution) return;
        IsTestingPollution = true;
        PollutionResults.Clear();
        try
        {
            var domain = string.IsNullOrWhiteSpace(TestPollutionDomain) ? "github.com" : TestPollutionDomain.Trim();
            var res = await _dnsPollutionService.CheckDomainPollutionAsync(domain);
            PollutionResults.Add(res);

            // Also test default major domains if none specified
            if (string.IsNullOrWhiteSpace(TestPollutionDomain))
            {
                foreach (var d in DnsPollutionService.DefaultTestDomains.Skip(1))
                {
                    var r = await _dnsPollutionService.CheckDomainPollutionAsync(d);
                    PollutionResults.Add(r);
                }
            }

            StatusMessage = "DNS 污染檢測分析完成！";
        }
        finally
        {
            IsTestingPollution = false;
        }
    }

    [RelayCommand]
    public async Task ConfigureWindowsDoHAsync()
    {
        var (ok, msg) = await _dnsPollutionService.ConfigureWindowsDoHAsync();
        StatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
    }

    [RelayCommand]
    public async Task BrowseShareFolderAsync()
    {
        var folder = await FilePickerHelper.PickFolderAsync("選擇要共用的資料夾");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            LoadShareFolderTarget(folder);
        }
    }

    public void LoadShareFolderTarget(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        NewSharePath = folderPath;
        var leaf = System.IO.Path.GetFileName(folderPath.TrimEnd('\\', '/'));
        if (!string.IsNullOrWhiteSpace(leaf))
        {
            NewShareName = leaf.Replace(" ", "_");
        }
        NetShareStatusMessage = $"已選取共用資料夾：{folderPath}";
        StatusMessage = NetShareStatusMessage;
    }

    // ══════════════════════════════════════════════════════════
    //  9. Global TCP Auto-Tuning & Stack CLI Commands
    //     (netsh int tcp set global autotuninglevel=...)
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task RefreshTcpGlobalAsync()
    {
        try
        {
            var settings = await _tcpOptimizerService.GetTcpSettingsAsync();
            RunOnUIThreadIfActive(() =>
            {
                if (!string.IsNullOrWhiteSpace(settings.AutoTuningLevel))
                {
                    TcpAutoTuningLevel = settings.AutoTuningLevel.Trim().ToLowerInvariant();
                }
                TcpCongestionProvider = string.IsNullOrWhiteSpace(settings.CongestionProvider) ? "default" : settings.CongestionProvider;
                TcpEcnState = string.IsNullOrWhiteSpace(settings.EcnCapability) ? "default" : settings.EcnCapability;
                TcpRssState = string.IsNullOrWhiteSpace(settings.Rss) ? "default" : settings.Rss;
                TcpRscState = string.IsNullOrWhiteSpace(settings.Rsc) ? "default" : settings.Rsc;
                TcpTimestampsState = string.IsNullOrWhiteSpace(settings.Timestamps) ? "default" : settings.Timestamps;
                TcpGlobalRawOutput = string.IsNullOrWhiteSpace(settings.RawGlobalOutput)
                    ? "(netsh int tcp show global 無輸出)"
                    : settings.RawGlobalOutput.Trim();
                TcpStatusMessage = $"目前狀態：AutoTuning={TcpAutoTuningLevel} | 擁塞演算法={TcpCongestionProvider} | ECN={TcpEcnState} | RSS={TcpRssState} | RSC={TcpRscState}";
            });
        }
        catch (Exception ex)
        {
            TcpStatusMessage = $"讀取全域 TCP 狀態失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ApplyTcpAutoTuningLevelAsync(string? level = null)
    {
        if (IsApplyingTcpGlobal) return;
        IsApplyingTcpGlobal = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(level))
            {
                TcpAutoTuningLevel = level.Trim().ToLowerInvariant();
            }

            var targetLevel = string.IsNullOrWhiteSpace(TcpAutoTuningLevel) ? "normal" : TcpAutoTuningLevel.Trim().ToLowerInvariant();
            TcpStatusMessage = $"正在執行：netsh int tcp set global autotuninglevel={targetLevel} ...";
            var (ok, msg) = await _tcpOptimizerService.SetAutoTuningLevelAsync(targetLevel);
            TcpStatusMessage = msg;
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await RefreshTcpGlobalAsync();
        }
        finally
        {
            IsApplyingTcpGlobal = false;
        }
    }

    [RelayCommand]
    public async Task ApplyTcpExtremePresetAsync()
    {
        if (IsApplyingTcpGlobal) return;
        IsApplyingTcpGlobal = true;
        try
        {
            var (ok, msg) = await _tcpOptimizerService.ApplyExtremeThroughputPresetAsync(
                string.Equals(TcpAutoTuningLevel, "experimental", StringComparison.OrdinalIgnoreCase));
            TcpStatusMessage = msg;
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await RefreshTcpGlobalAsync();
        }
        finally
        {
            IsApplyingTcpGlobal = false;
        }
    }

    [RelayCommand]
    public async Task RestoreTcpDefaultPresetAsync()
    {
        if (IsApplyingTcpGlobal) return;
        IsApplyingTcpGlobal = true;
        try
        {
            var (ok, msg) = await _tcpOptimizerService.RestoreWindowsDefaultsAsync();
            TcpStatusMessage = msg;
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
            await RefreshTcpGlobalAsync();
        }
        finally
        {
            IsApplyingTcpGlobal = false;
        }
    }

    // ── L2TP/IPsec VPN NAT-T Fix (AssumeUDPEncapsulationContextOnSendRule = 2) ──
    private readonly SystemOptimizerService _vpnOptimizerService = new();
    [ObservableProperty] private int _l2tpVpnNatTValue;
    [ObservableProperty] private string _l2tpVpnNatTStatusText = "尚未檢測";

    [RelayCommand]
    public void RefreshL2tpVpnNatTStatus()
    {
        try
        {
            int val = _vpnOptimizerService.GetL2tpVpnNatRuleValue();
            L2tpVpnNatTValue = val;
            L2tpVpnNatTStatusText = val == 2
                ? "🟢 已修復 (AssumeUDPEncapsulationContextOnSendRule = 2，支援 NAT 後方 L2TP/IPsec 伺服器與客戶端)"
                : $"⚠️ 未套用 (目前數值: {val}，NAT 後方 L2TP VPN 連線容易卡在錯誤 789 / 809)";
        }
        catch (Exception ex)
        {
            L2tpVpnNatTStatusText = $"讀取失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ApplyL2tpVpnNatTFixAsync()
    {
        await _vpnOptimizerService.CreatePreTweakBackupAsync("Before_L2tpVpnNatFix");
        bool ok = _vpnOptimizerService.SetL2tpVpnNatFix(true);
        StatusMessage = ok
            ? "✅ 已寫入 HKLM\\SYSTEM\\CurrentControlSet\\Services\\PolicyAgent\\AssumeUDPEncapsulationContextOnSendRule = 2 (REG_DWORD)！重新開機後生效。"
            : "❌ 寫入 PolicyAgent 登錄檔失敗，請確認是否以系統管理員身分執行。";
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        RefreshL2tpVpnNatTStatus();
    }

    [RelayCommand]
    public async Task RestoreL2tpVpnNatTDefaultAsync()
    {
        await _vpnOptimizerService.CreatePreTweakBackupAsync("Before_L2tpVpnNatRestore");
        bool ok = _vpnOptimizerService.SetL2tpVpnNatFix(false);
        StatusMessage = ok
            ? "🔄 已移除 AssumeUDPEncapsulationContextOnSendRule 鍵值，還原為 Windows 預設。"
            : "❌ 還原 PolicyAgent 登錄檔失敗，請確認是否以系統管理員身分執行。";
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
        RefreshL2tpVpnNatTStatus();
    }

    [RelayCommand]
    public void CopyTcpAutoTuningCmd()
    {
        try
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(TcpGeneratedCommand);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            TcpStatusMessage = $"已複製指令：{TcpGeneratedCommand}";
            StatusMessage = TcpStatusMessage;
            AudioFeedbackService.PlaySuccess();
        }
        catch (Exception ex)
        {
            TcpStatusMessage = $"複製失敗：{ex.Message}";
        }
    }
}
