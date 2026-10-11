using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.Services;

public record ReleaseAsset(string Name, string DownloadUrl, long SizeBytes);

public record UpdateDownloadProgress(long BytesReceived, long TotalBytes, double Percent, string SpeedText);

public class UpdateCheckResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string SourceChannel { get; set; } = "";
    public bool HasUpdate { get; set; }
    public string LatestVersion { get; set; } = "";
    public string ReleaseUrl { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public List<ReleaseAsset> Assets { get; set; } = new();
    public string? MatchingAssetUrl { get; set; }
    public string? MatchingAssetName { get; set; }

    public void Deconstruct(out bool hasUpdate, out string latestVersion, out string releaseUrl, out string releaseNotes)
    {
        hasUpdate = HasUpdate;
        latestVersion = LatestVersion;
        releaseUrl = ReleaseUrl;
        releaseNotes = ReleaseNotes;
    }
}

/// <summary>
/// Multi-tiered resilient service for checking updates across VPN, Cloudflare WARP,
/// proxies and restricted networks, with download acceleration and automated execution.
/// </summary>
public class UpdateService
{
    public static UpdateService Instance { get; } = new();

    private const string RepoWebLatestUrl = "https://github.com/Mangotimmy/DiskMasterWinUI/releases/latest";
    private const string RepoApiUrl = "https://api.github.com/repos/Mangotimmy/DiskMasterWinUI/releases/latest";
    private const string CdnManifestUrl = "https://cdn.jsdelivr.net/gh/Mangotimmy/DiskMasterWinUI@master/version.json";
    private const string RawManifestUrl = "https://raw.githubusercontent.com/Mangotimmy/DiskMasterWinUI/master/version.json";
    
    public static string CurrentVersion
    {
        get
        {
            var ver = typeof(UpdateService).Assembly.GetName().Version;
            return ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v1.5.1";
        }
    }

    /// <summary>
    /// Creates an HttpClient configured according to user Proxy settings and optimized for VPN / Cloudflare.
    /// </summary>
    private static HttpClient CreateHttpClient(bool allowRedirect = true, int timeoutSeconds = 25)
    {
        var settings = SettingsService.Instance.Current;
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = allowRedirect,
            AutomaticDecompression = DecompressionMethods.All,
            CheckCertificateRevocationList = false // Avoid hangs under VPN / SSL inspection
        };

        if (settings.ProxyMode == "Custom" && !string.IsNullOrWhiteSpace(settings.CustomProxyUrl))
        {
            try
            {
                handler.Proxy = new WebProxy(settings.CustomProxyUrl);
                handler.UseProxy = true;
            }
            catch
            {
                handler.UseProxy = true;
            }
        }
        else if (settings.ProxyMode == "Direct")
        {
            handler.UseProxy = false;
        }
        else
        {
            handler.UseProxy = true;
            handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "DiskMasterPro-App/1.5.1 (Windows NT 10.0; Win64; x64)");
        client.DefaultRequestHeaders.Add("Accept", "application/json, text/html, */*");
        return client;
    }

    /// <summary>
    /// Checks for newer versions using a multi-tiered pipeline:
    /// Tier 1: GitHub Web Redirect (Zero rate limits, works across Cloudflare / VPN)
    /// Tier 2: CDN Manifest (jsDelivr / Fastly / Raw)
    /// Tier 3: GitHub REST API
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        var result = new UpdateCheckResult
        {
            LatestVersion = CurrentVersion,
            ReleaseUrl = "https://github.com/Mangotimmy/DiskMasterWinUI/releases"
        };

        var errors = new List<string>();

        // Tier 1: GitHub Web Redirect (Bypasses API rate limit entirely)
        try
        {
            if (await CheckViaWebRedirectAsync(result))
            {
                result.IsSuccess = true;
                result.SourceChannel = "GitHub Web (零限流通道)";
                result.HasUpdate = CompareVersions(result.LatestVersion, CurrentVersion) > 0;
                await TryEnrichFromManifestAsync(result, result.LatestVersion);
                return result;
            }
        }
        catch (Exception ex)
        {
            errors.Add($"WebRedirect: {ex.Message}");
        }

        // Tier 2: CDN Manifest (jsDelivr / GitHub Raw)
        try
        {
            if (await CheckViaCdnManifestAsync(result))
            {
                result.IsSuccess = true;
                result.SourceChannel = "CDN Manifest (全球鏡像通道)";
                result.HasUpdate = CompareVersions(result.LatestVersion, CurrentVersion) > 0;
                return result;
            }
        }
        catch (Exception ex)
        {
            errors.Add($"CdnManifest: {ex.Message}");
        }

        // Tier 3: GitHub REST API (Subject to rate limits on shared VPN IPs)
        try
        {
            if (await CheckViaRestApiAsync(result))
            {
                result.IsSuccess = true;
                result.SourceChannel = "GitHub REST API";
                result.HasUpdate = CompareVersions(result.LatestVersion, CurrentVersion) > 0;
                return result;
            }
        }
        catch (Exception ex)
        {
            errors.Add($"RestApi: {ex.Message}");
        }

        // All tiers failed
        result.IsSuccess = false;
        result.HasUpdate = false;
        result.ErrorMessage = string.Join("; ", errors);
        return result;
    }

    /// <summary>
    /// Tier 1: Queries github.com/releases/latest with AllowAutoRedirect = false and inspects 302 Location header.
    /// </summary>
    private async Task<bool> CheckViaWebRedirectAsync(UpdateCheckResult result)
    {
        using var client = CreateHttpClient(allowRedirect: false, timeoutSeconds: 15);
        using var request = new HttpRequestMessage(HttpMethod.Get, RepoWebLatestUrl);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        string? locationUrl = null;
        if (response.Headers.Location != null)
        {
            locationUrl = response.Headers.Location.ToString();
        }
        else if (response.StatusCode == HttpStatusCode.Found ||
                 response.StatusCode == HttpStatusCode.MovedPermanently ||
                 response.StatusCode == HttpStatusCode.SeeOther ||
                 response.StatusCode == HttpStatusCode.TemporaryRedirect)
        {
            if (response.Headers.TryGetValues("Location", out var values))
            {
                locationUrl = values.FirstOrDefault();
            }
        }

        if (string.IsNullOrWhiteSpace(locationUrl))
        {
            // If redirect was followed or Location is missing, try reading full URL
            if (response.IsSuccessStatusCode && response.RequestMessage?.RequestUri != null)
            {
                locationUrl = response.RequestMessage.RequestUri.ToString();
            }
        }

        if (!string.IsNullOrWhiteSpace(locationUrl))
        {
            var match = Regex.Match(locationUrl, @"/releases/tag/([^/?#]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string tag = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    result.LatestVersion = tag;
                    result.ReleaseUrl = locationUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) 
                        ? locationUrl 
                        : $"https://github.com/Mangotimmy/DiskMasterWinUI/releases/tag/{tag}";

                    // Build standard matching asset
                    string preferred = AppEnvironmentHelper.PreferredAssetName;
                    result.MatchingAssetName = preferred;
                    result.MatchingAssetUrl = $"https://github.com/Mangotimmy/DiskMasterWinUI/releases/download/{tag}/{preferred}";
                    result.Assets.Clear();
                    result.Assets.Add(new ReleaseAsset(preferred, result.MatchingAssetUrl, 0));
                    result.Assets.Add(new ReleaseAsset("DiskMaster_x64.zip", $"https://github.com/Mangotimmy/DiskMasterWinUI/releases/download/{tag}/DiskMaster_x64.zip", 0));
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Tier 2: Queries static version.json from jsDelivr CDN and raw GitHub.
    /// </summary>
    private async Task<bool> CheckViaCdnManifestAsync(UpdateCheckResult result)
    {
        string[] urls = { CdnManifestUrl, RawManifestUrl };

        using var client = CreateHttpClient(allowRedirect: true, timeoutSeconds: 15);

        foreach (var url in urls)
        {
            try
            {
                var response = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(tagName) && root.TryGetProperty("version", out var verElem))
                {
                    tagName = "v" + verElem.GetString();
                }

                if (!string.IsNullOrWhiteSpace(tagName))
                {
                    result.LatestVersion = tagName;
                    result.ReleaseUrl = $"https://github.com/Mangotimmy/DiskMasterWinUI/releases/tag/{tagName}";
                    result.ReleaseNotes = root.TryGetProperty("release_notes", out var rnElem) ? rnElem.GetString() ?? "" : "";

                    if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                    {
                        result.Assets.Clear();
                        foreach (var item in assetsElem.EnumerateArray())
                        {
                            string name = item.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                            string dl = item.TryGetProperty("browser_download_url", out var dlProp) 
                                ? dlProp.GetString() ?? "" 
                                : item.TryGetProperty("download_url", out var d2) ? d2.GetString() ?? "" : "";
                            long sz = item.TryGetProperty("size", out var sProp) ? sProp.GetInt64() : 0L;

                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(dl))
                            {
                                result.Assets.Add(new ReleaseAsset(name, dl, sz));
                            }
                        }
                    }

                    MatchPreferredAsset(result);
                    return true;
                }
            }
            catch
            {
                // Try next CDN URL
            }
        }

        return false;
    }

    /// <summary>
    /// Tier 3: Queries GitHub REST API.
    /// </summary>
    private async Task<bool> CheckViaRestApiAsync(UpdateCheckResult result)
    {
        using var client = CreateHttpClient(allowRedirect: true, timeoutSeconds: 20);
        var response = await client.GetStringAsync(RepoApiUrl);
        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        string tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
        string htmlUrl = root.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() ?? "" : "https://github.com/Mangotimmy/DiskMasterWinUI/releases";
        string body = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(tagName)) return false;

        result.LatestVersion = tagName;
        result.ReleaseUrl = htmlUrl;
        result.ReleaseNotes = body;

        if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
        {
            result.Assets.Clear();
            foreach (var item in assetsElem.EnumerateArray())
            {
                string name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                string downloadUrl = item.TryGetProperty("browser_download_url", out var dlProp) ? dlProp.GetString() ?? "" : "";
                long size = item.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0L;

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(downloadUrl))
                {
                    result.Assets.Add(new ReleaseAsset(name, downloadUrl, size));
                }
            }
        }

        MatchPreferredAsset(result);
        return true;
    }

    /// <summary>
    /// Optionally enriches release notes and asset details from version.json if available.
    /// </summary>
    private async Task TryEnrichFromManifestAsync(UpdateCheckResult result, string tag)
    {
        try
        {
            using var client = CreateHttpClient(allowRedirect: true, timeoutSeconds: 5);
            var response = await client.GetStringAsync(CdnManifestUrl);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            string manifestTag = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
            if (string.Equals(manifestTag, tag, StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("release_notes", out var rnElem))
                {
                    result.ReleaseNotes = rnElem.GetString() ?? "";
                }

                if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                {
                    result.Assets.Clear();
                    foreach (var item in assetsElem.EnumerateArray())
                    {
                        string name = item.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                        string dl = item.TryGetProperty("browser_download_url", out var dlProp) ? dlProp.GetString() ?? "" : "";
                        long sz = item.TryGetProperty("size", out var sProp) ? sProp.GetInt64() : 0L;

                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(dl))
                        {
                            result.Assets.Add(new ReleaseAsset(name, dl, sz));
                        }
                    }
                    MatchPreferredAsset(result);
                }
            }
        }
        catch
        {
            // Non-critical enrichment failure ignored
        }
    }

    private static void MatchPreferredAsset(UpdateCheckResult result)
    {
        string preferredName = AppEnvironmentHelper.PreferredAssetName;
        var matchedAsset = result.Assets.FirstOrDefault(a => a.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
            ?? result.Assets.FirstOrDefault(a => a.Name.Contains(AppEnvironmentHelper.ArchitectureName, StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            ?? result.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (matchedAsset != null)
        {
            result.MatchingAssetName = matchedAsset.Name;
            result.MatchingAssetUrl = matchedAsset.DownloadUrl;
        }
        else
        {
            result.MatchingAssetName = preferredName;
            result.MatchingAssetUrl = $"https://github.com/Mangotimmy/DiskMasterWinUI/releases/download/{result.LatestVersion}/{preferredName}";
        }
    }

    /// <summary>
    /// Downloads a release asset asynchronously with automatic mirror fallback and real-time speed reporting.
    /// </summary>
    public async Task DownloadReleaseAssetAsync(
        string assetUrl,
        string destinationFilePath,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        string dir = Path.GetDirectoryName(destinationFilePath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPartPath = destinationFilePath + ".part";
        if (File.Exists(tempPartPath))
        {
            try { File.Delete(tempPartPath); } catch { }
        }

        // Candidate URLs: Direct GitHub, then Fast Mirrors for VPN / Firewall bypass
        var candidateUrls = new List<string> { assetUrl };
        if (assetUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            candidateUrls.Add($"https://ghproxy.net/{assetUrl}");
            candidateUrls.Add($"https://mirror.ghproxy.com/{assetUrl}");
        }

        Exception? lastException = null;

        foreach (var url in candidateUrls)
        {
            try
            {
                using var client = CreateHttpClient(allowRedirect: true, timeoutSeconds: 60);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? -1L;
                long totalRead = 0L;
                byte[] buffer = new byte[81920]; // 80 KB buffer

                var stopwatch = Stopwatch.StartNew();
                long lastReportTicks = 0;

                await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
                await using (var fileStream = new FileStream(tempPartPath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
                {
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        totalRead += bytesRead;

                        long nowTicks = stopwatch.ElapsedMilliseconds;
                        if (nowTicks - lastReportTicks > 200 || (totalBytes > 0 && totalRead == totalBytes))
                        {
                            lastReportTicks = nowTicks;
                            double percent = totalBytes > 0 ? (double)totalRead / totalBytes * 100.0 : 0.0;
                            double elapsedSec = Math.Max(0.1, stopwatch.Elapsed.TotalSeconds);
                            double speedBytesPerSec = totalRead / elapsedSec;
                            string speedText = speedBytesPerSec switch
                            {
                                >= 1048576 => $"{speedBytesPerSec / 1048576.0:F1} MB/s",
                                >= 1024 => $"{speedBytesPerSec / 1024.0:F0} KB/s",
                                _ => $"{speedBytesPerSec:F0} B/s"
                            };

                            progress?.Report(new UpdateDownloadProgress(totalRead, totalBytes, percent, speedText));
                        }
                    }
                }

                if (File.Exists(destinationFilePath))
                {
                    try { File.Delete(destinationFilePath); } catch { }
                }
                File.Move(tempPartPath, destinationFilePath);
                return; // Download succeeded
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (File.Exists(tempPartPath))
                {
                    try { File.Delete(tempPartPath); } catch { }
                }
            }
        }

        throw lastException ?? new HttpRequestException("無法從主要來源或任何加速鏡像下載更新檔。");
    }

    /// <summary>
    /// Launches the downloaded installer or new portable executable and exits the current process.
    /// </summary>
    public static void ApplyUpdateAndRestart(string downloadedFilePath)
    {
        if (!File.Exists(downloadedFilePath)) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = downloadedFilePath,
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch updated binary: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the release page in the default web browser.
    /// </summary>
    public static void OpenReleaseUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(url) ? "https://github.com/Mangotimmy/DiskMasterWinUI/releases" : url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static int CompareVersions(string v1, string v2)
    {
        try
        {
            var clean1 = v1.TrimStart('v', 'V');
            var clean2 = v2.TrimStart('v', 'V');

            // Strip build suffixes like -preview, -beta
            int dash1 = clean1.IndexOf('-');
            if (dash1 >= 0) clean1 = clean1.Substring(0, dash1);
            int dash2 = clean2.IndexOf('-');
            if (dash2 >= 0) clean2 = clean2.Substring(0, dash2);

            var ver1 = Version.Parse(clean1);
            var ver2 = Version.Parse(clean2);
            return ver1.CompareTo(ver2);
        }
        catch
        {
            return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
        }
    }
}
