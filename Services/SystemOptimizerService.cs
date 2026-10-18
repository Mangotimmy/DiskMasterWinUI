using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public class SystemOptimizerService
{
    private static readonly string BackupDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskMaster_Portable",
        "RegistryBackups");

    public SystemOptimizerService()
    {
        try { Directory.CreateDirectory(BackupDir); } catch { }
    }

    public string GetBackupDirectory() => BackupDir;

    // ══════════════════════════════════════════════════════════
    //  1. CPU Scheduling: Win32PrioritySeparation & Throttling
    // ══════════════════════════════════════════════════════════

    public int GetWin32PrioritySeparation()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
            if (key != null)
            {
                var val = key.GetValue("Win32PrioritySeparation");
                if (val is int intVal) return intVal;
            }
        }
        catch { }
        return 2; // Windows stock default
    }

    public bool SetWin32PrioritySeparation(int value)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", writable: true);
            if (key != null)
            {
                key.SetValue("Win32PrioritySeparation", value, RegistryValueKind.DWord);
                return true;
            }
        }
        catch { }
        return false;
    }

    public bool GetPowerThrottlingDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling");
            if (key != null)
            {
                var val = key.GetValue("PowerThrottlingOff");
                if (val is int intVal) return intVal == 1;
            }
        }
        catch { }
        return false;
    }

    public bool SetPowerThrottlingDisabled(bool disabled)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", writable: true);
            if (key != null)
            {
                key.SetValue("PowerThrottlingOff", disabled ? 1 : 0, RegistryValueKind.DWord);
                return true;
            }
        }
        catch { }
        return false;
    }

    // ══════════════════════════════════════════════════════════
    //  2. MMCSS & Network Latency
    // ══════════════════════════════════════════════════════════

    public int GetSystemResponsiveness()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
            if (key != null)
            {
                var val = key.GetValue("SystemResponsiveness");
                if (val is int intVal) return intVal;
            }
        }
        catch { }
        return 20; // Windows default reserved 20%
    }

    public bool SetSystemResponsiveness(int percent)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: true);
            if (key != null)
            {
                key.SetValue("SystemResponsiveness", percent, RegistryValueKind.DWord);
                return true;
            }
        }
        catch { }
        return false;
    }

    public bool GetNetworkThrottlingDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
            if (key != null)
            {
                var val = key.GetValue("NetworkThrottlingIndex");
                if (val is int intVal) return (uint)intVal == 0xFFFFFFFF || intVal == -1;
            }
        }
        catch { }
        return false;
    }

    public bool SetNetworkThrottlingDisabled(bool disabled)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: true);
            if (key != null)
            {
                int val = disabled ? unchecked((int)0xFFFFFFFF) : 10;
                key.SetValue("NetworkThrottlingIndex", val, RegistryValueKind.DWord);
                return true;
            }
        }
        catch { }
        return false;
    }

    public bool ConfigureMmcssGamesTask(bool highPriority)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", writable: true);
            if (key != null)
            {
                key.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                key.SetValue("Priority", highPriority ? 6 : 2, RegistryValueKind.DWord);
                key.SetValue("Scheduling Category", highPriority ? "High" : "Medium", RegistryValueKind.String);
                key.SetValue("SFIO Priority", highPriority ? "High" : "Normal", RegistryValueKind.String);
                key.SetValue("Clock Rate", 10000, RegistryValueKind.DWord);
                key.SetValue("Affinity", 0, RegistryValueKind.DWord);
                key.SetValue("Background Only", "False", RegistryValueKind.String);
                return true;
            }
        }
        catch { }
        return false;
    }

    // ══════════════════════════════════════════════════════════
    //  3. Gaming Mode: Game DVR, HAGS, Low Input Latency
    // ══════════════════════════════════════════════════════════

    public bool GetGameDvrDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
            if (key != null)
            {
                var val = key.GetValue("GameDVR_Enabled");
                if (val is int intVal) return intVal == 0;
            }
        }
        catch { }
        return false;
    }

    public bool SetGameDvrDisabled(bool disabled)
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore", writable: true))
            {
                if (key != null)
                {
                    key.SetValue("GameDVR_Enabled", disabled ? 0 : 1, RegistryValueKind.DWord);
                    key.SetValue("GameDVR_FSEBehaviorMode", disabled ? 2 : 0, RegistryValueKind.DWord);
                    key.SetValue("GameDVR_HonorUserFSEBehaviorMode", disabled ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("GameDVR_DXGIHonorFSEWindowsCompatible", disabled ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("GameDVR_EFSEFeatureFlags", 0, RegistryValueKind.DWord);
                }
            }
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", writable: true))
            {
                if (key != null)
                {
                    key.SetValue("AppCaptureEnabled", disabled ? 0 : 1, RegistryValueKind.DWord);
                }
            }
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", writable: true);
                key?.SetValue("AllowGameDVR", disabled ? 0 : 1, RegistryValueKind.DWord);
            }
            catch { }
            return true;
        }
        catch { }
        return false;
    }

    public bool GetHagsStatus()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            if (key != null)
            {
                var val = key.GetValue("HwSchMode");
                if (val is int intVal) return intVal == 2;
            }
        }
        catch { }
        return false;
    }

    public bool SetHagsStatus(bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", writable: true);
            if (key != null)
            {
                key.SetValue("HwSchMode", enabled ? 2 : 1, RegistryValueKind.DWord);
                return true;
            }
        }
        catch { }
        return false;
    }

    public bool ConfigureInputLatency(bool optimize)
    {
        try
        {
            // Mouse
            using (var mouseKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse", writable: true))
            {
                if (mouseKey != null)
                {
                    mouseKey.SetValue("MouseSpeed", optimize ? "0" : "1", RegistryValueKind.String);
                    mouseKey.SetValue("MouseThreshold1", optimize ? "0" : "6", RegistryValueKind.String);
                    mouseKey.SetValue("MouseThreshold2", optimize ? "0" : "10", RegistryValueKind.String);
                }
            }
            // Keyboard
            using (var kbKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Keyboard", writable: true))
            {
                if (kbKey != null)
                {
                    kbKey.SetValue("KeyboardDelay", optimize ? "0" : "1", RegistryValueKind.String);
                    kbKey.SetValue("KeyboardSpeed", "31", RegistryValueKind.String);
                }
            }
            // Desktop Menu Show Delay
            using (var deskKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", writable: true))
            {
                if (deskKey != null)
                {
                    deskKey.SetValue("MenuShowDelay", optimize ? "0" : "400", RegistryValueKind.String);
                }
            }
            return true;
        }
        catch { }
        return false;
    }

    // ══════════════════════════════════════════════════════════
    //  3.5 Advanced Latency, Memory & Group Policy Tweaks
    // ══════════════════════════════════════════════════════════

    public bool GetNagleDisabled()
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
            if (baseKey != null)
            {
                foreach (var subName in baseKey.GetSubKeyNames())
                {
                    using var ifKey = baseKey.OpenSubKey(subName);
                    if (ifKey != null)
                    {
                        var ack = ifKey.GetValue("TcpAckFrequency");
                        var nodelay = ifKey.GetValue("TCPNoDelay");
                        if (ack is int ackVal && nodelay is int ndVal && ackVal == 1 && ndVal == 1)
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch { }
        return false;
    }

    public bool ConfigureNagleAlgorithm(bool disableNagle)
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", writable: true);
            if (baseKey != null)
            {
                foreach (var subName in baseKey.GetSubKeyNames())
                {
                    using var ifKey = baseKey.OpenSubKey(subName, writable: true);
                    if (ifKey != null)
                    {
                        if (disableNagle)
                        {
                            ifKey.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                            ifKey.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                        }
                        else
                        {
                            try { ifKey.DeleteValue("TcpAckFrequency"); } catch { }
                            try { ifKey.DeleteValue("TCPNoDelay"); } catch { }
                        }
                    }
                }
                return true;
            }
        }
        catch { }
        return false;
    }

    public bool GetDisablePagingExecutive()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            if (key != null)
            {
                var val = key.GetValue("DisablePagingExecutive");
                if (val is int intVal) return intVal == 1;
            }
        }
        catch { }
        return false;
    }

    public bool ConfigureMemoryManagement(bool optimize)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", writable: true);
            if (key != null)
            {
                key.SetValue("DisablePagingExecutive", optimize ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("LargeSystemCache", optimize ? 1 : 0, RegistryValueKind.DWord);
                return true;
            }
        }
        catch { }
        return false;
    }

    public bool GetTelemetryDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
            if (key != null)
            {
                var val = key.GetValue("AllowTelemetry");
                if (val is int intVal) return intVal == 0;
            }
        }
        catch { }
        return false;
    }

    public bool ConfigureGroupPolicyPrivacy(bool disableTelemetryAndAds)
    {
        try
        {
            // 1. Data Collection / Telemetry
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", writable: true))
            {
                key?.SetValue("AllowTelemetry", disableTelemetryAndAds ? 0 : 3, RegistryValueKind.DWord);
            }
            // 2. Search Box Suggestions (Bing in Start)
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\Explorer", writable: true))
            {
                key?.SetValue("DisableSearchBoxSuggestions", disableTelemetryAndAds ? 1 : 0, RegistryValueKind.DWord);
            }
            // 3. Cloud Content / Consumer Features
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", writable: true))
            {
                key?.SetValue("DisableWindowsConsumerFeatures", disableTelemetryAndAds ? 1 : 0, RegistryValueKind.DWord);
            }
            // 4. Windows Error Reporting UI Hang
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", writable: true))
            {
                key?.SetValue("DontShowUI", disableTelemetryAndAds ? 1 : 0, RegistryValueKind.DWord);
            }
            // 5. Delivery Optimization P2P upload
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", writable: true))
            {
                key?.SetValue("DODownloadMode", disableTelemetryAndAds ? 0 : 1, RegistryValueKind.DWord);
            }
            return true;
        }
        catch { }
        return false;
    }

    // ══════════════════════════════════════════════════════════
    //  3.8 L2TP/IPsec VPN NAT-T Fix, DPS Memory Bloat & DiagTrack
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Reads HKLM\SYSTEM\CurrentControlSet\Services\PolicyAgent\AssumeUDPEncapsulationContextOnSendRule.
    /// 0 = Not configured (default), 1 = Server behind NAT only, 2 = Both Server &amp; Client behind NAT (Recommended VPN Fix).
    /// </summary>
    public int GetL2tpVpnNatRuleValue()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PolicyAgent");
            if (key != null)
            {
                var val = key.GetValue("AssumeUDPEncapsulationContextOnSendRule");
                if (val is int intVal) return intVal;
            }
        }
        catch { }
        return 0;
    }

    public bool GetL2tpVpnNatFixEnabled() => GetL2tpVpnNatRuleValue() == 2;

    /// <summary>
    /// Configures HKLM\SYSTEM\CurrentControlSet\Services\PolicyAgent\AssumeUDPEncapsulationContextOnSendRule (REG_DWORD = 2)
    /// to fix L2TP/IPsec VPN connection errors (Error 789 / 809) when the PC or VPN server is behind a NAT router.
    /// </summary>
    public bool SetL2tpVpnNatFix(bool enableNatTraversal)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\PolicyAgent", writable: true);
            if (key != null)
            {
                if (enableNatTraversal)
                {
                    key.SetValue("AssumeUDPEncapsulationContextOnSendRule", 2, RegistryValueKind.DWord);
                }
                else
                {
                    try { key.DeleteValue("AssumeUDPEncapsulationContextOnSendRule", throwOnMissingValue: false); } catch { }
                }

                // Also ensure RasMan\Parameters ProhibitIpSec is 0 when enabling L2TP/IPsec
                if (enableNatTraversal)
                {
                    try
                    {
                        using var rasKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\RasMan\Parameters", writable: true);
                        rasKey?.SetValue("ProhibitIpSec", 0, RegistryValueKind.DWord);
                        rasKey?.SetValue("AllowL2TPWeakCrypto", 1, RegistryValueKind.DWord);
                    }
                    catch { }
                }
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Checks whether DPS (Diagnostic Policy Service) is disabled (Start = 4) under HKLM\SYSTEM\CurrentControlSet\Services\DPS.
    /// Disabling DPS prevents background diagnostic troubleshooting memory bloat and svchost CPU/RAM spikes.
    /// </summary>
    public bool GetDpsServiceDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DPS");
            if (key != null)
            {
                var val = key.GetValue("Start");
                if (val is int intVal) return intVal == 4;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Sets HKLM\SYSTEM\CurrentControlSet\Services\DPS Start = 4 (Disabled) or 2 (Automatic) and stops/starts the service.
    /// </summary>
    public async Task<(bool Success, string Message)> SetDpsServiceDisabledAsync(bool disable)
    {
        try
        {
            int targetStart = disable ? 4 : 2;
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DPS", writable: true))
            {
                if (key == null)
                    return (false, "❌ 無法存取 HKLM\\SYSTEM\\CurrentControlSet\\Services\\DPS (請以系統管理員身分執行)。");
                key.SetValue("Start", targetStart, RegistryValueKind.DWord);
            }

            if (disable)
            {
                await ProcessHelper.RunProcessAsync("sc.exe", "stop DPS");
                await ProcessHelper.RunProcessAsync("sc.exe", "config DPS start= disabled");
                return (true, "✅ 已停用 DPS (Diagnostic Policy Service，Start = 4) 並停止背景服務，徹底消除背景診斷記憶體膨脹！");
            }
            else
            {
                await ProcessHelper.RunProcessAsync("sc.exe", "config DPS start= auto");
                await ProcessHelper.RunProcessAsync("sc.exe", "start DPS");
                return (true, "✅ 已還原 DPS (Diagnostic Policy Service，Start = 2 自動啟動) 並啟動服務。");
            }
        }
        catch (Exception ex)
        {
            return (false, $"❌ 設定 DPS 服務失敗 (需要系統管理員權限): {ex.Message}");
        }
    }

    /// <summary>
    /// Checks whether DiagTrack (Connected User Experiences and Telemetry) is disabled (Start = 4).
    /// </summary>
    public bool GetDiagTrackDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack");
            if (key != null)
            {
                var val = key.GetValue("Start");
                if (val is int intVal) return intVal == 4;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Turns Off ("sc stop DiagTrack &amp; sc config DiagTrack start=disabled") or Turns On
    /// ("sc config DiagTrack start=auto &amp; sc start DiagTrack") the Connected User Experiences and Telemetry service.
    /// Windows Update continues to work normally with DiagTrack disabled.
    /// </summary>
    public async Task<(bool Success, string Message)> SetDiagTrackServiceDisabledAsync(bool disable)
    {
        try
        {
            if (disable)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack", writable: true);
                    key?.SetValue("Start", 4, RegistryValueKind.DWord);
                }
                catch { }

                var (_, err1, code1) = await ProcessHelper.RunProcessAsync("sc.exe", "stop DiagTrack");
                var (out2, err2, code2) = await ProcessHelper.RunProcessAsync("sc.exe", "config DiagTrack start= disabled");

                if (code2 == 0 || GetDiagTrackDisabled())
                {
                    return (true, "✅ 已執行 `sc stop DiagTrack` 與 `sc config DiagTrack start= disabled`！遙測追蹤服務已徹底關閉 (Windows Update 依然完全正常運作)。");
                }
                return (false, $"❌ 關閉 DiagTrack 失敗 (請確認以系統管理員身分執行): {(string.IsNullOrWhiteSpace(err2) ? out2 : err2)}");
            }
            else
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack", writable: true);
                    key?.SetValue("Start", 2, RegistryValueKind.DWord);
                }
                catch { }

                var (out1, err1, code1) = await ProcessHelper.RunProcessAsync("sc.exe", "config DiagTrack start= auto");
                await ProcessHelper.RunProcessAsync("sc.exe", "start DiagTrack");

                if (code1 == 0 || !GetDiagTrackDisabled())
                {
                    return (true, "✅ 已執行 `sc config DiagTrack start= auto` 與 `sc start DiagTrack`！已恢復啟用 DiagTrack 服務。");
                }
                return (false, $"❌ 啟用 DiagTrack 失敗 (請確認以系統管理員身分執行): {(string.IsNullOrWhiteSpace(err1) ? out1 : err1)}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"❌ 操作 DiagTrack 服務失敗: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  4. 1-Click Gaming Mode / Restore Factory Defaults
    // ══════════════════════════════════════════════════════════

    public async Task<string> ApplyGamingProfileAsync(int win32PriorityVal = 38)
        => (await ApplyGamingProfileWithResultAsync(win32PriorityVal)).Log;

    /// <summary>
    /// Applies the one-click gaming profile and reports whether every registry write was accepted.
    /// A refused write (typically a non-elevated or policy-locked machine) is called out in the log
    /// instead of the previous unconditional "applied" text.
    /// </summary>
    public async Task<(bool Success, string Log)> ApplyGamingProfileWithResultAsync(int win32PriorityVal = 38)
    {
        var sb = new StringBuilder();
        bool allApplied = true;
        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🎮 正在建立登錄檔自動備份快照...");
        await CreatePreTweakBackupAsync("Before_GamingProfile");

        allApplied &= AppendStep(sb, $"⚡ 設定 Win32PrioritySeparation = 0x{win32PriorityVal:X} ({win32PriorityVal})...", SetWin32PrioritySeparation(win32PriorityVal));
        allApplied &= AppendStep(sb, "⚡ 停用 CPU 能源節流 (PowerThrottlingOff = 1)...", SetPowerThrottlingDisabled(true));
        allApplied &= AppendStep(sb, "🚀 設定 MMCSS SystemResponsiveness = 10 (保留 10% 系統穩定，90% 專注遊戲防微卡頓)...", SetSystemResponsiveness(10));
        allApplied &= AppendStep(sb, "🌐 停用網路節流佇列 (NetworkThrottlingIndex = 0xFFFFFFFF)...", SetNetworkThrottlingDisabled(true));
        allApplied &= AppendStep(sb, "🎮 提升 MMCSS Games 工作排程為 High Priority...", ConfigureMmcssGamesTask(true));
        allApplied &= AppendStep(sb, "🛡️ 關閉 Game DVR / Game Bar 背景錄影開銷 (GameDVR_Enabled = 0)...", SetGameDvrDisabled(true));
        allApplied &= AppendStep(sb, "🖥️ 啟用硬體加速 GPU 排程 (HAGS HwSchMode = 2)...", SetHagsStatus(true));
        allApplied &= AppendStep(sb, "🖱️ 最佳化滑鼠 1:1 感應器追蹤與按鍵零延遲...", ConfigureInputLatency(true));

        if (allApplied)
        {
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] ✅ 電競遊戲極致模式套用完成！建議重新開機以完全載入核心排程設定。");
        }
        else
        {
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ 部分登錄檔設定未套用：請以系統管理員身分重新執行 DiskMaster Pro 後再試。");
        }
        return (allApplied, sb.ToString());
    }

    public async Task<string> RestoreWindowsDefaultsAsync()
        => (await RestoreWindowsDefaultsWithResultAsync()).Log;

    /// <summary>
    /// Restores the Windows factory defaults and reports whether every registry write was accepted.
    /// </summary>
    public async Task<(bool Success, string Log)> RestoreWindowsDefaultsWithResultAsync()
    {
        var sb = new StringBuilder();
        bool allApplied = true;
        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🛡️ 正在建立登錄檔自動備份快照...");
        await CreatePreTweakBackupAsync("Before_RestoreDefaults");

        allApplied &= AppendStep(sb, "🔄 還原 Win32PrioritySeparation = 2 (Windows 預設)...", SetWin32PrioritySeparation(2));
        allApplied &= AppendStep(sb, "🔄 還原 CPU 能源節流 (預設啟用)...", SetPowerThrottlingDisabled(false));
        allApplied &= AppendStep(sb, "🔄 還原 MMCSS SystemResponsiveness = 20 (保留 20% 背景)...", SetSystemResponsiveness(20));
        allApplied &= AppendStep(sb, "🔄 還原 MMCSS NetworkThrottlingIndex = 10...", SetNetworkThrottlingDisabled(false));
        allApplied &= AppendStep(sb, "🔄 還原 MMCSS Games 工作排程為預設值...", ConfigureMmcssGamesTask(false));
        allApplied &= AppendStep(sb, "🔄 還原 Game DVR 與 Game Bar 為預設啟用...", SetGameDvrDisabled(false));
        allApplied &= AppendStep(sb, "🔄 還原硬體加速 GPU 排程 (HwSchMode = 1)...", SetHagsStatus(false));
        allApplied &= AppendStep(sb, "🔄 還原滑鼠/鍵盤/桌面選單為 Windows 原生設定...", ConfigureInputLatency(false));

        if (allApplied)
        {
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] ✅ 系統設定已全數安全還原為 Windows 官方出廠預設值！");
        }
        else
        {
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] ⚠️ 部分設定未還原：請以系統管理員身分重新執行 DiskMaster Pro 後再試。");
        }
        return (allApplied, sb.ToString());
    }

    /// <summary>
    /// Appends one tweak step to the log. Success keeps the original text unchanged; a refused
    /// registry write is reported explicitly so a profile run can never claim an unapplied tuning.
    /// </summary>
    private static bool AppendStep(StringBuilder sb, string description, bool applied)
    {
        sb.AppendLine(applied
            ? $"[{DateTime.Now:HH:mm:ss}] {description}"
            : $"[{DateTime.Now:HH:mm:ss}] ❌ 未套用 (需要系統管理員權限): {description}");
        return applied;
    }

    // ══════════════════════════════════════════════════════════
    //  5. Registry Backups & System Restore Points
    // ══════════════════════════════════════════════════════════

    public async Task<string> CreatePreTweakBackupAsync(string tag)
    {
        var fileName = $"{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.reg";
        var filePath = Path.Combine(BackupDir, fileName);
        return await ExportCoreRegistrySettingsAsync(filePath);
    }

    public async Task<string> CreateManualBackupAsync(string description)
    {
        var safeDesc = string.Concat(description.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        var fileName = $"Manual_{safeDesc}_{DateTime.Now:yyyyMMdd_HHmmss}.reg";
        var filePath = Path.Combine(BackupDir, fileName);
        return await ExportCoreRegistrySettingsAsync(filePath);
    }

    private async Task<string> ExportCoreRegistrySettingsAsync(string filePath)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Windows Registry Editor Version 5.00");
            sb.AppendLine();

            // 1. PriorityControl
            sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl]");
            sb.AppendLine($"\"Win32PrioritySeparation\"=dword:{GetWin32PrioritySeparation():x8}");
            sb.AppendLine();

            // 2. PowerThrottling
            sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling]");
            sb.AppendLine($"\"PowerThrottlingOff\"=dword:{(GetPowerThrottlingDisabled() ? 1 : 0):x8}");
            sb.AppendLine();

            // 3. MMCSS SystemProfile
            sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile]");
            sb.AppendLine($"\"SystemResponsiveness\"=dword:{GetSystemResponsiveness():x8}");
            sb.AppendLine($"\"NetworkThrottlingIndex\"=dword:{(GetNetworkThrottlingDisabled() ? unchecked((int)0xFFFFFFFF) : 10):x8}");
            sb.AppendLine();

            // 4. GraphicsDrivers HAGS
            sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers]");
            sb.AppendLine($"\"HwSchMode\"=dword:{(GetHagsStatus() ? 2 : 1):x8}");
            sb.AppendLine();

            // 5. GameConfigStore
            sb.AppendLine(@"[HKEY_CURRENT_USER\System\GameConfigStore]");
            sb.AppendLine($"\"GameDVR_Enabled\"=dword:{(GetGameDvrDisabled() ? 0 : 1):x8}");
            sb.AppendLine();

            // 6. PolicyAgent (L2TP/IPsec VPN NAT-T)
            sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\PolicyAgent]");
            sb.AppendLine($"\"AssumeUDPEncapsulationContextOnSendRule\"=dword:{GetL2tpVpnNatRuleValue():x8}");
            sb.AppendLine();

            // 7. DPS & DiagTrack Services
            sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\DPS]");
            sb.AppendLine($"\"Start\"=dword:{(GetDpsServiceDisabled() ? 4 : 2):x8}");
            sb.AppendLine();

            sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\DiagTrack]");
            sb.AppendLine($"\"Start\"=dword:{(GetDiagTrackDisabled() ? 4 : 2):x8}");
            sb.AppendLine();

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.Unicode);
            return filePath;
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    public async Task<string> RestoreRegistryFileAsync(string filePath)
    {
        if (!File.Exists(filePath)) return "ERROR: 檔案不存在";
        var (output, err, code) = await ProcessHelper.RunProcessAsync("reg.exe", $"import \"{filePath}\"");
        return code == 0 ? "Success" : string.IsNullOrWhiteSpace(err) ? output : err;
    }

    public List<RegistryBackupItem> ListBackups()
    {
        var list = new List<RegistryBackupItem>();
        try
        {
            if (Directory.Exists(BackupDir))
            {
                var files = Directory.GetFiles(BackupDir, "*.reg");
                foreach (var f in files)
                {
                    var fi = new FileInfo(f);
                    list.Add(new RegistryBackupItem
                    {
                        FileName = fi.Name,
                        FilePath = fi.FullName,
                        CreatedAt = fi.CreationTime,
                        FileSizeBytes = fi.Length,
                        Description = fi.Name.StartsWith("Manual_") ? "手動備份" : "調節前自動快照"
                    });
                }
            }
        }
        catch { }
        return list.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public bool DeleteBackup(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
        }
        catch { }
        return false;
    }

    public async Task<(bool Success, string Message)> CreateSystemRestorePointAsync(string description)
    {
        try
        {
            var psCmd = $"Checkpoint-Computer -Description \"{description.Replace("\"", "'")}\" -RestorePointType \"MODIFY_SETTINGS\"";
            var (output, err, code) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"{psCmd}\"");
            if (code == 0)
            {
                return (true, "已成功建立 Windows 系統還原點！");
            }
            return (false, string.IsNullOrWhiteSpace(err) ? output : err);
        }
        catch (Exception ex)
        {
            return (false, $"建立系統還原點失敗: {ex.Message}");
        }
    }

    public async Task<List<SystemRestorePointItem>> ListSystemRestorePointsAsync()
    {
        var list = new List<SystemRestorePointItem>();
        try
        {
            var psCmd = "Get-ComputerRestorePoint | Select-Object SequenceNumber, Description, CreationTime, RestorePointType | ConvertTo-Csv -NoTypeInformation";
            var (output, _, code) = await ProcessHelper.RunProcessAsync("powershell.exe", $"-NoProfile -Command \"{psCmd}\"");
            if (code == 0)
            {
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < lines.Length; i++)
                {
                    var parts = lines[i].Split(',');
                    if (parts.Length >= 4)
                    {
                        var seqStr = parts[0].Trim('\"');
                        int.TryParse(seqStr, out var seq);
                        list.Add(new SystemRestorePointItem
                        {
                            SequenceNumber = seq,
                            Description = parts[1].Trim('\"'),
                            CreationTime = parts[2].Trim('\"'),
                            RestorePointType = parts[3].Trim('\"')
                        });
                    }
                }
            }
        }
        catch { }
        return list;
    }

    // ══════════════════════════════════════════════════════════
    //  CPU Process Priority & IFEO Management
    // ══════════════════════════════════════════════════════════

    public List<RunningProcessPriorityItem> GetRunningProcessesWithPriority()
    {
        var list = new List<RunningProcessPriorityItem>();
        try
        {
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    if (p.Id <= 4) continue;
                    var name = p.ProcessName;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var exeName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
                    long memMb = 0;
                    string prioStr = "Normal";
                    string exePath = "";

                    try { memMb = p.WorkingSet64 / (1024 * 1024); } catch { }
                    try { prioStr = p.PriorityClass.ToString(); } catch { }
                    try { exePath = p.MainModule?.FileName ?? ""; } catch { }

                    list.Add(new RunningProcessPriorityItem
                    {
                        Pid = p.Id,
                        ProcessName = exeName,
                        DisplayName = $"{exeName} (PID: {p.Id}, {memMb} MB, 優先權: {prioStr})",
                        PriorityClass = prioStr,
                        MemoryMb = memMb,
                        ExecutablePath = exePath
                    });
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch { }

        return list.OrderBy(p => p.ProcessName).ToList();
    }

    public (bool Success, string Message, int AffectedCount) SetImmediateProcessPriority(string processName, ProcessPriorityClass priority)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return (false, "處理程序名稱不可為空", 0);

        var cleanName = System.IO.Path.GetFileName(processName.Trim());
        if (cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            cleanName = cleanName.Substring(0, cleanName.Length - 4);

        int affected = 0;
        try
        {
            var procs = Process.GetProcessesByName(cleanName);
            if (procs.Length == 0)
            {
                return (false, $"未找到正在執行的 {cleanName}.exe 行程", 0);
            }

            foreach (var p in procs)
            {
                try
                {
                    p.PriorityClass = priority;
                    affected++;
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }

            if (affected > 0)
            {
                return (true, $"已將 {cleanName}.exe (共 {affected} 個行程) 之 CPU 優先權即時設定為 {priority}", affected);
            }
            else
            {
                return (false, $"設定 {cleanName}.exe 優先權失敗 (可能權限不足)", 0);
            }
        }
        catch (Exception ex)
        {
            return (false, $"調整處理程序優先權發生錯誤: {ex.Message}", 0);
        }
    }

    public bool SetIfeoCpuPriority(string processName, string priority)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        var cleanName = System.IO.Path.GetFileName(processName.Trim());
        if (!cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            cleanName += ".exe";

        int ifeoDword = priority switch
        {
            "RealTime" or "Realtime" or "即時" => 4,
            "High" or "高" => 3,
            "AboveNormal" or "高於標準" => 6,
            "Normal" or "標準" => 2,
            _ => 3
        };

        try
        {
            using var ifeoKey = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{cleanName}\PerfOptions", writable: true);
            if (ifeoKey != null)
            {
                ifeoKey.SetValue("CpuPriorityClass", ifeoDword, RegistryValueKind.DWord);
                return true;
            }
        }
        catch { }

        return false;
    }

    public bool DeleteIfeoCpuPriority(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        var cleanName = System.IO.Path.GetFileName(processName.Trim());
        if (!cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            cleanName += ".exe";

        try
        {
            using var appKey = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{cleanName}", writable: true);
            if (appKey != null)
            {
                try { appKey.DeleteSubKeyTree("PerfOptions", throwOnMissingSubKey: false); } catch { }
                if (appKey.SubKeyCount == 0 && appKey.ValueCount == 0)
                {
                    using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", writable: true);
                    root?.DeleteSubKey(cleanName, throwOnMissingSubKey: false);
                }
                return true;
            }
        }
        catch { }

        return false;
    }

    public List<PersistedPriorityRuleItem> ListPersistedIfeoRules()
    {
        var list = new List<PersistedPriorityRuleItem>();
        var foundNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Read from Registry IFEO
        try
        {
            using var ifeoRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options");
            if (ifeoRoot != null)
            {
                foreach (var subKeyName in ifeoRoot.GetSubKeyNames())
                {
                    try
                    {
                        using var perfKey = ifeoRoot.OpenSubKey($@"{subKeyName}\PerfOptions");
                        if (perfKey != null)
                        {
                            var val = perfKey.GetValue("CpuPriorityClass");
                            if (val is int intVal)
                            {
                                string prio = intVal switch
                                {
                                    4 => "RealTime",
                                    3 => "High",
                                    6 => "AboveNormal",
                                    2 => "Normal",
                                    1 => "Idle",
                                    5 => "BelowNormal",
                                    _ => $"Custom ({intVal})"
                                };
                                list.Add(new PersistedPriorityRuleItem
                                {
                                    ProcessName = subKeyName,
                                    Priority = prio,
                                    IfeoValue = intVal,
                                    Status = "已寫入 IFEO (開機自動生效)"
                                });
                                foundNames.Add(subKeyName);
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        // 2. Also check SettingsService to include any configured rules that were saved
        try
        {
            foreach (var kvp in SettingsService.Instance.Current.ProcessCpuPriorities)
            {
                var procName = kvp.Key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? kvp.Key : kvp.Key + ".exe";
                if (!foundNames.Contains(procName))
                {
                    list.Add(new PersistedPriorityRuleItem
                    {
                        ProcessName = procName,
                        Priority = kvp.Value,
                        IfeoValue = kvp.Value switch { "RealTime" => 4, "High" => 3, "AboveNormal" => 6, _ => 2 },
                        Status = "已儲存於使用者設定"
                    });
                }
            }
        }
        catch { }

        return list.OrderBy(r => r.ProcessName).ToList();
    }
}

