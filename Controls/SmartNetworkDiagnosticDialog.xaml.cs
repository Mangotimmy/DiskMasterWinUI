using System.Net;
using System.Net.NetworkInformation;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DiskMasterWinUI.Controls;

public sealed partial class SmartNetworkDiagnosticDialog : ContentDialog
{
    public SmartNetworkDiagnosticDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RunDiagnosticPipelineAsync();
    }

    private class DiagStep
    {
        public string Title { get; set; } = "";
        public string Status { get; set; } = "⏳ 檢測中...";
        public bool IsPass { get; set; }
        public bool IsWarning { get; set; }
        public string Details { get; set; } = "";
    }

    private readonly List<DiagStep> _steps = new();

    public async Task RunDiagnosticPipelineAsync()
    {
        StepsContainer.Children.Clear();
        _steps.Clear();
        SummaryText.Text = "⏳ 正在逐步執行 8 大網路診斷管線，請稍候...";
        IsPrimaryButtonEnabled = false;

        // Step 1: Physical Adapter
        var s1 = new DiagStep { Title = "1. 實體網路卡與 IP 狀態" };
        var nic = NetworkDiagnosticService.GetPrimaryPhysicalInternetAdapter();
        if (nic != null && nic.OperationalStatus == OperationalStatus.Up)
        {
            var ip = nic.GetIPProperties().UnicastAddresses
                .FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString();
            s1.IsPass = true;
            s1.Status = $"🟢 正常 ({nic.Name}: {ip})";
        }
        else
        {
            s1.IsPass = false;
            s1.Status = "🔴 未偵測到連線中的實體網路卡";
        }
        AddStepToUi(s1);

        // Step 2: Gateway Ping
        var s2 = new DiagStep { Title = "2. 區域網路預設閘道回應" };
        string gwIp = "";
        if (nic != null)
        {
            gwIp = nic.GetIPProperties().GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString() ?? "";
        }
        if (!string.IsNullOrEmpty(gwIp))
        {
            var rtt = NetworkDiagnosticService.PingHost(gwIp);
            if (rtt >= 0)
            {
                s2.IsPass = true;
                s2.Status = $"🟢 正常回應 (閘道 {gwIp}, 延遲 {rtt} ms)";
            }
            else
            {
                s2.IsPass = false;
                s2.Status = $"🔴 預設閘道 ({gwIp}) 無法 ping 通或逾時";
            }
        }
        else
        {
            s2.IsPass = false;
            s2.Status = "🔴 無法取得有效預設閘道 IP";
        }
        AddStepToUi(s2);

        // Step 3: Public DNS Query
        var s3 = new DiagStep { Title = "3. DNS 網域名稱解析" };
        try
        {
            var ips = await Dns.GetHostAddressesAsync("www.microsoft.com");
            if (ips.Length > 0)
            {
                s3.IsPass = true;
                s3.Status = $"🟢 正常解析 (microsoft.com -> {ips[0]})";
            }
            else
            {
                s3.IsPass = false;
                s3.Status = "🔴 DNS 無法解析任何 IP";
            }
        }
        catch (Exception ex)
        {
            s3.IsPass = false;
            s3.Status = $"🔴 DNS 查詢失敗: {ex.Message}";
        }
        AddStepToUi(s3);

        // Step 4: Public Internet ICMP
        var s4 = new DiagStep { Title = "4. 公網 ICMP 封包連線" };
        var pubRtt = NetworkDiagnosticService.PingHost("8.8.8.8");
        if (pubRtt >= 0)
        {
            s4.IsPass = true;
            s4.Status = $"🟢 公網正常 (8.8.8.8, 延遲 {pubRtt} ms)";
        }
        else
        {
            s4.IsWarning = true;
            s4.Status = "🟡 8.8.8.8 無回應 (可能被防火牆或 ISP 阻擋 ICMP)";
        }
        AddStepToUi(s4);

        // Step 5: Web HTTP/TLS
        var s5 = new DiagStep { Title = "5. 網際網路 Web / TLS 握手" };
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var resp = await http.GetAsync("https://www.msftconnecttest.com/connecttest.txt");
            if (resp.IsSuccessStatusCode)
            {
                s5.IsPass = true;
                s5.Status = "🟢 Web 與 TLS 連線暢通";
            }
            else
            {
                s5.IsPass = false;
                s5.Status = $"🔴 HTTP 錯誤碼: {(int)resp.StatusCode}";
            }
        }
        catch
        {
            s5.IsPass = false;
            s5.Status = "🔴 Web 連線失敗 (可能遭遇 Proxy 或網路中斷)";
        }
        AddStepToUi(s5);

        // Step 6: System Proxy
        var s6 = new DiagStep { Title = "6. 系統代理伺服器 (Proxy) 設定" };
        try
        {
            using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            int proxyEnable = regKey?.GetValue("ProxyEnable") as int? ?? 0;
            string proxyServer = regKey?.GetValue("ProxyServer") as string ?? "";

            if (proxyEnable == 0 || string.IsNullOrWhiteSpace(proxyServer))
            {
                s6.IsPass = true;
                s6.Status = "🟢 直連模式 (無代理阻礙)";
            }
            else
            {
                s6.IsWarning = true;
                s6.Status = $"🟡 啟用代理中: {proxyServer}";
            }
        }
        catch
        {
            s6.IsPass = true;
            s6.Status = "🟢 直連模式 (預設)";
        }
        AddStepToUi(s6);

        // Step 7: MTU Path
        var s7 = new DiagStep { Title = "7. 最佳 MTU 路徑探測" };
        var cliService = new CliToolsService();
        var (mtu, _) = await cliService.DetectPathMtuAsync("1.1.1.1", _ => { });
        s7.IsPass = true;
        s7.Status = $"🟢 最佳路徑 MTU: {mtu} 位元組";
        AddStepToUi(s7);

        // Step 8: DNS Pollution
        var s8 = new DiagStep { Title = "8. DNS 污染與投毒分析" };
        var dnsService = new DnsPollutionService();
        var pollResult = await dnsService.CheckDomainPollutionAsync("github.com");
        if (pollResult.IsPolluted)
        {
            s8.IsPass = false;
            s8.Status = pollResult.StatusBadge;
        }
        else
        {
            s8.IsPass = true;
            s8.Status = "🟢 DNS 乾淨無投毒污染";
        }
        AddStepToUi(s8);

        // Final Summary
        bool allGood = _steps.All(s => s.IsPass || s.IsWarning);
        if (allGood)
        {
            SummaryText.Text = "🎉 恭喜！網路各項連線指標運作良好，未發現異常阻斷問題。";
        }
        else
        {
            SummaryText.Text = "⚠️ 偵測到網路存在障礙或解析異常，建議點擊下方「⚡ 一鍵智慧修復」進行全自動修復。";
        }

        IsPrimaryButtonEnabled = true;
    }

    private void AddStepToUi(DiagStep step)
    {
        _steps.Add(step);
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var titleBlock = new TextBlock
        {
            Text = step.Title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleBlock, 0);

        var statusBlock = new TextBlock
        {
            Text = step.Status,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(statusBlock, 1);

        grid.Children.Add(titleBlock);
        grid.Children.Add(statusBlock);
        StepsContainer.Children.Add(grid);
    }

    private async void SmartRepair_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // Keep dialog open while repairing
        IsPrimaryButtonEnabled = false;
        SummaryText.Text = "⚡ 正在執行一鍵全自動網路修復（清空 DNS、重設 Winsock、重設 IP 堆疊、清除 Proxy 假死）...";

        try
        {
            await ProcessHelper.RunProcessAsync("ipconfig.exe", "/flushdns");
            await ProcessHelper.RunProcessAsync("netsh.exe", "winsock reset");
            await ProcessHelper.RunProcessAsync("netsh.exe", "int ip reset");
            await ProcessHelper.RunProcessAsync("netsh.exe", "winhttp reset proxy");
            await ProcessHelper.RunProcessAsync("ipconfig.exe", "/renew");
            SummaryText.Text = "✅ 修復指令執行完成！正在重新驗證連線...";
            await RunDiagnosticPipelineAsync();
        }
        catch (Exception ex)
        {
            SummaryText.Text = $"修復時發生錯誤: {ex.Message}";
            IsPrimaryButtonEnabled = true;
        }
    }

    private async void RerunDiagnostic_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        await RunDiagnosticPipelineAsync();
    }
}
