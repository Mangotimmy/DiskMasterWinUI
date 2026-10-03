using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

public static class AdminHelper
{
    public static bool IsWinPE()
    {
        try
        {
            var sysDrive = Environment.GetEnvironmentVariable("SystemDrive");
            if (string.Equals(sysDrive, "X:", StringComparison.OrdinalIgnoreCase))
                return true;

            using var miniNt = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT");
            return miniNt != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsRunningAsAdmin()
    {
        if (IsWinPE()) return true;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator) || identity.IsSystem;
        }
        catch
        {
            return false;
        }
    }

    public static void RestartAsAdmin()
    {
        if (IsWinPE()) return; // Already SYSTEM in WinPE

        var exePath = Environment.ProcessPath;
        if (exePath == null) return;

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            Process.Start(startInfo);
            Environment.Exit(0);
        }
        catch
        {
            // User canceled UAC
        }
    }
}
