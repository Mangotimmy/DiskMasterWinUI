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

    // ── 4. Hosts File Editor ──
    public ObservableCollection<HostsEntryItem> HostsEntries { get; } = new();
    [ObservableProperty] private string _rawHostsText = "";
    [ObservableProperty] private bool _isLoadingHosts;
    [ObservableProperty] private string _newHostIp = "127.0.0.1";
    [ObservableProperty] private string _newHostDomain = "";
    [ObservableProperty] private string _newHostComment = "";

    // ── 5. NetShare & File Sharing Wizard ──
    private readonly NetShareService _netShareService = new();
    public ObservableCollection<NetShareItem> NetShares { get; } = new();
    [ObservableProperty] private string _newShareName = "SharedFolder";
    [ObservableProperty] private string _newSharePath = @"C:\Share";
    [ObservableProperty] private string _newShareRemark = "DiskMaster LAN Share";
    [ObservableProperty] private string _dedicatedUserName = "ShareUser";
    [ObservableProperty] private string _dedicatedUserPassword = "Password123!";
    [ObservableProperty] private bool _isMsAccountPolicyFixed;
    [ObservableProperty] private string _netShareStatusMessage = "";

    // ── 6. Remote Desktop (RDP Server) ──
    private readonly RdpServerService _rdpService = new();
    [ObservableProperty] private RdpStatusInfo _rdpStatus = new();
    public ObservableCollection<RdpSessionItem> RdpSessions { get; } = new();
    [ObservableProperty] private string _rdpStatusMessage = "";

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

    private bool _isInitialized;
    private bool _isLoadingData;

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
        if (!forceReload && (_isInitialized || _isLoadingData)) return;
        _isLoadingData = true;
        IsLoading = true;
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

                DispatcherHelper.RunOnUIThread(() =>
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

            await Task.WhenAll(nicTask, upnpTask, hostsTask, sharesTask, rdpTask);
            _isInitialized = true;
            StatusMessage = LocalizationService.T("網路工具資料已同步完成", "网络工具数据已同步完成", "Network tools data synchronized", "ネットワークツールの同期完了");
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Error("NetworkTools background initialization failed", ex, "NetworkTools");
        }
        finally
        {
            _isLoadingData = false;
            IsLoading = false;
            sw.Stop();
            DebugLogService.Instance.Info($"NetworkTools background initialization completed in {sw.ElapsedMilliseconds}ms", "NetworkTools");
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
                DispatcherHelper.RunOnUIThread(() => PortMappings.Clear());
                StatusMessage = "路由器未啟用 UPnP 或無回應。";
                return;
            }

            var (success, list, err) = await _upnpService.GetPortMappingsAsync();
            DispatcherHelper.RunOnUIThread(() =>
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
        try
        {
            IsApplyingDns = true;
            string targetName = SelectedAdapter?.Name ?? "";
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
    //  Hosts Commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task RefreshHostsAsync()
    {
        try
        {
            IsLoadingHosts = true;
            RawHostsText = await _hostsService.ReadRawHostsAsync();
            var entries = await _hostsService.ReadHostsAsync();
            DispatcherHelper.RunOnUIThread(() =>
            {
                HostsEntries.Clear();
                foreach (var e in entries) HostsEntries.Add(e);
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
        try
        {
            IsLoadingHosts = true;
            var (ok, msg) = await _hostsService.SaveRawHostsAsync(RawHostsText);
            StatusMessage = msg;
            if (ok) AudioFeedbackService.PlaySuccess();
            else AudioFeedbackService.PlayError();
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
    public async Task RefreshSharesAsync()
    {
        try
        {
            var shares = await _netShareService.GetActiveSharesAsync();
            DispatcherHelper.RunOnUIThread(() =>
            {
                NetShares.Clear();
                foreach (var s in shares) NetShares.Add(s);
            });
            IsMsAccountPolicyFixed = await _netShareService.IsMicrosoftAccountSharingPolicyFixedAsync();
            NetShareStatusMessage = $"已列出 {NetShares.Count} 個共用資源";
        }
        catch (Exception ex)
        {
            NetShareStatusMessage = $"讀取共用失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task CreateShareAsync()
    {
        var (ok, msg, uncIp, uncHost) = await _netShareService.CreateShareAsync(NewShareName, NewSharePath, "FULL", NewShareRemark);
        NetShareStatusMessage = msg;
        if (ok)
        {
            AudioFeedbackService.PlaySuccess();
            await RefreshSharesAsync();
        }
        else
        {
            AudioFeedbackService.PlayError();
        }
    }

    [RelayCommand]
    public async Task DeleteShareAsync(NetShareItem? item)
    {
        if (item == null) return;
        var (ok, msg) = await _netShareService.DeleteShareAsync(item.ShareName);
        NetShareStatusMessage = msg;
        if (ok) await RefreshSharesAsync();
    }

    [RelayCommand]
    public void FixMsAccountPolicy()
    {
        var (ok, msg) = _netShareService.FixMicrosoftAccountSharingPolicy();
        IsMsAccountPolicyFixed = _netShareService.IsMicrosoftAccountSharingPolicyFixed();
        NetShareStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
    }

    [RelayCommand]
    public async Task SetPrivateNetworkAsync()
    {
        var (ok, msg) = await _netShareService.SetNetworkProfilePrivateAsync();
        NetShareStatusMessage = msg;
    }

    [RelayCommand]
    public async Task EnableSharingFirewallAsync()
    {
        var (ok, msg) = await _netShareService.EnableFileSharingFirewallAsync();
        NetShareStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess();
    }

    [RelayCommand]
    public async Task CreateDedicatedUserAsync()
    {
        var (ok, msg) = await _netShareService.CreateDedicatedShareUserAsync(DedicatedUserName, DedicatedUserPassword);
        NetShareStatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess(); else AudioFeedbackService.PlayError();
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
            DispatcherHelper.RunOnUIThread(() =>
            {
                RdpSessions.Clear();
                foreach (var s in sessions) RdpSessions.Add(s);
            });
            RdpStatusMessage = $"RDP 狀態已更新 ({RdpStatus.EditionName})";
        }
        catch (Exception ex)
        {
            RdpStatusMessage = $"讀取 RDP 狀態失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task EnableRdpAsync()
    {
        RdpStatusMessage = "正在啟用遠端桌面並設定防火牆與服務...";
        var (ok, msg) = await _rdpService.EnableRdpServerAsync(RdpStatus.IsHomeEdition);
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
        _rdpService.LaunchLoopbackTest();
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
        var (ok, msg) = await _cliToolsService.KillProcessHoldingPortAsync(p);
        StatusMessage = msg;
        if (ok) AudioFeedbackService.PlaySuccess();
        await RefreshNetstatAsync();
    }

    [RelayCommand]
    public async Task KillProcessPidAsync(int pid)
    {
        if (pid <= 0) return;
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
}
