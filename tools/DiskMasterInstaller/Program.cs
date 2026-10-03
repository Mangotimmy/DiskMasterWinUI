using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace DiskMasterInstaller;

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
        bool isSilent = args.Any(a => a.Equals("/S", StringComparison.OrdinalIgnoreCase) || a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase) || a.Equals("-Silent", StringComparison.OrdinalIgnoreCase));
        bool noRestart = args.Any(a => a.Equals("/NORESTART", StringComparison.OrdinalIgnoreCase));
        
        string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DiskMaster Pro");
        
        foreach (var arg in args)
        {
            if (arg.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase))
            {
                targetDir = arg.Substring(5).Trim('"', '\'');
            }
        }

        if (!isSilent)
        {
            int res = MessageBox(
                IntPtr.Zero,
                $"歡迎使用 DiskMaster Pro 旗艦版安裝精靈！\n\n安裝目標資料夾：\n{targetDir}\n\n是否立即開始安裝？",
                "DiskMaster Pro 安裝精靈",
                MB_YESNO | MB_ICONQUESTION);

            if (res != IDYES)
            {
                return;
            }
        }

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
                    var cert = new X509Certificate2(certBytes);

                    using var rootStore = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                    rootStore.Open(OpenFlags.ReadWrite);
                    rootStore.Add(cert);

                    using var pubStore = new X509Store(StoreName.TrustedPublisher, StoreLocation.LocalMachine);
                    pubStore.Open(OpenFlags.ReadWrite);
                    pubStore.Add(cert);
                }
            }
            catch { }

            // 5. Create Shortcuts
            string exePath = Path.Combine(targetDir, "DiskMasterWinUI.exe");
            if (File.Exists(exePath))
            {
                // Desktop Shortcut
                string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(Path.Combine(desktopDir, "DiskMaster Pro.lnk"), exePath, targetDir, "DiskMaster Pro - Advanced Windows Disk & Deployment Manager");

                // Start Menu Shortcut
                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "DiskMaster Pro");
                if (!Directory.Exists(startMenuDir)) Directory.CreateDirectory(startMenuDir);
                CreateShortcut(Path.Combine(startMenuDir, "DiskMaster Pro.lnk"), exePath, targetDir, "DiskMaster Pro");
            }

            // 6. Create Uninstaller script and Register in Windows ARP
            string uninstallCmdPath = Path.Combine(targetDir, "uninstall.cmd");
            string uninstallScript = $@"@echo off
taskkill /f /im DiskMasterWinUI.exe >nul 2>&1
del /f /q ""%PUBLIC%\Desktop\DiskMaster Pro.lnk"" >nul 2>&1
del /f /q ""%USERPROFILE%\Desktop\DiskMaster Pro.lnk"" >nul 2>&1
rmdir /s /q ""%ProgramData%\Microsoft\Windows\Start Menu\Programs\DiskMaster Pro"" >nul 2>&1
reg delete ""HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskMasterPro"" /f >nul 2>&1
cd /d ""%TEMP%""
rmdir /s /q ""{targetDir}"" >nul 2>&1
echo DiskMaster Pro 已成功解除安裝。
";
            File.WriteAllText(uninstallCmdPath, uninstallScript);

            // Register in HKLM Uninstall
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskMasterPro");
                if (key != null)
                {
                    key.SetValue("DisplayName", "DiskMaster Pro 旗艦版");
                    key.SetValue("DisplayVersion", "1.3.0");
                    key.SetValue("Publisher", "DiskMaster Team");
                    key.SetValue("InstallLocation", targetDir);
                    key.SetValue("DisplayIcon", $"{exePath},0");
                    key.SetValue("UninstallString", $"\"{uninstallCmdPath}\"");
                    key.SetValue("QuietUninstallString", $"cmd.exe /c \"{uninstallCmdPath}\"");
                    key.SetValue("HelpLink", "https://github.com/Mangotimmy/DiskMasterWinUI");
                    key.SetValue("URLInfoAbout", "https://github.com/Mangotimmy/DiskMasterWinUI");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }
            catch { }

            if (!isSilent)
            {
                MessageBox(
                    IntPtr.Zero,
                    "DiskMaster Pro 旗艦版已成功安裝完成！\n\n已建立桌面與「開始」功能表捷徑。",
                    "安裝完成",
                    MB_OK | MB_ICONINFORMATION);
            }

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
