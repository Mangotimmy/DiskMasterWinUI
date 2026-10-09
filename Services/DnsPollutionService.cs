using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// DNS Pollution & Tampering Detection with DoH Anti-Pollution Remedies.
/// Compares local UDP 53 resolution against authenticated DoH (Cloudflare / Google),
/// identifies Bogon spoofed IPs, and configures Windows native DoH encryption.
/// </summary>
public class DnsPollutionService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static readonly HashSet<string> BogonPoisonedIps = new(StringComparer.OrdinalIgnoreCase)
    {
        "10.10.34.34", "243.185.187.39", "46.82.174.68", "78.16.49.15", "93.46.8.89",
        "37.61.54.158", "59.24.3.173", "203.98.7.65", "8.7.198.45", "127.0.0.1",
        "0.0.0.0", "159.106.121.75", "1.1.1.13", "192.168.0.1"
    };

    public static readonly string[] DefaultTestDomains =
    [
        "github.com",
        "twitter.com",
        "wikipedia.org",
        "youtube.com",
        "openai.com"
    ];

    public async Task<DnsPollutionItem> CheckDomainPollutionAsync(string domain, CancellationToken ct = default)
    {
        var item = new DnsPollutionItem { Domain = domain };

        var localIps = new List<string>();
        var dohIps = new List<string>();

        // 1. Query Local DNS (UDP 53)
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(domain, ct);
            localIps = addresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                .Select(a => a.ToString())
                                .ToList();
            item.LocalResolvedIps = string.Join(", ", localIps);
        }
        catch (Exception ex)
        {
            item.LocalResolvedIps = $"Failed: {ex.Message}";
        }

        // 2. Query Cloudflare DoH (DNS over HTTPS)
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://cloudflare-dns.com/dns-query?name={domain}&type=A");
            req.Headers.Add("Accept", "application/dns-json");

            var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Answer", out var answerArray))
                {
                    foreach (var elem in answerArray.EnumerateArray())
                    {
                        if (elem.TryGetProperty("data", out var dataVal))
                        {
                            var ip = dataVal.GetString();
                            if (!string.IsNullOrEmpty(ip) && IPAddress.TryParse(ip, out _))
                            {
                                dohIps.Add(ip);
                            }
                        }
                    }
                }
            }
            item.DohResolvedIps = dohIps.Count > 0 ? string.Join(", ", dohIps) : "None";
        }
        catch
        {
            item.DohResolvedIps = "DoH Query Timeout";
        }

        // 3. Evaluate Pollution
        bool hasBogon = localIps.Any(ip => BogonPoisonedIps.Contains(ip));
        bool mismatch = (localIps.Count > 0 && dohIps.Count > 0 && !localIps.Any(lip => dohIps.Contains(lip)));

        if (hasBogon)
        {
            item.IsPolluted = true;
            item.StatusBadge = "🚨 BOGON 假 IP 污染";
            item.Details = $"本機 DNS 回傳已知投毒假 IP ({string.Join(", ", localIps.Where(BogonPoisonedIps.Contains))})！";
        }
        else if (mismatch)
        {
            item.IsPolluted = true;
            item.StatusBadge = "⚠️ 疑似劫持/污染";
            item.Details = $"本機解析 ({item.LocalResolvedIps}) 與權威 DoH ({item.DohResolvedIps}) 完全不符。";
        }
        else
        {
            item.IsPolluted = false;
            item.StatusBadge = "✅ 乾淨未受污染";
            item.Details = "本機解析與 DoH 驗證結果吻合或安全無虞。";
        }

        return item;
    }

    public async Task<(bool Success, string Message)> ConfigureWindowsDoHAsync()
    {
        // Configure Windows 10/11 DoH template for Cloudflare 1.1.1.1
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("netsh.exe", "dns add encryption server=1.1.1.1 dohtemplate=https://cloudflare-dns.com/dns-query autoupgrade=yes");
        await ProcessHelper.RunProcessAsync("netsh.exe", "dns add encryption server=1.0.0.1 dohtemplate=https://cloudflare-dns.com/dns-query autoupgrade=yes");
        return (code == 0, code == 0 ? "Windows 原生 DoH (DNS over HTTPS) 加密已成功設定至 1.1.1.1 / 1.0.0.1！" : (string.IsNullOrWhiteSpace(errStr) ? outStr : errStr));
    }

    public async Task<(bool Success, string Message)> FlushDnsCacheAsync()
    {
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("ipconfig.exe", "/flushdns");
        return (code == 0, code == 0 ? "DNS 本機解析快取已清除完成。" : errStr);
    }
}
