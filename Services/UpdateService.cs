using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service for checking updates against GitHub releases repository.
/// </summary>
public class UpdateService
{
    public static UpdateService Instance { get; } = new();

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private const string RepoApiUrl = "https://api.github.com/repos/Mangotimmy/DiskMasterWinUI/releases/latest";
    
    public static string CurrentVersion
    {
        get
        {
            var ver = typeof(UpdateService).Assembly.GetName().Version;
            return ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v1.2.2";
        }
    }

    static UpdateService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "DiskMasterPro-App");
    }

    /// <summary>
    /// Checks GitHub releases API for newer versions.
    /// </summary>
    public async Task<(bool HasUpdate, string LatestVersion, string ReleaseUrl, string ReleaseNotes)> CheckForUpdatesAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync(RepoApiUrl);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() ?? "" : "https://github.com/Mangotimmy/DiskMasterWinUI/releases";
            string body = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";

            if (!string.IsNullOrWhiteSpace(tagName))
            {
                bool isNewer = CompareVersions(tagName, CurrentVersion) > 0;
                return (isNewer, tagName, htmlUrl, body);
            }
        }
        catch (Exception ex)
        {
            return (false, CurrentVersion, "https://github.com/Mangotimmy/DiskMasterWinUI/releases", $"Update check failed: {ex.Message}");
        }

        return (false, CurrentVersion, "https://github.com/Mangotimmy/DiskMasterWinUI/releases", "Already up to date.");
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
