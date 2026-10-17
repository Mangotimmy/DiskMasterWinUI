using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DiskMasterPortableLauncher;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class Program
{
    private const string TargetExeName = "DiskMasterWinUI.exe";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteFile(string lpFileName);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;

    private static void StripZoneIdentifier(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                DeleteFile($"{filePath}:Zone.Identifier");
            }
        }
        catch { }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();

            // 1. Determine execution and cache directory
            var isWinPe = string.Equals(Environment.GetEnvironmentVariable("SystemDrive"), "X:", StringComparison.OrdinalIgnoreCase)
                          || Environment.CurrentDirectory.StartsWith("X:", StringComparison.OrdinalIgnoreCase);

            var baseCacheDir = isWinPe
                ? Path.Combine(Path.GetTempPath(), "DiskMaster_PE")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskMaster_Portable");

            Directory.CreateDirectory(baseCacheDir);

            // Determine unique version tag based on launcher file write time
            var launcherPath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DiskMaster_Portable.exe");
            StripZoneIdentifier(launcherPath);
            var launcherTicks = File.Exists(launcherPath) ? File.GetLastWriteTimeUtc(launcherPath).Ticks : 0;
            // Elevated and non-elevated runs use separate caches so one can never hit files
            // (or locks) created by the other, which caused "Access to the path ... is denied".
            var elevationSuffix = IsElevated() ? "_admin" : string.Empty;
            var versionTag = (launcherTicks > 0 ? $"app_{launcherTicks}" : "app_latest") + elevationSuffix;

            var versionDir = Path.Combine(baseCacheDir, versionTag);
            var targetExePath = Path.Combine(versionDir, TargetExeName);
            var readyMarker = Path.Combine(versionDir, ".extracted");

            // Check if extraction is needed
            var needsExtract = !File.Exists(targetExePath) || !File.Exists(readyMarker);

            if (needsExtract)
            {
                // Serialize concurrent launches of the same build so they don't fight over files.
                using var mutex = new Mutex(false, $@"Local\DiskMasterPortable_{versionTag}");
                var locked = false;
                try { locked = mutex.WaitOne(TimeSpan.FromSeconds(60)); }
                catch (AbandonedMutexException) { locked = true; }

                try
                {
                    // Another instance may have finished extracting while we waited.
                    if (!File.Exists(targetExePath) || !File.Exists(readyMarker))
                    {
                        try
                        {
                            versionDir = ExtractPayloadStaged(assembly, versionDir);
                            targetExePath = Path.Combine(versionDir, TargetExeName);
                        }
                        catch (FileNotFoundException nf) when (nf.FileName == "payload.zip")
                        {
                            ShowError("找不到內嵌或同目錄下的 payload.zip 資源檔案，無法解壓運行。");
                            return 1;
                        }
                        catch (Exception extractEx)
                        {
                            // Fallback: If target executable already exists in this folder or baseCacheDir, try to run it
                            if (File.Exists(targetExePath))
                            {
                                // A previous extraction is still usable.
                            }
                            else
                            {
                                var legacyTarget = Path.Combine(baseCacheDir, TargetExeName);
                                if (File.Exists(legacyTarget))
                                {
                                    targetExePath = legacyTarget;
                                    versionDir = baseCacheDir;
                                }
                                else
                                {
                                    ShowError($"解壓 DiskMaster 核心組件失敗:\n{extractEx.Message}");
                                    return 1;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (locked) { try { mutex.ReleaseMutex(); } catch { } }
                }

                // Asynchronously clean up older app_* folders that are not in use
                TryCleanOldVersions(baseCacheDir, Path.GetFileName(versionDir.TrimEnd(Path.DirectorySeparatorChar)));
            }

            // 2. Launch application (default asInvoker; elevate if requested)
            StripZoneIdentifier(targetExePath);

            bool requestAdmin = args.Any(a => string.Equals(a, "--elevate", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(a, "/elevate", StringComparison.OrdinalIgnoreCase));

            var psi = new ProcessStartInfo
            {
                FileName = targetExePath,
                Arguments = string.Join(" ", args.Where(a => !a.Equals("--elevate", StringComparison.OrdinalIgnoreCase) && !a.Equals("/elevate", StringComparison.OrdinalIgnoreCase)).Select(a => $"\"{a}\"")),
                WorkingDirectory = versionDir,
                UseShellExecute = true
            };

            if (requestAdmin)
            {
                psi.Verb = "runas";
            }

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                ShowError($"啟動主程式失敗:\n{targetExePath}");
                return 1;
            }

            // Launcher exits immediately; child app continues independently
            return 0;
        }
        catch (Exception ex)
        {
            ShowError($"DiskMaster Portable 啟動異常:\n\n{ex.Message}\n\n{ex.StackTrace}");
            return -1;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts the payload into a fresh unique staging folder (so no existing, possibly locked or
    /// differently-owned file is ever overwritten), then renames it to <paramref name="versionDir"/>.
    /// Returns the directory that actually contains the extracted app (falls back to the staging
    /// folder if the final rename is not possible).
    /// </summary>
    private static string ExtractPayloadStaged(Assembly assembly, string versionDir)
    {
        var stagingDir = $"{versionDir}.tmp_{Guid.NewGuid():N}";
        Directory.CreateDirectory(stagingDir);

        try
        {
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(resourceName))
            {
                var localZip = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload.zip");
                if (!File.Exists(localZip))
                {
                    throw new FileNotFoundException("payload.zip not found", "payload.zip");
                }
                ZipFile.ExtractToDirectory(localZip, stagingDir, overwriteFiles: true);
            }
            else
            {
                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException("無法載入內嵌資源 payload.zip。");
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                archive.ExtractToDirectory(stagingDir, overwriteFiles: true);
            }

            File.WriteAllText(Path.Combine(stagingDir, ".extracted"), DateTime.UtcNow.ToString("o"));
        }
        catch
        {
            try { Directory.Delete(stagingDir, recursive: true); } catch { }
            throw;
        }

        // Drop a stale/incomplete previous folder if possible, then publish the staged one.
        if (Directory.Exists(versionDir))
        {
            try { Directory.Delete(versionDir, recursive: true); } catch { /* locked or not ours */ }
        }

        try
        {
            Directory.Move(stagingDir, versionDir);
            return versionDir;
        }
        catch
        {
            // Could not take over versionDir; run straight from the private staging copy.
            return stagingDir;
        }
    }

    private static void TryCleanOldVersions(string baseCacheDir, string currentTag)
    {
        Task.Run(() =>
        {
            try
            {
                var dirs = Directory.GetDirectories(baseCacheDir, "app_*");
                foreach (var dir in dirs)
                {
                    var dirName = Path.GetFileName(dir);
                    if (!string.Equals(dirName, currentTag, StringComparison.OrdinalIgnoreCase))
                    {
                        try { Directory.Delete(dir, recursive: true); } catch { /* Ignore locked directories */ }
                    }
                }
            }
            catch { }
        });
    }

    private static void ShowError(string message)
    {
        try
        {
            MessageBox(IntPtr.Zero, message, "DiskMaster Pro 啟動錯誤", MB_OK | MB_ICONERROR);
        }
        catch
        {
            Console.Error.WriteLine(message);
        }
    }
}
