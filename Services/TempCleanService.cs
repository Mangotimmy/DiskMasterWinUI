using System.IO;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service for analyzing, scanning, and safely cleaning system and user temporary files,
/// Windows.old backup archives, application and browser caches, crash dumps, and Windows upgrade remnants.
/// </summary>
public class TempCleanService
{
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
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
                    Description = "目前登入使用者應用程式所產生的暫存與中繼快取檔案 (%TEMP%)",
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
                    Id = "AppCache",
                    Name = "應用程式與瀏覽器快取 (App Cache)",
                    Description = "Edge、Chrome、Firefox 瀏覽器快取、DirectX/GPU Shader 快取及 Discord/Spotify/Teams 快取",
                    Path = LocalAppData,
                    Icon = "🌐"
                },
                new()
                {
                    Id = "WindowsOld",
                    Name = "舊版 Windows 備份 (Windows.old)",
                    Description = "前次系統升級保留之回退目錄 (Windows.old / Windows.old.000)",
                    Path = Path.Combine(Path.GetPathRoot(WinDir) ?? @"C:\", "Windows.old"),
                    Icon = "💾"
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

                    if (cat.Id == "AppCache")
                    {
                        var cacheDirs = GetAppCacheDirectories();
                        foreach (var dir in cacheDirs)
                        {
                            var (bytes, c) = ScanFolderSafe(dir, "*.*");
                            totalBytes += bytes;
                            count += c;
                        }
                    }
                    else if (cat.Id == "WindowsOld")
                    {
                        var oldDirs = GetWindowsOldPaths();
                        foreach (var dir in oldDirs)
                        {
                            var (bytes, c) = ScanFolderSafe(dir, "*.*");
                            totalBytes += bytes;
                            count += c;
                        }
                    }
                    else if (cat.Id == "UserTemp")
                    {
                        if (Directory.Exists(UserTemp))
                        {
                            var (bytes, c) = ScanFolderSafe(UserTemp, "*.*");
                            totalBytes += bytes;
                            count += c;
                        }

                        var inetCache = Path.Combine(LocalAppData, @"Microsoft\Windows\INetCache");
                        if (Directory.Exists(inetCache))
                        {
                            var (bytes, c) = ScanFolderSafe(inetCache, "*.*");
                            totalBytes += bytes;
                            count += c;
                        }
                    }
                    else if (cat.Id == "MemoryDumps")
                    {
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

                        if (Directory.Exists(cat.Path))
                        {
                            var (bytes, c) = ScanFolderSafe(cat.Path, "*.dmp");
                            totalBytes += bytes;
                            count += c;
                        }

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
            onOutput($"[{DateTime.Now:HH:mm:ss}] 啟動系統暫存與磁碟清理引擎");
            onOutput("══════════════════════════════════════════════════════════════");

            // User Temp
            if (targetSet.Contains("UserTemp"))
            {
                onOutput("▸ 正在清理使用者暫存目錄 (%TEMP%)...");
                var (f, b) = CleanDirectoryInternal(UserTemp, "*.*", ct);
                var inetCache = Path.Combine(LocalAppData, @"Microsoft\Windows\INetCache");
                var (f2, b2) = CleanDirectoryInternal(inetCache, "*.*", ct);
                deletedFiles += f + f2;
                freedBytes += b + b2;
                onOutput($"  - 已清理 {f + f2} 個使用者暫存檔案 ({FormatBytes(b + b2)})");
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

            // App Cache
            if (targetSet.Contains("AppCache"))
            {
                onOutput("▸ 正在清理應用程式與瀏覽器快取 (App Cache: Edge/Chrome/Firefox/GPU Shader)...");
                int af = 0;
                long ab = 0;
                var cacheDirs = GetAppCacheDirectories();
                foreach (var dir in cacheDirs)
                {
                    if (ct.IsCancellationRequested) break;
                    var (f, b) = CleanDirectoryInternal(dir, "*.*", ct);
                    af += f;
                    ab += b;
                }
                deletedFiles += af;
                freedBytes += ab;
                onOutput($"  - 已清理 {af} 個應用程式快取檔案 ({FormatBytes(ab)})");
            }

            // Windows.old
            if (targetSet.Contains("WindowsOld"))
            {
                var oldDirs = GetWindowsOldPaths();
                if (oldDirs.Count > 0)
                {
                    onOutput("▸ 正在清理舊版 Windows 備份目錄 (Windows.old)...");
                    int wf = 0;
                    long wb = 0;
                    foreach (var winOld in oldDirs)
                    {
                        if (ct.IsCancellationRequested) break;
                        var (f, b) = CleanDirectoryInternal(winOld, "*.*", ct);
                        wf += f;
                        wb += b;

                        // Check if locked subdirectories remain
                        if (Directory.Exists(winOld))
                        {
                            try
                            {
                                onOutput($"  ▸ 取得 {winOld} 管理員擁有權並釋放權限...");
                                ProcessHelper.RunProcessAsync("takeown.exe", $"/F \"{winOld}\" /A /R /D Y").GetAwaiter().GetResult();
                                ProcessHelper.RunProcessAsync("icacls.exe", $"\"{winOld}\" /grant *S-1-5-32-544:F /T /C /Q").GetAwaiter().GetResult();
                                ProcessHelper.RunProcessAsync("cmd.exe", $"/c rd /s /q \"{winOld}\"").GetAwaiter().GetResult();
                            }
                            catch { }
                        }
                    }
                    deletedFiles += wf;
                    freedBytes += wb;
                    onOutput($"  - 已清理 {wf} 個 Windows.old 歷史備份檔案 ({FormatBytes(wb)})");
                }
                else
                {
                    onOutput("  - 未偵測到 Windows.old 備份目錄。");
                }
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
            onOutput($"[{DateTime.Now:HH:mm:ss}] 🎉 磁碟清理完成！共釋放 {FormatBytes(freedBytes)} 磁碟空間 (清理 {deletedFiles:N0} 個檔案)");
            onOutput("══════════════════════════════════════════════════════════════");

            return (deletedFiles, freedBytes, FormatBytes(freedBytes));
        });
    }

    /// <summary>
    /// Directly cleans temporary files (%TEMP%, Windows Temp, INetCache).
    /// </summary>
    public Task<(int DeletedFiles, long FreedBytes, string FreedDisplay)> CleanTempFilesAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        return CleanSelectedCategoriesAsync(new[] { "UserTemp", "SystemTemp" }, onOutput, ct);
    }

    /// <summary>
    /// Directly cleans Windows.old backup directories with administrative ownership release.
    /// </summary>
    public Task<(int DeletedFiles, long FreedBytes, string FreedDisplay)> CleanWindowsOldAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        return CleanSelectedCategoriesAsync(new[] { "WindowsOld" }, onOutput, ct);
    }

    /// <summary>
    /// Directly cleans browser, GPU shader, and common app caches.
    /// </summary>
    public Task<(int DeletedFiles, long FreedBytes, string FreedDisplay)> CleanAppCacheAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        return CleanSelectedCategoriesAsync(new[] { "AppCache" }, onOutput, ct);
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

    /// <summary>
    /// Collects known application and browser cache directories across active and system user profiles.
    /// </summary>
    public static List<string> GetAppCacheDirectories()
    {
        var list = new List<string>();
        var localApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roamingApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfValid(string? path, bool isRoaming)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                if (isRoaming) roamingApps.Add(path);
                else localApps.Add(path);
            }
        }

        AddIfValid(Environment.GetEnvironmentVariable("LOCALAPPDATA"), false);
        AddIfValid(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), false);
        AddIfValid(Environment.GetEnvironmentVariable("APPDATA"), true);
        AddIfValid(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), true);

        // Also inspect profiles under C:\Users\*
        try
        {
            var usersDir = Path.Combine(Path.GetPathRoot(WinDir) ?? @"C:\", "Users");
            if (Directory.Exists(usersDir))
            {
                foreach (var userDir in Directory.EnumerateDirectories(usersDir))
                {
                    var name = Path.GetFileName(userDir);
                    if (name.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("All Users", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var uLocal = Path.Combine(userDir, @"AppData\Local");
                    if (Directory.Exists(uLocal)) localApps.Add(uLocal);
                    var uRoaming = Path.Combine(userDir, @"AppData\Roaming");
                    if (Directory.Exists(uRoaming)) roamingApps.Add(uRoaming);
                }
            }
        }
        catch { }

        foreach (var localApp in localApps)
        {
            // Browsers
            list.Add(Path.Combine(localApp, @"Microsoft\Edge\User Data\Default\Cache"));
            list.Add(Path.Combine(localApp, @"Microsoft\Edge\User Data\Default\Code Cache"));
            list.Add(Path.Combine(localApp, @"Microsoft\Edge\User Data\Default\GPUCache"));

            list.Add(Path.Combine(localApp, @"Google\Chrome\User Data\Default\Cache"));
            list.Add(Path.Combine(localApp, @"Google\Chrome\User Data\Default\Code Cache"));
            list.Add(Path.Combine(localApp, @"Google\Chrome\User Data\Default\GPUCache"));

            list.Add(Path.Combine(localApp, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"));
            list.Add(Path.Combine(localApp, @"BraveSoftware\Brave-Browser\User Data\Default\Code Cache"));

            var firefoxProfiles = Path.Combine(localApp, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(firefoxProfiles))
            {
                try
                {
                    foreach (var d in Directory.EnumerateDirectories(firefoxProfiles))
                    {
                        list.Add(Path.Combine(d, "cache2"));
                    }
                }
                catch { }
            }

            list.Add(Path.Combine(localApp, @"Opera Software\Opera Stable\Cache"));
            list.Add(Path.Combine(localApp, @"Opera Software\Opera GX Stable\Cache"));
            list.Add(Path.Combine(localApp, @"Vivaldi\User Data\Default\Cache"));

            // Shader Caches
            list.Add(Path.Combine(localApp, "D3DSCache"));
            list.Add(Path.Combine(localApp, @"NVIDIA\DXCache"));
            list.Add(Path.Combine(localApp, @"NVIDIA\GLCache"));
            list.Add(Path.Combine(localApp, @"AMD\DxCache"));
            list.Add(Path.Combine(localApp, @"AMD\GLCache"));

            list.Add(Path.Combine(localApp, @"Spotify\Storage"));
            list.Add(Path.Combine(localApp, @"Packages\MSTeams_8wekyb3d8bbwe\LocalCache"));
        }

        foreach (var roamingApp in roamingApps)
        {
            list.Add(Path.Combine(roamingApp, @"discord\Cache"));
            list.Add(Path.Combine(roamingApp, @"discord\Code Cache"));
            list.Add(Path.Combine(roamingApp, @"discord\GPUCache"));
            list.Add(Path.Combine(roamingApp, @"Microsoft\Teams\Cache"));
            list.Add(Path.Combine(roamingApp, @"Code\Cache"));
            list.Add(Path.Combine(roamingApp, @"Code\CachedData"));
        }

        return list.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Searches for Windows.old and historical upgrade backup directories across drives.
    /// </summary>
    public static List<string> GetWindowsOldPaths()
    {
        var list = new List<string>();
        try
        {
            var winRoot = Path.GetPathRoot(WinDir) ?? @"C:\";
            var rootOld = Path.Combine(winRoot, "Windows.old");
            if (Directory.Exists(rootOld)) list.Add(rootOld);

            var drives = DriveInfo.GetDrives();
            foreach (var d in drives)
            {
                if (!d.IsReady) continue;
                try
                {
                    var p = Path.Combine(d.RootDirectory.FullName, "Windows.old");
                    if (Directory.Exists(p)) list.Add(p);

                    foreach (var oldDir in Directory.EnumerateDirectories(d.RootDirectory.FullName, "Windows.old.*"))
                    {
                        list.Add(oldDir);
                    }
                }
                catch { }
            }
        }
        catch { }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly EnumerationOptions SafeEnumOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private static (long TotalBytes, int FileCount) ScanFolderSafe(string folderPath, string pattern)
    {
        long total = 0;
        int count = 0;
        try
        {
            var dir = new DirectoryInfo(folderPath);
            if (!dir.Exists) return (0, 0);

            foreach (var fi in dir.EnumerateFiles(pattern, SafeEnumOptions))
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
            foreach (var file in dir.EnumerateFiles(searchPattern, SafeEnumOptions))
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
            foreach (var sub in dir.EnumerateDirectories("*", SafeEnumOptions))
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
