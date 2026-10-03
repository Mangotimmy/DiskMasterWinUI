using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service for managing download temporary caches, partial chunks (.tmp_dm),
/// DISM stale mountpoints, and DiskPart script remnants to reclaim disk space.
/// </summary>
public class CacheCleanService
{
    public record CacheStats(long TotalSizeBytes, int FileCount, string SizeDisplay);

    /// <summary>
    /// Calculates total size of temporary download caches and .tmp_dm partial files.
    /// </summary>
    public Task<CacheStats> GetDownloadCacheStatsAsync(string? directory = null)
    {
        return Task.Run(() =>
        {
            var targetDir = directory ?? WindowsDownloadService.DefaultDownloadDirectory;
            if (!Directory.Exists(targetDir)) return new CacheStats(0, 0, "0 B");

            long totalBytes = 0;
            int count = 0;

            try
            {
                // Scan .tmp_dm directory
                var tmpDir = Path.Combine(targetDir, ".tmp_dm");
                if (Directory.Exists(tmpDir))
                {
                    foreach (var file in Directory.EnumerateFiles(tmpDir, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            totalBytes += fi.Length;
                            count++;
                        }
                        catch { }
                    }
                }

                // Scan .part files
                foreach (var file in Directory.EnumerateFiles(targetDir, "*.part", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        totalBytes += fi.Length;
                        count++;
                    }
                    catch { }
                }
            }
            catch { }

            return new CacheStats(totalBytes, count, FormatBytes(totalBytes));
        });
    }

    /// <summary>
    /// Cleans incomplete download chunks and .tmp_dm temporary directories.
    /// </summary>
    public Task<(int DeletedFiles, long FreedBytes, string FreedDisplay)> CleanDownloadCacheAsync(string? directory = null)
    {
        return Task.Run(() =>
        {
            var targetDir = directory ?? WindowsDownloadService.DefaultDownloadDirectory;
            if (!Directory.Exists(targetDir)) return (0, 0, "0 B");

            int deletedFiles = 0;
            long freedBytes = 0;

            try
            {
                var tmpDir = Path.Combine(targetDir, ".tmp_dm");
                if (Directory.Exists(tmpDir))
                {
                    foreach (var file in Directory.EnumerateFiles(tmpDir, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            freedBytes += fi.Length;
                            File.Delete(file);
                            deletedFiles++;
                        }
                        catch { }
                    }

                    try { Directory.Delete(tmpDir, true); } catch { }
                }

                foreach (var file in Directory.EnumerateFiles(targetDir, "*.part", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        freedBytes += fi.Length;
                        File.Delete(file);
                        deletedFiles++;
                    }
                    catch { }
                }
            }
            catch { }

            return (deletedFiles, freedBytes, FormatBytes(freedBytes));
        });
    }

    /// <summary>
    /// Cleans orphan DISM mountpoints and uncommitted WIM locks.
    /// </summary>
    public async Task<string> CleanDismMountpointsAsync()
    {
        var (out1, _, _) = await ProcessHelper.RunProcessAsync("dism.exe", "/Cleanup-Mountpoints");
        var (out2, _, _) = await ProcessHelper.RunProcessAsync("dism.exe", "/Cleanup-Wim");
        return $"{out1}\n{out2}".Trim();
    }

    /// <summary>
    /// Cleans DiskPart script temporary files (*.txt) created in %TEMP%.
    /// </summary>
    public Task<int> CleanDiskPartTempFilesAsync()
    {
        return Task.Run(() =>
        {
            int deleted = 0;
            try
            {
                var tempDir = Path.GetTempPath();
                foreach (var file in Directory.EnumerateFiles(tempDir, "dm_*.*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch { }
                }
            }
            catch { }
            return deleted;
        });
    }

    /// <summary>
    /// Performs a full one-click deep cleanup of all DiskMaster caches.
    /// </summary>
    public async Task<(string Summary, long FreedBytes)> PerformFullCleanupAsync(string? downloadDir = null)
    {
        var (files, bytes, freedDisplay) = await CleanDownloadCacheAsync(downloadDir);
        var scriptCount = await CleanDiskPartTempFilesAsync();
        var dismResult = await CleanDismMountpointsAsync();

        var summary = $"已成功清理 {files} 個下載暫存區塊 (釋放 {freedDisplay})，並移除了 {scriptCount} 個 DiskPart 臨時指令檔與 DISM 掛載鎖定點。";
        return (summary, bytes);
    }

    /// <summary>
    /// Performs deep cleaning of system temp files, prefetch, WER logs, dumps, and download caches.
    /// </summary>
    public async Task<(string Summary, long FreedBytes)> PerformSystemDeepCleanupAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        var tempService = new TempCleanService();
        var allCats = new[] { "UserTemp", "SystemTemp", "Prefetch", "MemoryDumps", "WerReports", "DeliveryOpt", "SetupFiles", "ThumbCache" };
        var (tFiles, tBytes, _) = await tempService.CleanSelectedCategoriesAsync(allCats, onOutput, ct);
        var (dFiles, dBytes, _) = await CleanDownloadCacheAsync();
        var scriptCount = await CleanDiskPartTempFilesAsync();
        var dismOut = await CleanDismMountpointsAsync();

        long totalBytes = tBytes + dBytes;
        var summary = $"全域系統暫存與快取清理完成！共釋放 {FormatBytes(totalBytes)} (清理暫存檔 {tFiles + dFiles:N0} 個，移除 {scriptCount} 個 DiskPart 腳本與 DISM 掛載鎖定點)。";
        return (summary, totalBytes);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F2} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }
}
