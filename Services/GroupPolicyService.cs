using System.Diagnostics;
using System.IO;
using DiskMasterWinUI.Helpers;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

public class GroupPolicyService
{
    private static readonly string GpeditMscPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "gpedit.msc");

    public bool IsGpeditAvailable() => File.Exists(GpeditMscPath);

    public (string EditionId, string EditionName, bool IsHomeEdition) GetWindowsEdition()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string id = key?.GetValue("EditionID")?.ToString() ?? "";
            string name = key?.GetValue("ProductName")?.ToString() ?? "";
            bool isHome = id.Contains("Home", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("Home", StringComparison.OrdinalIgnoreCase);
            return (id, name, isHome);
        }
        catch
        {
            return ("", "", false);
        }
    }

    public void OpenGpedit()
    {
        try
        {
            Process.Start(new ProcessStartInfo("gpedit.msc") { UseShellExecute = true });
        }
        catch { }
    }

    public async Task<string> RunGpupdateAsync()
    {
        try
        {
            var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("gpupdate.exe", "/force");
            return string.IsNullOrWhiteSpace(outStr) ? errStr : outStr;
        }
        catch (Exception ex)
        {
            return $"執行 gpupdate 失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// Installs Group Policy Client Extensions and Tools on Windows Home edition using DISM packages.
    /// </summary>
    public async Task<(bool Success, string Message)> InstallGpeditOnHomeEditionAsync(IProgress<string>? progress = null)
    {
        return await Task.Run(async () =>
        {
            try
            {
                string servicingDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "servicing", "Packages");
                if (!Directory.Exists(servicingDir))
                {
                    return (false, "找不到系統 servicing\\Packages 目錄。");
                }

                progress?.Report("正在搜尋群組原則安裝封裝 (*GroupPolicy*.mum)...");
                var pkgFiles = Directory.GetFiles(servicingDir, "*GroupPolicy*.mum")
                    .Where(f => f.Contains("ClientExtensions-Package") || f.Contains("ClientTools-Package"))
                    .ToList();

                if (pkgFiles.Count == 0)
                {
                    return (false, "系統封裝庫中未找到群組原則相關套件。");
                }

                int total = pkgFiles.Count;
                int current = 0;

                foreach (var pkg in pkgFiles)
                {
                    current++;
                    string pkgName = Path.GetFileName(pkg);
                    progress?.Report($"[{current}/{total}] 正在透過 DISM 載入套件: {pkgName}...");

                    var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("dism.exe", $"/online /norestart /add-package:\"{pkg}\"");
                    if (code != 0 && !outStr.Contains("100.0%"))
                    {
                        // Some architecture variants may not apply; continue trying other packages
                    }
                }

                progress?.Report("正在驗證 gpedit.msc 是否成功產生...");
                await Task.Delay(1000);

                if (IsGpeditAvailable())
                {
                    return (true, "🎉 成功為 Windows 家用版安裝並啟用群組原則編輯器 (gpedit.msc)！");
                }
                else
                {
                    return (false, "套件安裝完成，但 gpedit.msc 尚未就緒，請重新啟動電腦後檢查。");
                }
            }
            catch (Exception ex)
            {
                return (false, $"安裝過程發生例外: {ex.Message}");
            }
        });
    }

    // ══════════════════════════════════════════════════════════════════
    //  8 Essential Visual Group Policies (Registry-Backed Policies)
    // ══════════════════════════════════════════════════════════════════

    // 1. Windows Update Force Reboot
    public bool GetDisableAutoRebootPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU");
            return key?.GetValue("NoAutoRebootWithLoggedOnUsers") as int? == 1;
        }
        catch { return false; }
    }

    public bool SetDisableAutoRebootPolicy(bool disable)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", true);
            if (disable)
            {
                key.SetValue("NoAutoRebootWithLoggedOnUsers", 1, RegistryValueKind.DWord);
                key.SetValue("AUOptions", 2, RegistryValueKind.DWord); // Notify before download
            }
            else
            {
                key.DeleteValue("NoAutoRebootWithLoggedOnUsers", false);
                key.DeleteValue("AUOptions", false);
            }
            return true;
        }
        catch { return false; }
    }

    // 2. Disable Windows Telemetry / Diagnostics
    public bool GetDisableTelemetryPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
            return key?.GetValue("AllowTelemetry") as int? == 0;
        }
        catch { return false; }
    }

    public bool SetDisableTelemetryPolicy(bool disable)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", true);
            key.SetValue("AllowTelemetry", disable ? 0 : 3, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // 3. Disable Start Menu Bing Search Suggestions
    public bool GetDisableSearchBoxSuggestionsPolicy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Explorer");
            return key?.GetValue("DisableSearchBoxSuggestions") as int? == 1;
        }
        catch { return false; }
    }

    public bool SetDisableSearchBoxSuggestionsPolicy(bool disable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Explorer", true);
            if (disable) key.SetValue("DisableSearchBoxSuggestions", 1, RegistryValueKind.DWord);
            else key.DeleteValue("DisableSearchBoxSuggestions", false);
            return true;
        }
        catch { return false; }
    }

    // 4. Disable Consumer Cloud Content (CandyCrush, Sponsored Apps)
    public bool GetDisableConsumerFeaturesPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent");
            return key?.GetValue("DisableWindowsConsumerFeatures") as int? == 1;
        }
        catch { return false; }
    }

    public bool SetDisableConsumerFeaturesPolicy(bool disable)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", true);
            key.SetValue("DisableWindowsConsumerFeatures", disable ? 1 : 0, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // 5. Disable Delivery Optimization P2P upload
    public bool GetDisableDeliveryOptimizationPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization");
            return key?.GetValue("DODownloadMode") as int? == 0;
        }
        catch { return false; }
    }

    public bool SetDisableDeliveryOptimizationPolicy(bool disable)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", true);
            key.SetValue("DODownloadMode", disable ? 0 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // 6. Disable UAC Secure Desktop Dimming
    public bool GetDisableUacSecureDesktopPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            return key?.GetValue("PromptOnSecureDesktop") as int? == 0;
        }
        catch { return false; }
    }

    public bool SetDisableUacSecureDesktopPolicy(bool disable)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true);
            key.SetValue("PromptOnSecureDesktop", disable ? 0 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // 7. Disable Windows Defender Cloud Sample Auto Submission
    public bool GetDisableDefenderSampleSubmissionPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet");
            return key?.GetValue("SubmitSamplesConsent") as int? == 2;
        }
        catch { return false; }
    }

    public bool SetDisableDefenderSampleSubmissionPolicy(bool disable)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", true);
            key.SetValue("SubmitSamplesConsent", disable ? 2 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    // 8. Force Hardware-Accelerated GPU Scheduling (HAGS)
    public bool GetForceHagsPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            return key?.GetValue("HwSchMode") as int? == 2;
        }
        catch { return false; }
    }

    public bool SetForceHagsPolicy(bool enable)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
            key.SetValue("HwSchMode", enable ? 2 : 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }
}
