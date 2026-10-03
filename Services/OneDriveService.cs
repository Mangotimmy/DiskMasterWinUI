using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Provides comprehensive Microsoft OneDrive detection, uninstallation, folder redirection repair,
/// ghost icon cleanup, sync engine reset, and policy blocking services.
/// </summary>
public class OneDriveService
{
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static readonly string SystemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>
    /// Detects the installation, process, shell folder redirection, and policy status of OneDrive.
    /// </summary>
    public async Task<OneDriveStatusInfo> DetectOneDriveStatusAsync()
    {
        return await Task.Run(() =>
        {
            var info = new OneDriveStatusInfo();

            // 1. Locate OneDrive Executables
            var candidateExePaths = new[]
            {
                Path.Combine(LocalAppData, "Microsoft", "OneDrive", "OneDrive.exe"),
                Path.Combine(ProgramFiles, "Microsoft OneDrive", "OneDrive.exe"),
                Path.Combine(ProgramFilesX86, "Microsoft OneDrive", "OneDrive.exe")
            };

            foreach (var path in candidateExePaths)
            {
                if (File.Exists(path))
                {
                    info.IsInstalled = true;
                    info.ExePath = path;
                    break;
                }
            }

            // 2. Locate Uninstaller / Setup
            var candidateSetupPaths = new List<string>
            {
                Path.Combine(SystemRoot, "SysWOW64", "OneDriveSetup.exe"),
                Path.Combine(SystemRoot, "System32", "OneDriveSetup.exe"),
                Path.Combine(LocalAppData, "Microsoft", "OneDrive", "OneDriveSetup.exe")
            };

            // Search for versioned OneDriveSetup.exe in LocalAppData
            var oneDriveBaseDir = Path.Combine(LocalAppData, "Microsoft", "OneDrive");
            if (Directory.Exists(oneDriveBaseDir))
            {
                try
                {
                    var foundSetups = Directory.GetFiles(oneDriveBaseDir, "OneDriveSetup.exe", SearchOption.AllDirectories);
                    if (foundSetups.Length > 0)
                    {
                        candidateSetupPaths.AddRange(foundSetups);
                    }
                }
                catch { }
            }

            foreach (var path in candidateSetupPaths)
            {
                if (File.Exists(path))
                {
                    info.UninstallerPath = path;
                    info.IsInstalled = true;
                    break;
                }
            }

            // Also check registry uninstall entry directly
            try
            {
                using var uninstKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe");
                if (uninstKey != null)
                {
                    info.IsInstalled = true;
                    var uninstStr = uninstKey.GetValue("UninstallString")?.ToString();
                    if (!string.IsNullOrWhiteSpace(uninstStr) && string.IsNullOrWhiteSpace(info.UninstallerPath))
                    {
                        var match = Regex.Match(uninstStr, "\"([^\"]+)\"");
                        if (match.Success && File.Exists(match.Groups[1].Value))
                        {
                            info.UninstallerPath = match.Groups[1].Value;
                        }
                    }
                }
            }
            catch { }

            // Also check running processes
            try
            {
                var procs = Process.GetProcessesByName("OneDrive");
                info.IsRunning = procs.Length > 0;
                if (info.IsRunning && !info.IsInstalled && procs.Length > 0)
                {
                    info.IsInstalled = true;
                    try { info.ExePath = procs[0].MainModule?.FileName ?? ""; } catch { }
                }
            }
            catch { }

            // 3. Local OneDrive Folder
            var defaultOneDriveDir = Path.Combine(UserProfile, "OneDrive");
            if (Directory.Exists(defaultOneDriveDir))
            {
                info.LocalOneDrivePath = defaultOneDriveDir;
            }

            // 4. Check User Shell Folders Redirection (Desktop, Personal, Pictures)
            try
            {
                using var shellKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
                if (shellKey != null)
                {
                    var folderNames = new[] { "Desktop", "Personal", "My Pictures", "{0DDD0157-5460-434e-8677-E563E3663870}", "{754AC886-DF64-4C2C-86F5-A0E05EADC3E9}", "{F42EE2D3-909F-4907-8871-4C22FC0BF756}" };
                    foreach (var name in folderNames)
                    {
                        var val = shellKey.GetValue(name)?.ToString();
                        if (!string.IsNullOrWhiteSpace(val) && val.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            info.IsFoldersRedirected = true;
                            var friendlyName = name switch
                            {
                                "Personal" or "{F42EE2D3-909F-4907-8871-4C22FC0BF756}" => "Documents (文件)",
                                "Desktop" or "{754AC886-DF64-4C2C-86F5-A0E05EADC3E9}" => "Desktop (桌面)",
                                "My Pictures" or "{0DDD0157-5460-434e-8677-E563E3663870}" => "Pictures (圖片)",
                                _ => name
                            };
                            if (!info.RedirectedFolderNames.Contains(friendlyName))
                            {
                                info.RedirectedFolderNames.Add(friendlyName);
                            }
                        }
                    }
                }
            }
            catch { }

            // 5. Check Cloud-Only Files (Files On-Demand)
            if (!string.IsNullOrWhiteSpace(info.LocalOneDrivePath) && Directory.Exists(info.LocalOneDrivePath))
            {
                try
                {
                    int cloudCount = 0;
                    var di = new DirectoryInfo(info.LocalOneDrivePath);
                    foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories).Take(500))
                    {
                        if ((fi.Attributes & FileAttributes.ReparsePoint) != 0 || (fi.Attributes & FileAttributes.Offline) != 0)
                        {
                            cloudCount++;
                        }
                    }
                    info.HasCloudOnlyFiles = cloudCount > 0;
                    info.CloudOnlyFileCount = cloudCount;
                }
                catch { }
            }

            // 6. Check File Explorer Navigation Pane Pin
            try
            {
                using var clsidKey = Registry.ClassesRoot.OpenSubKey(@"CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}");
                if (clsidKey != null)
                {
                    var pinned = clsidKey.GetValue("System.IsPinnedToNameSpaceTree");
                    if (pinned is int pInt && pInt == 1) info.IsFileExplorerPinned = true;
                }
            }
            catch { }

            // 7. Check Group Policy Block
            try
            {
                using var policyKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\OneDrive");
                if (policyKey != null)
                {
                    var disabled = policyKey.GetValue("DisableFileSyncNGSC");
                    if (disabled is int dInt && dInt == 1) info.IsPolicyBlocked = true;
                }
            }
            catch { }

            return info;
        });
    }

    /// <summary>
    /// Performs deep uninstallation and residual purge of Microsoft OneDrive.
    /// </summary>
    public async Task<(bool Success, string Message)> DeepUninstallOneDriveAsync(
        bool restoreShellFolders = true,
        bool cleanResiduals = true,
        bool blockReinstall = true)
    {
        var messages = new List<string>();

        // Step 1: Kill OneDrive processes
        try
        {
            var procs = Process.GetProcessesByName("OneDrive");
            foreach (var p in procs)
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
            messages.Add("已強制結束所有 OneDrive 正在執行的處理程序。");
        }
        catch { }

        // Step 2: Run uninstaller
        var status = await DetectOneDriveStatusAsync();
        string uninstaller = !string.IsNullOrWhiteSpace(status.UninstallerPath) && File.Exists(status.UninstallerPath)
            ? status.UninstallerPath
            : Path.Combine(SystemRoot, "SysWOW64", "OneDriveSetup.exe");

        if (File.Exists(uninstaller))
        {
            var (outStr, errStr, exitCode) = await ProcessHelper.RunProcessAsync(uninstaller, "/uninstall");
            messages.Add($"已執行原生解除安裝程式 ({uninstaller} /uninstall)。");
        }
        else
        {
            // Try winget uninstall
            try
            {
                await ProcessHelper.RunProcessAsync("winget", "uninstall --id Microsoft.OneDrive --silent --accept-source-agreements");
                messages.Add("已透過 WinGet 執行 OneDrive 靜默解除安裝。");
            }
            catch { }
        }

        // Step 3: Restore Shell Folders if requested
        if (restoreShellFolders)
        {
            var (folderOk, folderMsg) = await RestoreUserShellFoldersAsync();
            messages.Add(folderMsg);
        }

        // Step 4: Remove Ghost Icon in File Explorer
        var (iconOk, iconMsg) = await RemoveExplorerGhostIconAsync();
        messages.Add(iconMsg);

        // Step 5: Clean residual files & folders if requested
        if (cleanResiduals)
        {
            var (cleanOk, cleanMsg) = await CleanResidualFoldersAsync();
            messages.Add(cleanMsg);
        }

        // Step 6: Block automatic reinstallation via Group Policy if requested
        if (blockReinstall)
        {
            var (polOk, polMsg) = await ToggleOneDrivePolicyBlockAsync(true);
            messages.Add(polMsg);
        }

        // Step 7: Delete Run Startup keys
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (runKey != null && runKey.GetValue("OneDrive") != null)
            {
                runKey.DeleteValue("OneDrive", false);
                messages.Add("已移除登錄檔開機自啟動項目 (Run)。");
            }
        }
        catch { }

        return (true, string.Join("\n", messages));
    }

    /// <summary>
    /// Restores user shell folders (Desktop, Documents, Pictures) from OneDrive back to native %USERPROFILE%.
    /// Safely moves existing files without overwriting.
    /// </summary>
    public async Task<(bool Success, string Message)> RestoreUserShellFoldersAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var movedCount = 0;
                var targets = new (string KeyName, string FolderName, string GuidKey)[]
                {
                    ("Desktop", "Desktop", "{754AC886-DF64-4C2C-86F5-A0E05EADC3E9}"),
                    ("Personal", "Documents", "{F42EE2D3-909F-4907-8871-4C22FC0BF756}"),
                    ("My Pictures", "Pictures", "{0DDD0157-5460-434e-8677-E563E3663870}"),
                    ("My Music", "Music", "{A0C69A99-21C8-4671-8703-7934162FCF1D}"),
                    ("My Video", "Videos", "{352481E8-33FF-4434-886C-9A349D670D50}")
                };

                using var shellKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", true);
                if (shellKey != null)
                {
                    foreach (var (keyName, folderName, guidKey) in targets)
                    {
                        var nativePath = Path.Combine(UserProfile, folderName);
                        Directory.CreateDirectory(nativePath);

                        var oneDrivePath = Path.Combine(UserProfile, "OneDrive", folderName);

                        // Move existing files from OneDrive folder back to native directory
                        if (Directory.Exists(oneDrivePath))
                        {
                            try
                            {
                                foreach (var file in Directory.GetFiles(oneDrivePath))
                                {
                                    var dest = Path.Combine(nativePath, Path.GetFileName(file));
                                    if (!File.Exists(dest))
                                    {
                                        File.Move(file, dest);
                                        movedCount++;
                                    }
                                }
                            }
                            catch { }
                        }

                        // Restore registry pointer
                        var rawValue = @"%USERPROFILE%\" + folderName;
                        shellKey.SetValue(keyName, rawValue, RegistryValueKind.ExpandString);
                        if (!string.IsNullOrWhiteSpace(guidKey))
                        {
                            shellKey.SetValue(guidKey, rawValue, RegistryValueKind.ExpandString);
                        }
                    }
                }

                return (true, $"個人資料夾原生路徑已成功還原至 %USERPROFILE% (安全遷回 {movedCount} 個檔案)。");
            }
            catch (Exception ex)
            {
                return (false, $"資料夾還原失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Removes the ghost OneDrive cloud icon from the File Explorer sidebar navigation pane.
    /// </summary>
    public async Task<(bool Success, string Message)> RemoveExplorerGhostIconAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var clsidList = new[]
                {
                    @"Software\Classes\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}",
                    @"Software\Classes\WOW6432Node\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}"
                };

                foreach (var sub in clsidList)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(sub);
                    key?.SetValue("System.IsPinnedToNameSpaceTree", 0, RegistryValueKind.DWord);
                }

                // Also update HKEY_CLASSES_ROOT if elevated
                try
                {
                    using var crKey = Registry.ClassesRoot.OpenSubKey(@"CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}", true);
                    crKey?.SetValue("System.IsPinnedToNameSpaceTree", 0, RegistryValueKind.DWord);
                }
                catch { }

                return (true, "已成功隱藏檔案總管左側側邊欄的 OneDrive 幽靈圖示。");
            }
            catch (Exception ex)
            {
                return (false, $"側邊欄圖示清除失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Resets the OneDrive sync engine (executes onedrive.exe /reset) and clears corrupted sync cache.
    /// Fixes the infinite "Processing Changes" / "Looking for changes" hang.
    /// </summary>
    public async Task<(bool Success, string Message)> ResetOneDriveSyncEngineAsync()
    {
        var status = await DetectOneDriveStatusAsync();
        if (string.IsNullOrWhiteSpace(status.ExePath) || !File.Exists(status.ExePath))
        {
            return (false, "未偵測到 OneDrive 主程式，無法執行重設。");
        }

        try
        {
            // Execute onedrive.exe /reset
            Process.Start(new ProcessStartInfo
            {
                FileName = status.ExePath,
                Arguments = "/reset",
                UseShellExecute = true
            });

            // Clean corrupted personal settings cache
            var settingsDir = Path.Combine(LocalAppData, "Microsoft", "OneDrive", "settings", "Personal");
            if (Directory.Exists(settingsDir))
            {
                try
                {
                    foreach (var f in Directory.GetFiles(settingsDir, "*.dat")) File.Delete(f);
                }
                catch { }
            }

            return (true, "已成功觸發 OneDrive 同步引擎重設命令 (/reset) 並清理損毀快取！");
        }
        catch (Exception ex)
        {
            return (false, $"重設失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// Blocks or unblocks OneDrive automatic reinstallation and background synchronization via Group Policy.
    /// </summary>
    public async Task<(bool Success, string Message)> ToggleOneDrivePolicyBlockAsync(bool block)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\OneDrive");
                if (key != null)
                {
                    if (block)
                    {
                        key.SetValue("DisableFileSyncNGSC", 1, RegistryValueKind.DWord);
                        key.SetValue("PreventNetworkTrafficPreUserSignIn", 1, RegistryValueKind.DWord);
                        return (true, "已透過本機群組原則徹底封鎖 OneDrive 同步與背景自動重新安裝。");
                    }
                    else
                    {
                        key.DeleteValue("DisableFileSyncNGSC", false);
                        key.DeleteValue("PreventNetworkTrafficPreUserSignIn", false);
                        return (true, "已解除群組原則對 OneDrive 的封鎖。");
                    }
                }
                return (false, "無法存取登錄檔群組原則路徑。");
            }
            catch (Exception ex)
            {
                return (false, $"群組原則設定失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Cleans residual OneDrive cache and temporary directories.
    /// </summary>
    private static async Task<(bool Success, string Message)> CleanResidualFoldersAsync()
    {
        return await Task.Run(() =>
        {
            var cleanedCount = 0;
            var targetDirs = new[]
            {
                Path.Combine(LocalAppData, "Microsoft", "OneDrive"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft OneDrive"),
                @"C:\OneDriveTemp"
            };

            foreach (var dir in targetDirs)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        cleanedCount++;
                    }
                    catch { }
                }
            }

            return (true, $"已清理 {cleanedCount} 個 OneDrive 快取與暫存殘留目錄。");
        });
    }
}
