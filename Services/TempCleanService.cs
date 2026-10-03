using System.IO;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service for analyzing, scanning, and safely cleaning system and user temporary files,
/// crash dumps, Windows Error Reporting logs, prefetch data, and Windows upgrade remnants.
/// </summary>
public class TempCleanService
{
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static readonly string UserTemp = Path.GetTempPath();

    /// <summary>
    /// Scans all standard temporary and cache locations asynchronously.
    /// </summary>
    public async Task<List<TempCategoryItem>> ScanTempCategoriesAsync()
    {
        return await Task.Run(() =>
        {
            var categories = new List<TempCategoryItem>
            {
                new()
                {
                    Id = "UserTemp",
                    Name = "使用者暫存檔案 (User Temp)",
                    Description = "目前登入使用者應用程式所產生的暫存與中繼快取檔案",
                    Path = UserTemp,
                    Icon = "👤"
                },
                new()
                {
                    Id = "SystemTemp",
                    Name = "Windows 系統暫存 (System Temp)",
                    Description = "Windows 系統服務與安裝程式暫存目錄 (C:\\Windows\\Temp)",
                    Path = Path.Combine(WinDir, "Temp"),
                    Icon = "🪟"
                },
                new()
                {
                    Id = "Prefetch",
                    Name = "預先擷取資料 (Prefetch)",
                    Description = "系統應用程式啟動歷史快取檔案，清理後可釋放空間並重建索引",
                    Path = Path.Combine(WinDir, "Prefetch"),
                    Icon = "⚡"
                },
                new()
                {
                    Id = "MemoryDumps",
                    Name = "系統損毀傾印與傾印檔案 (Memory Dumps)",
                    Description = "BSOD 藍屏記憶體傾印 (MEMORY.DMP) 與 Minidump 小型傾印紀錄",
                    Path = Path.Combine(WinDir, "Minidump"),
                    Icon = "💥"
                },
                new()
                {
                    Id = "WerReports",
                    Name = "Windows 錯誤回報佇列 (WER Logs)",
                    Description = "系統錯誤與當機回報佇列與封存資料庫 (ProgramData\\WER)",
                    Path = Path.Combine(ProgramData, "Microsoft", "Windows", "WER"),
                    Icon = "📋"
                },
                new()
                {
                    Id = "DeliveryOpt",
                    Name = "傳遞最佳化快取 (Delivery Optimization)",
                    Description = "Windows Update 區域網路傳遞快取檔案",
                    Path = Path.Combine(WinDir, "SoftwareDistribution", "DeliveryOptimization", "Cache"),
                    Icon = "🌐"
                },
                new()
                {
                    Id = "SetupFiles",
                    Name = "Windows 安裝與升級暫存 ($WINDOWS.~BT / Panther)",
                    Description = "功能更新、版本升級遺留之安裝下載暫存目錄",
                    Path = Path.Combine(Path.GetPathRoot(WinDir) ?? @"C:\", "$WINDOWS.~BT"),
                    Icon = "📦"
                },
                new()
                {
                    Id = "WindowsOld",
                    Name = "舊版 Windows 備份 (Windows.old)",
                    Description = "前次系統升級保留之回退目錄 (Windows.old)",
                    Path = Path.Combine(Path.GetPathRoot(WinDir) ?? @"C:\", "Windows.old"),
                    Icon = "💾"
                },
                new()
                {
                    Id = "ThumbCache",
                    Name = "檔案總管縮圖快取 (Thumbnail Cache)",
                    Description = "圖片與影片圖示縮圖資料庫 (thumbcache_*.db)",
                    Path = Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer"),
                    Icon = "🖼️"
                },
                new()
                {
                    Id = "DefenderCache",
                    Name = "Windows Defender 病毒碼備份 (Defender Cache)",
                    Description = "Windows Defender 病毒碼定義更新歷史備份檔案",
                    Path = Path.Combine(ProgramData, "Microsoft", "Windows Defender", "Definition Updates", "Backup"),
                    Icon = "🛡️"
                },
                new()
                {
                    Id = "PackageCache",
                    Name = "軟體安裝套件快取 (Package Cache)",
                    Description = "各類應用程式安裝快取備份 (ProgramData\\Package Cache)",
                    Path = Path.Combine(ProgramData, "Package Cache"),
                    Icon = "📦"
                },
                new()
                {
                    Id = "CbsLogs",
                    Name = "系統元件維護記錄檔 (CBS Logs)",
                    Description = "Windows 元件服務日誌 (C:\\Windows\\Logs\\CBS)",
                    Path = Path.Combine(WinDir, "Logs", "CBS"),
                    Icon = "📝"
                }
            };

            // Scan each category in parallel
            Parallel.ForEach(categories, cat =>
            {
                try
                {
                    long totalBytes = 0;
                    int count = 0;

                    if (cat.Id == "MemoryDumps")
                    {
                        // Check MEMORY.DMP at root
                        var fullDump = Path.Combine(WinDir, "MEMORY.DMP");
                        if (File.Exists(fullDump))
                        {
                            try
                            {
                                var fi = new FileInfo(fullDump);
                                totalBytes += fi.Length;
                                count++;
                            }
                            catch { }
                        }

                        // Check Minidump folder
                        if (Directory.Exists(cat.Path))
                        {
                            var (bytes, c) = ScanFolderSafe(cat.Path, "*.dmp");
                            totalBytes += bytes;
                            count += c;
                        }

                        // Check LocalAppData CrashDumps
                        var crashDir = Path.Combine(LocalAppData, "CrashDumps");
                        if (Directory.Exists(crashDir))
                        {
                            var (bytes, c) = ScanFolderSafe(crashDir, "*.*");
                            totalBytes += bytes;
                            count += c;
                        }
                    }
                    else if (cat.Id == "SetupFiles")
                    {
                        var root = Path.GetPathRoot(WinDir) ?? @"C:\";
                        string[] setupDirs = { "$WINDOWS.~BT", "$WINDOWS.~WS", "Windows\\Panther" };
                        foreach (var sub in setupDirs)
                        {
                            var target = Path.Combine(root, sub);
                            if (Directory.Exists(target))
                            {
                                var (bytes, c) = ScanFolderSafe(target, "*.*");
                                totalBytes += bytes;
                                count += c;
                            }
                        }
                    }
                    else if (cat.Id == "ThumbCache")
                    {
                        if (Directory.Exists(cat.Path))
                        {
                            var (bytes, c) = ScanFolderSafe(cat.Path, "thumbcache_*.db");
                            totalBytes += bytes;
                            count += c;
                        }
                    }
                    else
                    {
                        if (Directory.Exists(cat.Path))
                        {
                            var (bytes, c) = ScanFolderSafe(cat.Path, "*.*");
                            totalBytes += bytes;
                            count += c;
                        }
                    }

                    cat.SizeBytes = totalBytes;
                    cat.FileCount = count;
                    cat.SizeDisplay = FormatBytes(totalBytes);
                }
                catch { }
            });

            return categories;
        });
    }

    /// <summary>
    /// Safely cleans files in the selected category IDs, skipping files in active use without throwing errors.
    /// </summary>
    public async Task<(int DeletedFiles, long FreedBytes, string FreedDisplay)> CleanSelectedCategoriesAsync(
        IEnumerable<string> categoryIds,
        Action<string> onOutput,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            int deletedFiles = 0;
            long freedBytes = 0;
            var targetSet = new HashSet<string>(categoryIds, StringComparer.OrdinalIgnoreCase);

            onOutput("══════════════════════════════════════════════════════════════");
            onOutput($"[{DateTime.Now:HH:mm:ss}] 啟動系統暫存檔案深度清理引擎");
            onOutput("══════════════════════════════════════════════════════════════");

            // User Temp
            if (targetSet.Contains("UserTemp"))
            {
                onOutput("▸ 正在清理使用者暫存目錄 (%TEMP%)...");
                var (f, b) = CleanDirectoryInternal(UserTemp, "*.*", ct);
                deletedFiles += f;
                freedBytes += b;
                onOutput($"  - 已清理 {f} 個暫存檔案 ({FormatBytes(b)})");
            }

            // System Temp
            if (targetSet.Contains("SystemTemp"))
            {
                var sysTemp = Path.Combine(WinDir, "Temp");
                onOutput("▸ 正在清理系統暫存目錄 (C:\\Windows\\Temp)...");
                var (f, b) = CleanDirectoryInternal(sysTemp, "*.*", ct);
                deletedFiles += f;
                freedBytes += b;
                onOutput($"  - 已清理 {f} 個系統暫存檔 ({FormatBytes(b)})");
            }

            // Prefetch
            if (targetSet.Contains("Prefetch"))
            {
                var prefetch = Path.Combine(WinDir, "Prefetch");
                onOutput("▸ 正在清理預先擷取快取 (Prefetch)...");
                var (f, b) = CleanDirectoryInternal(prefetch, "*.pf", ct);
                deletedFiles += f;
                freedBytes += b;
                onOutput($"  - 已清理 {f} 個 Prefetch 快取檔 ({FormatBytes(b)})");
            }

            // Memory Dumps
            if (targetSet.Contains("MemoryDumps"))
            {
                onOutput("▸ 正在清理系統記憶體與損毀傾印 (Dumps)...");
                var fullDump = Path.Combine(WinDir, "MEMORY.DMP");
                if (File.Exists(fullDump))
                {
                    try
                    {
                        var len = new FileInfo(fullDump).Length;
                        File.Delete(fullDump);
                        deletedFiles++;
                        freedBytes += len;
                    }
                    catch { }
                }

                var miniDir = Path.Combine(WinDir, "Minidump");
                var (f1, b1) = CleanDirectoryInternal(miniDir, "*.dmp", ct);
                var crashDir = Path.Combine(LocalAppData, "CrashDumps");
                var (f2, b2) = CleanDirectoryInternal(crashDir, "*.*", ct);
                deletedFiles += f1 + f2;
                freedBytes += b1 + b2;
                onOutput($"  - 已清理 {f1 + f2 + (File.Exists(fullDump) ? 0 : 1)} 個傾印檔案 ({FormatBytes(b1 + b2)})");
            }

            // WER Logs
            if (targetSet.Contains("WerReports"))
            {
                var werDir = Path.Combine(ProgramData, "Microsoft", "Windows", "WER");
                onOutput("▸ 正在清理 Windows 錯誤回報紀錄 (WER)...");
                var (f, b) = CleanDirectoryInternal(werDir, "*.*", ct);
                deletedFiles += f;
                freedBytes += b;
                onOutput($"  - 已清理 {f} 個錯誤回報項目 ({FormatBytes(b)})");
            }

            // Delivery Optimization Cache
            if (targetSet.Contains("DeliveryOpt"))
            {
                var doDir = Path.Combine(WinDir, "SoftwareDistribution", "DeliveryOptimization", "Cache");
                onOutput("▸ 正在清理傳遞最佳化快取 (Delivery Optimization)...");
                var (f, b) = CleanDirectoryInternal(doDir, "*.*", ct);
                deletedFiles += f;
                freedBytes += b;
                onOutput($"  - 已清理 {f} 個傳遞暫存區塊 ({FormatBytes(b)})");
            }

            // Setup Files ($WINDOWS.~BT)
            if (targetSet.Contains("SetupFiles"))
            {
                var root = Path.GetPathRoot(WinDir) ?? @"C:\";
                string[] setupDirs = { "$WINDOWS.~BT", "$WINDOWS.~WS", "Windows\\Panther" };
                onOutput("▸ 正在清理 Windows 安裝與版本升級殘留暫存...");
                int sf = 0;
                long sb = 0;
                foreach (var dir in setupDirs)
                {
                    var target = Path.Combine(root, dir);
                    var (f, b) = CleanDirectoryInternal(target, "*.*", ct);
                    sf += f;
                    sb += b;
                }
                deletedFiles += sf;
                freedBytes += sb;
                onOutput($"  - 已清理 {sf} 個安裝殘留項目 ({FormatBytes(sb)})");
            }

            // Windows.old
            if (targetSet.Contains("WindowsOld"))
            {
                var winOld = Path.Combine(Path.GetPathRoot(WinDir) ?? @"C:\", "Windows.old");
                if (Directory.Exists(winOld))
                {
                    onOutput("▸ 正在清理舊版 Windows 備份 (Windows.old)...");
                    var (f, b) = CleanDirectoryInternal(winOld, "*.*", ct);
                    deletedFiles += f;
                    freedBytes += b;
                    onOutput($"  - 已清理 {f} 個舊版本檔案 ({FormatBytes(b)})");
                }
            }

            // Thumbnail Cache
            if (targetSet.Contains("ThumbCache"))
            {
                var thumbDir = Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer");
                onOutput("▸ 正在清理檔案總管縮圖快取 (thumbcache_*.db)...");
                var (f, b) = CleanDirectoryInternal(thumbDir, "thumbcache_*.db", ct);
                deletedFiles += f;
                freedBytes += b;
                onOutput($"  - 已清理 {f} 個縮圖資料庫檔案 ({FormatBytes(b)})");
            }

            // Defender Cache
            if (targetSet.Contains("DefenderCache"))
            {
                var defDir = Path.Combine(ProgramData, "Microsoft", "Windows Defender", "Definition Updates", "Backup");
                onOutput("▸ 正在清理 Windows Defender 病毒碼歷史備份...");
                var (f, b) = CleanDirectoryInternal(defDir, "*.*", ct);
                deletedFiles += f;
                freedBytes += b;
                onOutput($"  - 已清理 {f} 個病毒碼備份檔 ({FormatBytes(b)})");
            }

            // Package Cache
            if (targetSet.Contains("PackageCache"))
            {
                var pkgDir = Path.Combine(ProgramData, "Package Cache");
                onOutput("▸ 正在清理安裝套件快取 (Package Cache)...");
                var (f, b) = CleanDirectoryInternal(pkgDir, "*.*", ct);
                deletedFiles += f;
                freedBytes += b;
                onOutput($"  - 已清理 {f} 個安裝快取檔 ({FormatBytes(b)})");
            }

            // CBS Logs
            if (targetSet.Contains("CbsLogs"))
            {
                var cbsDir = Path.Combine(WinDir, "Logs", "CBS");
                onOutput("▸ 正在清理系統元件維護紀錄 (CBS Logs)...");
                var (f, b) = CleanDirectoryInternal(cbsDir, "CbsPersist_*.log", ct);
                var (f2, b2) = CleanDirectoryInternal(cbsDir, "CbsPersist_*.cab", ct);
                deletedFiles += f + f2;
                freedBytes += b + b2;
                onOutput($"  - 已清理 {f + f2} 個歷史維護記錄檔 ({FormatBytes(b + b2)})");
            }

            onOutput("\n══════════════════════════════════════════════════════════════");
            onOutput($"[{DateTime.Now:HH:mm:ss}] 🎉 系統暫存清理完成！共釋放 {FormatBytes(freedBytes)} 磁碟空間 (清理 {deletedFiles:N0} 個檔案)");
            onOutput("══════════════════════════════════════════════════════════════");

            return (deletedFiles, freedBytes, FormatBytes(freedBytes));
        });
    }

    /// <summary>
    /// Executes the native Windows Disk Cleanup utility (cleanmgr.exe) silently in background.
    /// </summary>
    public async Task<int> RunCleanmgrAutomatedAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        onOutput($"[{DateTime.Now:HH:mm:ss}] 啟動 Windows 原生磁碟清理工具 (cleanmgr.exe /autoclean)...");
        var (output, _, exitCode) = await ProcessHelper.RunProcessAsync("cleanmgr.exe", "/autoclean");
        if (!string.IsNullOrWhiteSpace(output)) onOutput(output.Trim());
        onOutput($"[{DateTime.Now:HH:mm:ss}] Windows 磁碟清理工具執行結束 (結束代碼: {exitCode})。");
        return exitCode;
    }

    private static (long TotalBytes, int FileCount) ScanFolderSafe(string folderPath, string pattern)
    {
        long total = 0;
        int count = 0;
        try
        {
            var dir = new DirectoryInfo(folderPath);
            foreach (var fi in dir.EnumerateFiles(pattern, SearchOption.AllDirectories))
            {
                try
                {
                    total += fi.Length;
                    count++;
                }
                catch { }
            }
        }
        catch { }
        return (total, count);
    }

    private static (int DeletedCount, long FreedBytes) CleanDirectoryInternal(string folderPath, string searchPattern, CancellationToken ct)
    {
        int deleted = 0;
        long bytes = 0;
        if (!Directory.Exists(folderPath)) return (0, 0);

        try
        {
            var dir = new DirectoryInfo(folderPath);
            foreach (var file in dir.EnumerateFiles(searchPattern, SearchOption.AllDirectories))
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var len = file.Length;
                    file.Delete();
                    bytes += len;
                    deleted++;
                }
                catch { /* Locked file in use by active process: safely skip */ }
            }

            // Try removing empty subdirectories
            foreach (var sub in dir.EnumerateDirectories("*", SearchOption.AllDirectories))
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    if (!sub.EnumerateFileSystemInfos().Any())
                    {
                        sub.Delete();
                    }
                }
                catch { }
            }
        }
        catch { }

        return (deleted, bytes);
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
