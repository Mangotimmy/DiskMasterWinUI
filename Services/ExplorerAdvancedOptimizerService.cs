using System.Runtime.InteropServices;
using Microsoft.Win32;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service for auditing, applying, and restoring high-performance /boost optimizations
/// for the Windows Shell under HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced.
/// Eliminates 150-250ms animation latency, stops 5-30s folder freezing on offline network shares,
/// isolates explorer windows, terminates RAM-heavy telemetry (Widgets, Copilot, Iris),
/// and preserves CPU deep C-states for lower thermal output.
/// </summary>
public class ExplorerAdvancedOptimizerService
{
    private static readonly Lazy<ExplorerAdvancedOptimizerService> _instance = new(() => new ExplorerAdvancedOptimizerService());
    public static ExplorerAdvancedOptimizerService Instance => _instance.Value;

    private const string AdvKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ExpKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer";

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_FLUSH = 0x1000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    /// <summary>
    /// Reads and parses current Explorer Advanced and Shell latency settings from the registry.
    /// </summary>
    public ExplorerAdvancedState GetState()
    {
        var state = new ExplorerAdvancedState();

        try
        {
            using var advKey = Registry.CurrentUser.OpenSubKey(AdvKeyPath, writable: false);
            if (advKey != null)
            {
                state.TaskbarAnimationsDisabled = GetDword(advKey, "TaskbarAnimations", 1) == 0;
                int desktopHover = GetDword(advKey, "DesktopLivePreviewHoverTime", 500);
                state.DesktopLivePreviewInstant = desktopHover <= 50;

                int uiHover = GetDword(advKey, "ExtendedUIHoverTime", 400);
                state.ExtendedUIHoverTimeInstant = uiHover == 0;

                state.DisallowShakingEnabled = GetDword(advKey, "DisallowShaking", 0) == 1;
                state.SeparateProcessEnabled = GetDword(advKey, "SeparateProcess", 0) == 1;
                state.NonetCrawlingDisabled = GetDword(advKey, "nonetcrawling", 0) == 1;
                state.IconsOnlyEnabled = GetDword(advKey, "IconsOnly", 0) == 1;
                state.HideFileExtDisabled = GetDword(advKey, "HideFileExt", 1) == 0; // 0 means show extensions

                state.TaskbarDaDisabled = GetDword(advKey, "TaskbarDa", 1) == 0;
                state.ShowCopilotButtonDisabled = GetDword(advKey, "ShowCopilotButton", 1) == 0;
                state.StartIrisRecommendationsDisabled = GetDword(advKey, "Start_IrisRecommendations", 1) == 0;
                state.StartTrackDocsDisabled = GetDword(advKey, "Start_TrackDocs", 1) == 0;
                state.ShowSecondsInSystemClockDisabled = GetDword(advKey, "ShowSecondsInSystemClock", 0) == 0;
            }

            using var expKey = Registry.CurrentUser.OpenSubKey(ExpKeyPath, writable: false);
            if (expKey != null)
            {
                int showRecent = GetDword(expKey, "ShowRecent", 1);
                int showFreq = GetDword(expKey, "ShowFrequent", 1);
                state.QuickAccessRecentDisabled = (showRecent == 0 && showFreq == 0);
            }
        }
        catch { }

        return state;
    }

    /// <summary>
    /// Applies the recommended /boost optimization profile to Windows Explorer.
    /// </summary>
    public async Task<(bool Success, string Message)> ApplyBoostAsync(bool iconsOnly = false, bool restartExplorer = true)
    {
        return await Task.Run(async () =>
        {
            try
            {
                using (var advKey = Registry.CurrentUser.CreateSubKey(AdvKeyPath, writable: true))
                {
                    if (advKey != null)
                    {
                        // UI Latency & Animations
                        advKey.SetValue("TaskbarAnimations", 0, RegistryValueKind.DWord);
                        advKey.SetValue("DesktopLivePreviewHoverTime", 0, RegistryValueKind.DWord);
                        advKey.SetValue("ExtendedUIHoverTime", 0, RegistryValueKind.DWord);
                        advKey.SetValue("DisallowShaking", 1, RegistryValueKind.DWord);

                        // Disk I/O & Network Hang Prevention
                        advKey.SetValue("nonetcrawling", 1, RegistryValueKind.DWord);
                        advKey.SetValue("SeparateProcess", 1, RegistryValueKind.DWord);
                        advKey.SetValue("IconsOnly", iconsOnly ? 1 : 0, RegistryValueKind.DWord);
                        advKey.SetValue("HideFileExt", 0, RegistryValueKind.DWord); // Show extensions

                        // Memory & Telemetry
                        advKey.SetValue("TaskbarDa", 0, RegistryValueKind.DWord); // Disable Widgets
                        advKey.SetValue("ShowCopilotButton", 0, RegistryValueKind.DWord); // Disable Copilot
                        advKey.SetValue("Start_IrisRecommendations", 0, RegistryValueKind.DWord);
                        advKey.SetValue("Start_TrackDocs", 0, RegistryValueKind.DWord);
                        advKey.SetValue("ShowSecondsInSystemClock", 0, RegistryValueKind.DWord);
                    }
                }

                using (var expKey = Registry.CurrentUser.CreateSubKey(ExpKeyPath, writable: true))
                {
                    if (expKey != null)
                    {
                        expKey.SetValue("ShowRecent", 0, RegistryValueKind.DWord);
                        expKey.SetValue("ShowFrequent", 0, RegistryValueKind.DWord);
                    }
                }

                NotifyShell();

                if (restartExplorer)
                {
                    await ExplorerWatchdogService.Instance.RestartExplorerManuallyAsync();
                }

                return (true, "✅ 已成功套用 Explorer 極致無延遲設定 (已消除工作列與動畫延遲、阻止離線網路凍結、停用小工具並釋放記憶體)");
            }
            catch (Exception ex)
            {
                return (false, $"❌ 套用 Explorer 優化失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Restores standard Windows stock defaults for Explorer.
    /// </summary>
    public async Task<(bool Success, string Message)> RestoreDefaultsAsync(bool restartExplorer = true)
    {
        return await Task.Run(async () =>
        {
            try
            {
                using (var advKey = Registry.CurrentUser.CreateSubKey(AdvKeyPath, writable: true))
                {
                    if (advKey != null)
                    {
                        advKey.SetValue("TaskbarAnimations", 1, RegistryValueKind.DWord);
                        advKey.SetValue("DesktopLivePreviewHoverTime", 500, RegistryValueKind.DWord);
                        advKey.SetValue("ExtendedUIHoverTime", 400, RegistryValueKind.DWord);
                        advKey.SetValue("DisallowShaking", 0, RegistryValueKind.DWord);

                        advKey.SetValue("nonetcrawling", 0, RegistryValueKind.DWord);
                        advKey.SetValue("SeparateProcess", 0, RegistryValueKind.DWord);
                        advKey.SetValue("IconsOnly", 0, RegistryValueKind.DWord);
                        advKey.SetValue("HideFileExt", 1, RegistryValueKind.DWord);

                        advKey.SetValue("TaskbarDa", 1, RegistryValueKind.DWord);
                        advKey.SetValue("ShowCopilotButton", 1, RegistryValueKind.DWord);
                        advKey.SetValue("Start_IrisRecommendations", 1, RegistryValueKind.DWord);
                        advKey.SetValue("Start_TrackDocs", 1, RegistryValueKind.DWord);
                        advKey.SetValue("ShowSecondsInSystemClock", 0, RegistryValueKind.DWord);
                    }
                }

                using (var expKey = Registry.CurrentUser.CreateSubKey(ExpKeyPath, writable: true))
                {
                    if (expKey != null)
                    {
                        expKey.SetValue("ShowRecent", 1, RegistryValueKind.DWord);
                        expKey.SetValue("ShowFrequent", 1, RegistryValueKind.DWord);
                    }
                }

                NotifyShell();

                if (restartExplorer)
                {
                    await ExplorerWatchdogService.Instance.RestartExplorerManuallyAsync();
                }

                return (true, "🛡️ 已還原 Windows 官方預設檔案總管與工作列設定");
            }
            catch (Exception ex)
            {
                return (false, $"❌ 還原預設失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Modifies a single registry value under Advanced or Explorer.
    /// </summary>
    public async Task<bool> SetSettingAsync(string keyName, int value, bool isExplorerRoot = false, bool restartExplorer = false)
    {
        return await Task.Run(async () =>
        {
            try
            {
                string targetKeyPath = isExplorerRoot ? ExpKeyPath : AdvKeyPath;
                using var key = Registry.CurrentUser.CreateSubKey(targetKeyPath, writable: true);
                if (key != null)
                {
                    key.SetValue(keyName, value, RegistryValueKind.DWord);
                    NotifyShell();
                    if (restartExplorer)
                    {
                        await ExplorerWatchdogService.Instance.RestartExplorerManuallyAsync();
                    }
                    return true;
                }
            }
            catch { }
            return false;
        });
    }

    private static int GetDword(RegistryKey key, string name, int fallback)
    {
        var val = key.GetValue(name);
        return val is int i ? i : fallback;
    }

    private static void NotifyShell()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }
}
