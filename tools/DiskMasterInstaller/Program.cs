using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.Win32;

namespace DiskMasterInstaller;

internal static class Program
{
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x00000000;
    private const uint MB_YESNO = 0x00000004;
    private const uint MB_YESNOCANCEL = 0x00000003;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const int IDYES = 6;
    private const int IDNO = 7;
    private const int IDCANCEL = 2;

    [STAThread]
    static void Main(string[] args)
    {
        bool isSilent = args.Any(a => a.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("-Silent", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

        bool noRestart = args.Any(a => a.Equals("/NORESTART", StringComparison.OrdinalIgnoreCase) ||
                                       a.Equals("--norestart", StringComparison.OrdinalIgnoreCase));

        bool isAllUsers = args.Any(a => a.Equals("/ALLUSERS", StringComparison.OrdinalIgnoreCase) ||
                                       a.Equals("/MACHINE", StringComparison.OrdinalIgnoreCase) ||
                                       a.Equals("--allusers", StringComparison.OrdinalIgnoreCase));

        bool isCurrentUser = args.Any(a => a.Equals("/CURRENTUSER", StringComparison.OrdinalIgnoreCase) ||
                                         a.Equals("/USER", StringComparison.OrdinalIgnoreCase) ||
                                         a.Equals("--currentuser", StringComparison.OrdinalIgnoreCase));

        bool grantAdmin = !args.Any(a => a.Equals("/NOGRANTADMIN", StringComparison.OrdinalIgnoreCase) ||
                                         a.Equals("--no-grant-admin", StringComparison.OrdinalIgnoreCase));

        // If invoked as uninstaller directly
        if (args.Any(a => a.Equals("/UNINSTALL", StringComparison.OrdinalIgnoreCase) || a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            PerformDirectUninstall(args, isSilent);
            return;
        }

        bool isAdmin = IsAdministrator();
        string customDir = "";

        foreach (var arg in args)
        {
            if (arg.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase))
            {
                customDir = arg.Substring(5).Trim('"', '\'');
            }
            else if (arg.StartsWith("--dir=", StringComparison.OrdinalIgnoreCase))
            {
                customDir = arg.Substring(6).Trim('"', '\'');
            }
        }

        string currentCulture = CultureInfo.CurrentUICulture.Name;
        bool isZhCn = currentCulture.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase) ||
                      currentCulture.StartsWith("zh-SG", StringComparison.OrdinalIgnoreCase);
        bool isJa = currentCulture.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
        bool isEn = currentCulture.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        string installerTitle = isZhCn ? "DiskMaster Pro 安装向导" :
                               isJa ? "DiskMaster Pro セットアップウィザード" :
                               isEn ? "DiskMaster Pro Setup Wizard" :
                               "DiskMaster Pro 安裝精靈";

        // Determine mode and default target directory
        if (!isAllUsers && !isCurrentUser)
        {
            if (!isSilent)
            {
                if (isAdmin)
                {
                    // Already running as administrator: default to All Users (Program Files)
                    isAllUsers = true;
                    string defaultAdminDir = !string.IsNullOrEmpty(customDir)
                        ? customDir
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DiskMaster Pro");

                    string prompt = isZhCn
                        ? $"欢迎使用 DiskMaster Pro 旗舰版安装向导！\n\n当前以管理员身份运行。即将安装为全系统所有用户模式：\n目标文件夹：\n{defaultAdminDir}\n\n是否立即开始安装？"
                        : isJa
                        ? $"DiskMaster Pro Flagship セットアップウィザードへようこそ！\n\n管理者権限で実行中です。システム全体（すべてのユーザー）向けにインストールします：\n対象フォルダー：\n{defaultAdminDir}\n\n今すぐインストールを開始しますか？"
                        : isEn
                        ? $"Welcome to the DiskMaster Pro Flagship Setup Wizard!\n\nRunning with Administrator privileges. Setup will install for all users:\nDestination folder:\n{defaultAdminDir}\n\nDo you want to proceed with the installation?"
                        : $"歡迎使用 DiskMaster Pro 旗艦版安裝精靈！\n\n目前以系統管理員身分執行。即將安裝為全系統所有使用者模式：\n目標資料夾：\n{defaultAdminDir}\n\n是否立即開始安裝？";

                    int res = MessageBox(IntPtr.Zero, prompt, installerTitle, MB_YESNO | MB_ICONQUESTION);
                    if (res != IDYES) return;
                }
                else
                {
                    // Not running as administrator: Offer user choice
                    string defaultUserDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DiskMaster Pro");
                    string defaultAdminDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DiskMaster Pro");

                    string choicePrompt = isZhCn
                        ? $"欢迎使用 DiskMaster Pro 旗舰版安装向导！\n\n请选择安装范围与模式：\n\n【是 (Yes)】仅为当前用户安装 (无需管理员权限)\n   路径：{defaultUserDir}\n\n【否 (No)】提升为管理员并为所有用户安装 (推荐)\n   路径：{defaultAdminDir}\n\n【取消 (Cancel)】退出安装向导"
                        : isJa
                        ? $"DiskMaster Pro Flagship セットアップウィザードへようこそ！\n\nインストール範囲を選択してください：\n\n【はい (Yes)】現在のユーザーのみにインストール (管理者権限不要)\n   場所：{defaultUserDir}\n\n【いいえ (No)】管理者として再起動し、すべてのユーザー向けにインストール (推奨)\n   場所：{defaultAdminDir}\n\n【キャンセル (Cancel)】セットアップを終了"
                        : isEn
                        ? $"Welcome to the DiskMaster Pro Flagship Setup Wizard!\n\nSelect installation scope:\n\n[Yes] Install for Current User only (No admin privileges required)\n   Path: {defaultUserDir}\n\n[No] Relaunch with UAC Elevation for All Users (Recommended for disk operations)\n   Path: {defaultAdminDir}\n\n[Cancel] Exit Setup"
                        : $"歡迎使用 DiskMaster Pro 旗艦版安裝精靈！\n\n請選擇安裝範圍與模式：\n\n【是 (Yes)】僅為目前使用者安裝 (不需要管理員權限)\n   路徑：{defaultUserDir}\n\n【否 (No)】提升為系統管理員並為所有使用者安裝 (推薦)\n   路徑：{defaultAdminDir}\n\n【取消 (Cancel)】退出安裝精靈";

                    int choice = MessageBox(IntPtr.Zero, choicePrompt, installerTitle, MB_YESNOCANCEL | MB_ICONQUESTION);
                    if (choice == IDYES)
                    {
                        isCurrentUser = true;
                        isAllUsers = false;
                    }
                    else if (choice == IDNO)
                    {
                        // Relaunch self with UAC elevation
                        try
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = Environment.ProcessPath ?? "DiskMaster_Setup.exe",
                                Arguments = "/ALLUSERS " + string.Join(" ", args.Where(a => !a.Equals("/CURRENTUSER", StringComparison.OrdinalIgnoreCase) && !a.Equals("--currentuser", StringComparison.OrdinalIgnoreCase))),
                                UseShellExecute = true,
                                Verb = "runas"
                            };
                            Process.Start(psi);
                            return;
                        }
                        catch
                        {
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }
            }
            else
            {
                // Silent default
                if (isAdmin) isAllUsers = true;
                else isCurrentUser = true;
            }
        }
        else if (isAllUsers && !isAdmin)
        {
            // Explicitly requested All Users but not elevated: Relaunch with runas
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath ?? "DiskMaster_Setup.exe",
                    Arguments = string.Join(" ", args),
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                return;
            }
            catch (Exception ex)
            {
                if (!isSilent) MessageBox(IntPtr.Zero, $"需要系統管理員權限才能為所有使用者安裝：\n{ex.Message}", installerTitle, MB_OK | MB_ICONERROR);
                return;
            }
        }

        string targetDir = !string.IsNullOrEmpty(customDir)
            ? customDir
            : (isAllUsers
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DiskMaster Pro")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DiskMaster Pro"));

        try
        {
            // 1. Terminate running instances
            foreach (var procName in new[] { "DiskMasterWinUI", "DiskMaster_Portable" })
            {
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }

            // 2. Prepare destination folder
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // 3. Extract payload.zip
            var assembly = Assembly.GetExecutingAssembly();
            var payloadStream = assembly.GetManifestResourceStream("payload.zip")
                ?? assembly.GetManifestResourceNames()
                    .Where(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase))
                    .Select(assembly.GetManifestResourceStream)
                    .FirstOrDefault();

            if (payloadStream == null)
            {
                if (!isSilent) MessageBox(IntPtr.Zero, "安裝程式中找不到內嵌檔案 (payload.zip)！", "錯誤", MB_OK | MB_ICONERROR);
                return;
            }

            using (var zipArchive = new ZipArchive(payloadStream, ZipArchiveMode.Read))
            {
                foreach (var entry in zipArchive.Entries)
                {
                    string fullPath = Path.Combine(targetDir, entry.FullName);
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(fullPath);
                        continue;
                    }

                    string? parentDir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    entry.ExtractToFile(fullPath, overwrite: true);
                }
            }

            // 4. Trust Certificate if embedded
            try
            {
                var certStream = assembly.GetManifestResourceStream("DiskMaster_Certificate.cer")
                    ?? assembly.GetManifestResourceNames()
                        .Where(n => n.EndsWith("DiskMaster_Certificate.cer", StringComparison.OrdinalIgnoreCase))
                        .Select(assembly.GetManifestResourceStream)
                        .FirstOrDefault();

                if (certStream != null)
                {
                    using var ms = new MemoryStream();
                    certStream.CopyTo(ms);
                    var certBytes = ms.ToArray();
                    var cert = X509CertificateLoader.LoadCertificate(certBytes);

                    StoreLocation loc = (isAllUsers && IsAdministrator()) ? StoreLocation.LocalMachine : StoreLocation.CurrentUser;

                    using var rootStore = new X509Store(StoreName.Root, loc);
                    rootStore.Open(OpenFlags.ReadWrite);
                    rootStore.Add(cert);

                    using var pubStore = new X509Store(StoreName.TrustedPublisher, loc);
                    pubStore.Open(OpenFlags.ReadWrite);
                    pubStore.Add(cert);
                }
            }
            catch { }

            // 5. Create Desktop & Start Menu Shortcuts
            string exePath = Path.Combine(targetDir, "DiskMasterWinUI.exe");
            string uninstallExePath = Path.Combine(targetDir, "Uninstall.exe");

            if (File.Exists(exePath))
            {
                string desktopDir = isAllUsers
                    ? Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                if (!string.IsNullOrEmpty(desktopDir))
                {
                    CreateShortcut(Path.Combine(desktopDir, "DiskMaster Pro.lnk"), exePath, targetDir, "DiskMaster Pro - Advanced Windows Disk & Deployment Manager");
                }

                string startMenuDir = isAllUsers
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "DiskMaster Pro")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DiskMaster Pro");

                if (!Directory.Exists(startMenuDir)) Directory.CreateDirectory(startMenuDir);
                CreateShortcut(Path.Combine(startMenuDir, "DiskMaster Pro.lnk"), exePath, targetDir, "DiskMaster Pro");

                if (File.Exists(uninstallExePath))
                {
                    string uninstName = isZhCn ? "卸载 DiskMaster Pro.lnk" :
                                       isJa ? "DiskMaster Pro アンインストール.lnk" :
                                       isEn ? "Uninstall DiskMaster Pro.lnk" :
                                       "解除安裝 DiskMaster Pro.lnk";
                    CreateShortcut(Path.Combine(startMenuDir, uninstName), uninstallExePath, targetDir, "Uninstall DiskMaster Pro");
                }
            }

            // 6. Grant Administrator Mode (AppCompatFlags ~ RUNASADMIN)
            if (grantAdmin && File.Exists(exePath))
            {
                try
                {
                    using var hkcuKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", writable: true);
                    hkcuKey?.SetValue(exePath, "~ RUNASADMIN");
                }
                catch { }

                if (isAllUsers && IsAdministrator())
                {
                    try
                    {
                        using var hklmKey = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", writable: true);
                        hklmKey?.SetValue(exePath, "~ RUNASADMIN");
                    }
                    catch { }
                }
            }

            // 7. Register in Windows Add/Remove Programs (ARP)
            string uninstallCmd = File.Exists(uninstallExePath)
                ? $"\"{uninstallExePath}\""
                : $"\"{exePath}\" /UNINSTALL";

            string quietUninstallCmd = File.Exists(uninstallExePath)
                ? $"\"{uninstallExePath}\" /S"
                : $"\"{exePath}\" /UNINSTALL /S";

            RegisterInUninstall(isAllUsers, targetDir, exePath, uninstallCmd, quietUninstallCmd);

            // 8. Completion Notice
            if (!isSilent)
            {
                string finishMsg = isZhCn
                    ? "DiskMaster Pro 旗舰版已成功安装完成！\n\n已建立桌面与「开始」菜单快捷方式，并已配置管理员运行模式。"
                    : isJa
                    ? "DiskMaster Pro Flagship のインストールが完了しました！\n\nデスクトップおよびスタートメニューにショートカットが作成され、管理者権限実行モードが構成されました。"
                    : isEn
                    ? "DiskMaster Pro Flagship has been installed successfully!\n\nDesktop and Start Menu shortcuts created, and Administrator execution mode has been configured."
                    : "DiskMaster Pro 旗艦版已成功安裝完成！\n\n已建立桌面與「開始」功能表捷徑，並已配置系統管理員執行模式。";

                MessageBox(IntPtr.Zero, finishMsg, installerTitle, MB_OK | MB_ICONINFORMATION);
            }

            // 9. Launch Application if requested
            if (!noRestart && File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = targetDir,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            if (!isSilent)
            {
                MessageBox(IntPtr.Zero, $"安裝過程中發生錯誤：\n{ex.Message}", "安裝失敗", MB_OK | MB_ICONERROR);
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

    private static void RegisterInUninstall(bool isAllUsers, string targetDir, string exePath, string uninstallCmd, string quietUninstallCmd)
    {
        RegistryKey? baseKey = (isAllUsers && IsAdministrator()) ? Registry.LocalMachine : Registry.CurrentUser;
        try
        {
            using var key = baseKey.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskMasterPro");
            if (key != null)
            {
                key.SetValue("DisplayName", "DiskMaster Pro 旗艦版");
                key.SetValue("DisplayVersion", "1.4.2");
                key.SetValue("Publisher", "DiskMaster Team");
                key.SetValue("InstallLocation", targetDir);
                key.SetValue("DisplayIcon", $"{exePath},0");
                key.SetValue("UninstallString", uninstallCmd);
                key.SetValue("QuietUninstallString", quietUninstallCmd);
                key.SetValue("HelpLink", "https://github.com/Mangotimmy/DiskMasterWinUI");
                key.SetValue("URLInfoAbout", "https://github.com/Mangotimmy/DiskMasterWinUI");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
        catch { }
    }

    private static void PerformDirectUninstall(string[] args, bool isSilent)
    {
        string targetDir = AppContext.BaseDirectory;
        string uninstExe = Path.Combine(targetDir, "Uninstall.exe");
        if (File.Exists(uninstExe))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uninstExe,
                Arguments = isSilent ? "/S" : "",
                UseShellExecute = true
            });
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDir;
                shortcut.IconLocation = $"{targetPath},0";
                shortcut.Description = description;
                shortcut.Save();
            }
        }
        catch { }
    }
}
