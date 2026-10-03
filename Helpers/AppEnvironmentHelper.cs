using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Detects application execution environment (Portable single-file vs Windows Setup Installed),
/// machine architecture, and download asset resolution.
/// </summary>
public static class AppEnvironmentHelper
{
    private static readonly Lazy<bool> s_isInstallerMode = new(DetectInstallerMode);
    private static readonly Lazy<bool> s_isPortableMode = new(() => !s_isInstallerMode.Value);

    public static bool IsInstallerMode => s_isInstallerMode.Value;
    public static bool IsPortableMode => s_isPortableMode.Value;

    public static string ExecutionModeDescription => IsInstallerMode ? "Windows 安裝版 (Installer)" : "免安裝單檔便攜版 (Portable)";

    public static string CurrentProcessPath
    {
        get
        {
            try
            {
                return Environment.ProcessPath 
                    ?? Process.GetCurrentProcess().MainModule?.FileName 
                    ?? AppContext.BaseDirectory;
            }
            catch
            {
                return AppContext.BaseDirectory;
            }
        }
    }

    public static string ArchitectureName => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "x64"
    };

    public static string PreferredAssetName
    {
        get
        {
            bool isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            if (IsInstallerMode)
            {
                return isArm64 ? "DiskMaster_arm64_Setup.exe" : "DiskMaster_Setup.exe";
            }
            else
            {
                return isArm64 ? "DiskMaster_arm64_Portable.exe" : "DiskMaster_Portable.exe";
            }
        }
    }

    private static bool DetectInstallerMode()
    {
        try
        {
            string exePath = CurrentProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;

            // 1. Check if running from Program Files
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            if ((!string.IsNullOrEmpty(programFiles) && exePath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(programFilesX86) && exePath.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // 2. Check Windows Add/Remove Programs registry key
            using var hklmKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskMasterPro");
            if (hklmKey != null) return true;

            using var hkcuKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskMasterPro");
            if (hkcuKey != null) return true;
        }
        catch
        {
            // Silently fall back to portable
        }

        return false;
    }
}
