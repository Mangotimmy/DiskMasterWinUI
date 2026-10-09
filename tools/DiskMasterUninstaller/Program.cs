using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace DiskMasterUninstaller;

internal static class Program
{
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x00000000;
    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const int IDYES = 6;

    [STAThread]
    static void Main(string[] args)
    {
        bool isSilent = args.Any(a => a.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("-Silent", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

        bool isWorker = args.Any(a => a.Equals("--worker", StringComparison.OrdinalIgnoreCase));
        
        string targetDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int parentPid = 0;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--target=", StringComparison.OrdinalIgnoreCase))
            {
                targetDir = arg.Substring(9).Trim('"', '\'');
            }
            else if (arg.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase))
            {
                targetDir = arg.Substring(5).Trim('"', '\'');
            }
            else if (arg.StartsWith("--parent-pid=", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(arg.Substring(13), out parentPid);
            }
        }

        string currentCulture = CultureInfo.CurrentUICulture.Name;
        bool isZhCn = currentCulture.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase) ||
                      currentCulture.StartsWith("zh-SG", StringComparison.OrdinalIgnoreCase);
        bool isJa = currentCulture.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
        bool isEn = currentCulture.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        // Helper text
        string title = isZhCn ? "DiskMaster Pro 卸载程序" :
                       isJa ? "DiskMaster Pro アンインストール" :
                       isEn ? "DiskMaster Pro Uninstaller" :
                       "DiskMaster Pro 解除安裝程式";

        string confirmMsg = isZhCn ? $"您确定要彻底卸载 DiskMaster Pro 旗舰版及其所有组件吗？\n\n目标文件夹：\n{targetDir}" :
                            isJa ? $"DiskMaster Pro Flagship を完全にアンインストールしてもよろしいですか？\n\n対象フォルダー：\n{targetDir}" :
                            isEn ? $"Are you sure you want to completely uninstall DiskMaster Pro Flagship and all of its components?\n\nTarget directory:\n{targetDir}" :
                            $"您確定要徹底解除安裝 DiskMaster Pro 旗艦版及其所有元件嗎？\n\n目標資料夾：\n{targetDir}";

        string completedMsg = isZhCn ? "DiskMaster Pro 旗舰版已成功从您的计算机中移除。" :
                              isJa ? "DiskMaster Pro Flagship はコンピューターから正常に削除されました。" :
                              isEn ? "DiskMaster Pro Flagship has been successfully removed from your computer." :
                              "DiskMaster Pro 旗艦版已成功從您的電腦中移除。";

        string errorMsg = isZhCn ? "卸载过程中发生错误：" :
                          isJa ? "アンインストール中にエラーが発生しました：" :
                          isEn ? "An error occurred during uninstallation:" :
                          "解除安裝過程中發生錯誤：";

        if (!isWorker)
        {
            // Initial Launch: Confirmation Dialog
            if (!isSilent)
            {
                int res = MessageBox(IntPtr.Zero, confirmMsg, title, MB_YESNO | MB_ICONQUESTION);
                if (res != IDYES)
                {
                    return;
                }
            }

            // Check if elevated privileges are needed for Program Files
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            bool isUnderProgramFiles = (!string.IsNullOrEmpty(programFiles) && targetDir.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)) ||
                                       (!string.IsNullOrEmpty(programFilesX86) && targetDir.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase));

            if (isUnderProgramFiles && !IsAdministrator())
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = Environment.ProcessPath ?? Path.Combine(targetDir, "Uninstall.exe"),
                        Arguments = string.Join(" ", args),
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(psi);
                    return;
                }
                catch
                {
                    // If UAC was cancelled by user, stop
                    return;
                }
            }

            // Copy executable to %TEMP% to unlock target directory
            try
            {
                string tempDir = Path.GetTempPath();
                string tempExePath = Path.Combine(tempDir, $"DiskMaster_Uninstaller_{Process.GetCurrentProcess().Id}.exe");
                
                string currentExePath = Environment.ProcessPath ?? Path.Combine(targetDir, "Uninstall.exe");
                File.Copy(currentExePath, tempExePath, overwrite: true);

                var workerPsi = new ProcessStartInfo
                {
                    FileName = tempExePath,
                    Arguments = $"--worker --target=\"{targetDir}\" --parent-pid={Process.GetCurrentProcess().Id}" + (isSilent ? " --silent" : ""),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(workerPsi);
                return;
            }
            catch (Exception ex)
            {
                if (!isSilent) MessageBox(IntPtr.Zero, $"{errorMsg}\n{ex.Message}", title, MB_OK | MB_ICONERROR);
                return;
            }
        }

        // ==================== WORKER EXECUTION ====================
        try
        {
            // 1. Wait for parent process to exit
            if (parentPid > 0)
            {
                try
                {
                    var parentProc = Process.GetProcessById(parentPid);
                    parentProc.WaitForExit(5000);
                }
                catch { }
            }

            // 2. Kill all application processes
            foreach (var procName in new[] { "DiskMasterWinUI", "DiskMaster_Portable", "DiskMasterInstaller" })
            {
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }

            Thread.Sleep(500);

            // 3. Remove Desktop & Start Menu Shortcuts
            RemoveShortcuts();

            // 4. Remove Registry Keys (ARP & AppCompatFlags)
            RemoveRegistryEntries(targetDir);

            // 5. Delete Target Installation Directory
            if (Directory.Exists(targetDir))
            {
                // Retry up to 5 times with backoff in case of slow file release
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        DeleteDirectoryRecursive(targetDir);
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(500);
                    }
                }
            }

            // 6. Show Success Dialog
            if (!isSilent)
            {
                MessageBox(IntPtr.Zero, completedMsg, title, MB_OK | MB_ICONINFORMATION);
            }

            // 7. Detach self-deletion of temp executable
            ScheduleSelfDelete();
        }
        catch (Exception ex)
        {
            if (!isSilent)
            {
                MessageBox(IntPtr.Zero, $"{errorMsg}\n{ex.Message}", title, MB_OK | MB_ICONERROR);
            }
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveShortcuts()
    {
        try
        {
            // Desktop Shortcuts
            string pubDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrEmpty(pubDesktop))
            {
                string pubLnk = Path.Combine(pubDesktop, "DiskMaster Pro.lnk");
                if (File.Exists(pubLnk)) try { File.Delete(pubLnk); } catch { }
            }

            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrEmpty(userDesktop))
            {
                string userLnk = Path.Combine(userDesktop, "DiskMaster Pro.lnk");
                if (File.Exists(userLnk)) try { File.Delete(userLnk); } catch { }
            }

            // Start Menu Shortcuts
            string commonStart = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            if (!string.IsNullOrEmpty(commonStart))
            {
                string startDir = Path.Combine(commonStart, "DiskMaster Pro");
                if (Directory.Exists(startDir)) try { Directory.Delete(startDir, true); } catch { }
            }

            string userStart = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (!string.IsNullOrEmpty(userStart))
            {
                string startDir = Path.Combine(userStart, "DiskMaster Pro");
                if (Directory.Exists(startDir)) try { Directory.Delete(startDir, true); } catch { }
            }
        }
        catch { }
    }

    private static void RemoveRegistryEntries(string targetDir)
    {
        string exePath = Path.Combine(targetDir, "DiskMasterWinUI.exe");

        // Remove HKLM ARP Uninstall Key
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskMasterPro", throwOnMissingSubKey: false);
        }
        catch { }

        // Remove HKCU ARP Uninstall Key
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskMasterPro", throwOnMissingSubKey: false);
        }
        catch { }

        // Remove AppCompatFlags RUNASADMIN in HKLM
        try
        {
            using var lmLayerKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", writable: true);
            lmLayerKey?.DeleteValue(exePath, throwOnMissingValue: false);
        }
        catch { }

        // Remove AppCompatFlags RUNASADMIN in HKCU
        try
        {
            using var cuLayerKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", writable: true);
            cuLayerKey?.DeleteValue(exePath, throwOnMissingValue: false);
        }
        catch { }
    }

    private static void DeleteDirectoryRecursive(string path)
    {
        foreach (var file in Directory.GetFiles(path))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch { }
        }

        foreach (var subDir in Directory.GetDirectories(path))
        {
            DeleteDirectoryRecursive(subDir);
        }

        try
        {
            Directory.Delete(path, true);
        }
        catch { }
    }

    private static void ScheduleSelfDelete()
    {
        try
        {
            string? currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe)) return;

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c ping 127.0.0.1 -n 2 >nul & del \"{currentExe}\" >nul 2>&1",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }
        catch { }
    }
}
