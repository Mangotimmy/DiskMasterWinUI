using System.Collections.ObjectModel;
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

    public NetworkToolsViewModel()
    {
        LocalIpAddress = _upnpService.GetLocalIpAddress();
        foreach (var preset in UpnpService.GetGamePresets())
        {
            GamePresets.Add(preset);
        }
        foreach (var dns in NetworkDiagnosticService.GetRecommendedDnsPresets())
        {
            DnsPresets.Add(dns);
        }
        SelectedDnsPreset = DnsPresets.FirstOrDefault();
        RefreshNetworkAdapters();
        _ = LoadInitialDataAsync();
    }

    [RelayCommand]
    public void RefreshNetworkAdapters()
    {
        NetworkAdapters.Clear();
        foreach (var nic in NetworkDiagnosticService.GetAvailableNetworkAdapters())
        {
            NetworkAdapters.Add(nic);
        }
        SelectedAdapter = NetworkAdapters.FirstOrDefault(a => a.IsPrimary) ?? NetworkAdapters.FirstOrDefault();
    }

    [RelayCommand]
    public async Task LoadInitialDataAsync()
    {
        await RefreshUpnpAsync();
        await RefreshHostsAsync();
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
            LocalIpAddress = _upnpService.GetLocalIpAddress();
            IsUpnpSupported = await _upnpService.IsUpnpSupportedAsync();

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
}
