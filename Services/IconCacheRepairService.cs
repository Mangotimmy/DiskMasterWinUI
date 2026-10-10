using System.Diagnostics;
using System.IO;
using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service for rebuilding and repairing corrupted or blank Windows Explorer icon and thumbnail caches.
/// Cleanly terminates explorer.exe, removes IconCache.db and modern iconcache*/thumbcache* databases,
/// and restarts explorer.exe to trigger automatic OS-level icon index regeneration.
/// </summary>
public class IconCacheRepairService
{
    private static readonly Lazy<IconCacheRepairService> _instance = new(() => new IconCacheRepairService());
    public static IconCacheRepairService Instance => _instance.Value;

    public record IconRepairResult(bool Success, string Message, int DeletedFilesCount);

    /// <summary>
    /// Executes the full icon cache purge and explorer restart pipeline.
    /// </summary>
    public async Task<IconRepairResult> RebuildIconCacheAsync(IProgress<string>? progress = null)
    {
        return await Task.Run(async () =>
        {
            int deletedCount = 0;
            try
            {
                progress?.Report("正在強制關閉 Windows 檔案總管 (explorer.exe)...");
                DebugLogService.Instance.Info("Terminating explorer.exe...", "IconCacheRepair");

                // 1. Terminate explorer.exe
                try
                {
                    await ProcessHelper.RunProcessAsync("taskkill.exe", "/f /im explorer.exe");
                }
                catch { }

                // Brief pause to allow file handles to be released
                await Task.Delay(800);

                // 2. Clear legacy IconCache.db in %localappdata%
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var legacyIconDb = Path.Combine(localAppData, "IconCache.db");
                if (File.Exists(legacyIconDb))
                {
                    try
                    {
                        progress?.Report("正在清除舊版圖示快取檔 IconCache.db...");
                        File.SetAttributes(legacyIconDb, FileAttributes.Normal);
                        File.Delete(legacyIconDb);
                        deletedCount++;
                        DebugLogService.Instance.Info($"Deleted legacy cache: {legacyIconDb}", "IconCacheRepair");
                    }
                    catch (Exception ex)
                    {
                        DebugLogService.Instance.Error($"Failed to delete {legacyIconDb}", ex, "IconCacheRepair");
                    }
                }

                // 3. Clear modern iconcache* and thumbcache* in %localappdata%\Microsoft\Windows\Explorer\
                var explorerCacheDir = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");
                if (Directory.Exists(explorerCacheDir))
                {
                    progress?.Report("正在清除現代圖示與縮圖快取資料庫 (iconcache* / thumbcache*)...");
                    try
                    {
                        var cacheFiles = Directory.EnumerateFiles(explorerCacheDir, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(f =>
                            {
                                var name = Path.GetFileName(f);
                                return name.StartsWith("iconcache", StringComparison.OrdinalIgnoreCase) ||
                                       name.StartsWith("thumbcache", StringComparison.OrdinalIgnoreCase);
                            });

                        foreach (var f in cacheFiles)
                        {
                            try
                            {
                                File.SetAttributes(f, FileAttributes.Normal);
                                File.Delete(f);
                                deletedCount++;
                            }
                            catch (Exception ex)
                            {
                                DebugLogService.Instance.Debug($"Could not delete cache file {Path.GetFileName(f)}: {ex.Message}", "IconCacheRepair");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogService.Instance.Error("Error enumerating explorer cache dir", ex, "IconCacheRepair");
                    }
                }

                // 4. Restart explorer.exe
                progress?.Report("正在重新啟動 Windows 檔案總管與桌面程序...");
                DebugLogService.Instance.Info("Restarting explorer.exe...", "IconCacheRepair");
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    DebugLogService.Instance.Error("Failed to launch explorer.exe", ex, "IconCacheRepair");
                    // Fallback
                    try { await ProcessHelper.RunProcessAsync("cmd.exe", "/c start explorer.exe"); } catch { }
                }

                await Task.Delay(1000);
                progress?.Report("圖示快取重建完成！");

                return new IconRepairResult(
                    true,
                    $"✅ 圖示與縮圖快取已徹底清除並重建完成！(共清理 {deletedCount} 個快取檔案，檔案總管已自動重啟)",
                    deletedCount
                );
            }
            catch (Exception ex)
            {
                DebugLogService.Instance.Error("Rebuild error", ex, "IconCacheRepair");
                // Ensure explorer is running in case of exception
                try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }); } catch { }
                return new IconRepairResult(false, $"❌ 重建圖示快取過程發生錯誤: {ex.Message}", deletedCount);
            }
        });
    }
}
