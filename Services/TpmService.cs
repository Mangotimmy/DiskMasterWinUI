using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DiskMasterWinUI.Models;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Service providing hardware TPM 2.0 diagnostics, security console invocation,
/// and Windows 11 installation/upgrade requirement bypass configuration (LabConfig).
/// </summary>
public class TpmService
{
    private const string LabConfigPath = @"SYSTEM\Setup\LabConfig";
    private const string MoSetupPath = @"SYSTEM\Setup\MoSetup";

    /// <summary>
    /// Queries the system TPM 2.0/1.2 status via Registry services, PowerShell Get-Tpm, and LabConfig.
    /// </summary>
    public async Task<TpmStatusInfo> GetTpmStatusAsync()
    {
        return await Task.Run(() =>
        {
            var info = new TpmStatusInfo();
            var sb = new StringBuilder();

            // 1. Check TPM service in registry (fast, non-blocking)
            try
            {
                using var tpmKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TPM");
                if (tpmKey != null)
                {
                    sb.AppendLine("[TPM Registry Service] tpm.sys driver service is registered.");
                }
            }
            catch { }

            // 2. Query PowerShell Get-Tpm / Win32_Tpm
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"try { $t = Get-Tpm -ErrorAction SilentlyContinue; if ($t) { [PSCustomObject]@{ Present = $t.TpmPresent; Ready = $t.TpmReady; Version = $t.ManufacturerVersion; Id = $t.ManufacturerId } | ConvertTo-Json -Compress } else { [PSCustomObject]@{ Present = $false } | ConvertTo-Json -Compress } } catch { [PSCustomObject]@{ Present = $false } | ConvertTo-Json -Compress }\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(3500);

                    if (!string.IsNullOrWhiteSpace(output) && output.StartsWith("{"))
                    {
                        using var doc = JsonDocument.Parse(output);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("Present", out var pElem) && pElem.GetBoolean())
                        {
                            info.IsPresent = true;
                            info.IsEnabled = true;
                            info.IsActivated = true;
                            info.SpecVersion = "2.0";

                            if (root.TryGetProperty("Ready", out var rElem))
                            {
                                info.IsOwned = rElem.GetBoolean();
                            }
                            if (root.TryGetProperty("Version", out var vElem))
                            {
                                info.ManufacturerVersion = vElem.GetString() ?? "";
                            }
                            if (root.TryGetProperty("Id", out var idElem))
                            {
                                info.ManufacturerName = idElem.GetString() ?? "Discrete / Firmware TPM";
                            }
                            sb.AppendLine($"[PowerShell Get-Tpm] Present: {info.IsPresent}, Version: {info.ManufacturerVersion}, ID: {info.ManufacturerName}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[TPM Query Notice] {ex.Message}");
            }

            // Summary formatting
            if (info.IsPresent)
            {
                info.StatusSummary = $"TPM {info.SpecVersion} 正常運行中 (已啟用 / 已就緒)";
            }
            else
            {
                info.StatusSummary = "未檢測到硬體 TPM 晶片 (可透過下方一鍵繞過 Win11 限制)";
            }

            // 3. Query Windows 11 LabConfig Bypass Status
            info.IsWin11BypassActive = IsWin11RequirementBypassActive();
            sb.AppendLine($"[LabConfig Bypass] Active: {info.IsWin11BypassActive}");

            info.DetailsLog = sb.ToString();
            return info;
        });
    }

    /// <summary>
    /// Checks whether the Windows 11 LabConfig bypass values are present in HKLM.
    /// </summary>
    public bool IsWin11RequirementBypassActive()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(LabConfigPath);
            if (key != null)
            {
                var tpmBypass = key.GetValue("BypassTPMCheck");
                if (tpmBypass is int val && val == 1) return true;
                if (tpmBypass != null && tpmBypass.ToString() == "1") return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Configures or restores the Windows 11 hardware check bypass flags in LabConfig and MoSetup.
    /// </summary>
    public async Task<(bool Success, string Message)> SetWin11RequirementBypassAsync(bool enable)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (enable)
                {
                    using (var labKey = Registry.LocalMachine.CreateSubKey(LabConfigPath, true))
                    {
                        labKey.SetValue("BypassTPMCheck", 1, RegistryValueKind.DWord);
                        labKey.SetValue("BypassSecureBootCheck", 1, RegistryValueKind.DWord);
                        labKey.SetValue("BypassRAMCheck", 1, RegistryValueKind.DWord);
                        labKey.SetValue("BypassCPUCheck", 1, RegistryValueKind.DWord);
                        labKey.SetValue("BypassStorageCheck", 1, RegistryValueKind.DWord);
                    }

                    using (var moKey = Registry.LocalMachine.CreateSubKey(MoSetupPath, true))
                    {
                        moKey.SetValue("AllowUpgradesWithUnsupportedTPMOrCPU", 1, RegistryValueKind.DWord);
                    }

                    return (true, "已成功啟用 Windows 11 安裝與升級限制繞過 (LabConfig: TPM/SecureBoot/CPU/RAM)！");
                }
                else
                {
                    using (var labKey = Registry.LocalMachine.OpenSubKey(LabConfigPath, true))
                    {
                        if (labKey != null)
                        {
                            labKey.DeleteValue("BypassTPMCheck", false);
                            labKey.DeleteValue("BypassSecureBootCheck", false);
                            labKey.DeleteValue("BypassRAMCheck", false);
                            labKey.DeleteValue("BypassCPUCheck", false);
                            labKey.DeleteValue("BypassStorageCheck", false);
                        }
                    }

                    using (var moKey = Registry.LocalMachine.OpenSubKey(MoSetupPath, true))
                    {
                        if (moKey != null)
                        {
                            moKey.DeleteValue("AllowUpgradesWithUnsupportedTPMOrCPU", false);
                        }
                    }

                    return (true, "已成功還原預設檢查，移除 LabConfig 繞過設定。");
                }
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "需要系統管理員權限才能修改 HKLM 登錄檔。");
            }
            catch (Exception ex)
            {
                return (false, $"設定失敗: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Alias for SetWin11RequirementBypassAsync.
    /// </summary>
    public Task<(bool Success, string Message)> SetWin11SetupBypassAsync(bool enable) => SetWin11RequirementBypassAsync(enable);

    /// <summary>
    /// Opens the native Windows TPM management console (tpm.msc).
    /// </summary>
    public void OpenTpmManagementConsole()
    {
        try
        {
            Process.Start(new ProcessStartInfo("tpm.msc") { UseShellExecute = true });
        }
        catch { }
    }
}
