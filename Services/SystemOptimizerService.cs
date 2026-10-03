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
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", writable: true))
            {
                if (key != null)
                {
                    key.SetValue("AllowGameDVR", disabled ? 0 : 1, RegistryValueKind.DWord);
                }
            }
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
    //  4. 1-Click Gaming Mode / Restore Factory Defaults
    // ══════════════════════════════════════════════════════════

    public async Task<string> ApplyGamingProfileAsync(int win32PriorityVal = 38)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🎮 正在建立登錄檔自動備份快照...");
        await CreatePreTweakBackupAsync("Before_GamingProfile");

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] ⚡ 設定 Win32PrioritySeparation = 0x{win32PriorityVal:X} ({win32PriorityVal})...");
        SetWin32PrioritySeparation(win32PriorityVal);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] ⚡ 停用 CPU 能源節流 (PowerThrottlingOff = 1)...");
        SetPowerThrottlingDisabled(true);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🚀 設定 MMCSS SystemResponsiveness = 0 (100% 前台優先)...");
        SetSystemResponsiveness(0);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🌐 停用網路節流佇列 (NetworkThrottlingIndex = 0xFFFFFFFF)...");
        SetNetworkThrottlingDisabled(true);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🎮 提升 MMCSS Games 工作排程為 High Priority...");
        ConfigureMmcssGamesTask(true);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🛡️ 關閉 Game DVR / Game Bar 背景開銷與啟用全螢幕優化...");
        SetGameDvrDisabled(true);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🖥️ 啟用硬體加速 GPU 排程 (HAGS HwSchMode = 2)...");
        SetHagsStatus(true);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🖱️ 最佳化滑鼠 1:1 感應器追蹤與按鍵零延遲...");
        ConfigureInputLatency(true);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] ✅ 電競遊戲極致模式套用完成！建議重新開機以完全載入核心排程設定。");
        return sb.ToString();
    }

    public async Task<string> RestoreWindowsDefaultsAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🛡️ 正在建立登錄檔自動備份快照...");
        await CreatePreTweakBackupAsync("Before_RestoreDefaults");

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🔄 還原 Win32PrioritySeparation = 2 (Windows 預設)...");
        SetWin32PrioritySeparation(2);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🔄 還原 CPU 能源節流 (預設啟用)...");
        SetPowerThrottlingDisabled(false);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🔄 還原 MMCSS SystemResponsiveness = 20 (保留 20% 背景)...");
        SetSystemResponsiveness(20);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🔄 還原 MMCSS NetworkThrottlingIndex = 10...");
        SetNetworkThrottlingDisabled(false);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🔄 還原 MMCSS Games 工作排程為預設值...");
        ConfigureMmcssGamesTask(false);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🔄 還原 Game DVR 與 Game Bar 為預設啟用...");
        SetGameDvrDisabled(false);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🔄 還原硬體加速 GPU 排程 (HwSchMode = 1)...");
        SetHagsStatus(false);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] 🔄 還原滑鼠/鍵盤/桌面選單為 Windows 原生設定...");
        ConfigureInputLatency(false);

        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] ✅ 系統設定已全數安全還原為 Windows 官方出廠預設值！");
        return sb.ToString();
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
}
