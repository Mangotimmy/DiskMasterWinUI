using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.Services;

public record ReleaseAsset(string Name, string DownloadUrl, long SizeBytes);

public record UpdateDownloadProgress(long BytesReceived, long TotalBytes, double Percent, string SpeedText);

public class UpdateCheckResult
{
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
/// Service for checking updates against GitHub releases repository, streaming download,
/// and automated execution for portable and installer packages.
/// </summary>
public class UpdateService
{
    public static UpdateService Instance { get; } = new();

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private const string RepoApiUrl = "https://api.github.com/repos/Mangotimmy/DiskMasterWinUI/releases/latest";
    
    public static string CurrentVersion
    {
        get
        {
            var ver = typeof(UpdateService).Assembly.GetName().Version;
            return ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v1.3.0";
        }
    }

    static UpdateService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "DiskMasterPro-App");
    }

    /// <summary>
    /// Checks GitHub releases API for newer versions, parsing release assets for local architecture and mode.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        var result = new UpdateCheckResult
        {
            LatestVersion = CurrentVersion,
            ReleaseUrl = "https://github.com/Mangotimmy/DiskMasterWinUI/releases"
        };

        try
        {
            var response = await _httpClient.GetStringAsync(RepoApiUrl);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() ?? "" : "https://github.com/Mangotimmy/DiskMasterWinUI/releases";
            string body = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";

            result.LatestVersion = tagName;
            result.ReleaseUrl = htmlUrl;
            result.ReleaseNotes = body;

            // Parse Assets
            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
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

            // Match preferred asset based on AppEnvironmentHelper
            string preferredName = AppEnvironmentHelper.PreferredAssetName;
            var matchedAsset = result.Assets.FirstOrDefault(a => a.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                ?? result.Assets.FirstOrDefault(a => a.Name.Contains(AppEnvironmentHelper.ArchitectureName, StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ?? result.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (matchedAsset != null)
            {
                result.MatchingAssetName = matchedAsset.Name;
                result.MatchingAssetUrl = matchedAsset.DownloadUrl;
            }

            if (!string.IsNullOrWhiteSpace(tagName))
            {
                result.HasUpdate = CompareVersions(tagName, CurrentVersion) > 0;
            }
        }
        catch (Exception ex)
        {
            result.HasUpdate = false;
            result.ReleaseNotes = $"Update check failed: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Downloads a release asset asynchronously with real-time speed and progress reporting.
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

        using var response = await _httpClient.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
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
