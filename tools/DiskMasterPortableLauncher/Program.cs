using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DiskMasterPortableLauncher;

internal static class Program
{
    private const string TargetExeName = "DiskMasterWinUI.exe";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;

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
            var launcherTicks = File.Exists(launcherPath) ? File.GetLastWriteTimeUtc(launcherPath).Ticks : 0;
            var versionTag = launcherTicks > 0 ? $"app_{launcherTicks}" : "app_latest";

            var versionDir = Path.Combine(baseCacheDir, versionTag);
            var targetExePath = Path.Combine(versionDir, TargetExeName);
            var readyMarker = Path.Combine(versionDir, ".extracted");

            // Check if extraction is needed
            var needsExtract = !File.Exists(targetExePath) || !File.Exists(readyMarker);

            if (needsExtract)
            {
                // Locate embedded payload resource
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));

                try
                {
                    if (string.IsNullOrEmpty(resourceName))
                    {
                        var localZip = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload.zip");
                        if (File.Exists(localZip))
                        {
                            Directory.CreateDirectory(versionDir);
                            ZipFile.ExtractToDirectory(localZip, versionDir, overwriteFiles: true);
                            File.WriteAllText(readyMarker, DateTime.UtcNow.ToString("o"));
                        }
                        else
                        {
                            ShowError("找不到內嵌或同目錄下的 payload.zip 資源檔案，無法解壓運行。");
                            return 1;
                        }
                    }
                    else
                    {
                        Directory.CreateDirectory(versionDir);
                        using var stream = assembly.GetManifestResourceStream(resourceName);
                        if (stream == null)
                        {
                            ShowError("無法載入內嵌資源 payload.zip。");
                            return 1;
                        }

                        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                        archive.ExtractToDirectory(versionDir, overwriteFiles: true);
                        File.WriteAllText(readyMarker, DateTime.UtcNow.ToString("o"));
                    }
                }
                catch (Exception extractEx)
                {
                    // Fallback: If target executable already exists in this folder or baseCacheDir, try to run it
                    if (!File.Exists(targetExePath))
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

                // Asynchronously clean up older app_* folders that are not in use
                TryCleanOldVersions(baseCacheDir, versionTag);
            }

            // 2. Launch application (default asInvoker; elevate if requested)
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
