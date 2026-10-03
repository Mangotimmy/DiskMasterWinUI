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

            // 1. Check TPM service and WMI status in registry (fast, non-blocking, works even without admin)
            try
            {
                using var tpmKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TPM");
                if (tpmKey != null)
                {
                    sb.AppendLine("[TPM Registry Service] tpm.sys driver service is registered.");
                }

                using var wmiKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TPM\WMI");
                if (wmiKey != null)
                {
                    var taskMfgId = wmiKey.GetValue("TaskManufacturerId");
                    var taskFwVer = wmiKey.GetValue("TaskFirmwareVersion")?.ToString();
                    var provFw = wmiKey.GetValue("FirmwareVersionAtLastProvision")?.ToString();

                    if (taskMfgId != null)
                    {
                        info.IsPresent = true;
                        info.IsEnabled = true;
                        info.IsActivated = true;
                        info.SpecVersion = "2.0";
                        if (!string.IsNullOrWhiteSpace(taskFwVer))
                        {
                            info.ManufacturerVersion = taskFwVer;
                        }
                        else if (!string.IsNullOrWhiteSpace(provFw))
                        {
                            info.ManufacturerVersion = provFw;
                        }

                        info.ManufacturerName = DecodeTpmManufacturer(taskMfgId);
                        sb.AppendLine($"[TPM WMI Registry] Manufacturer: {info.ManufacturerName}, FW: {info.ManufacturerVersion}");
                    }
                }
            }
            catch { }

            // 2. Query PowerShell Get-Tpm for live runtime status
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"try { $t = Get-Tpm -ErrorAction SilentlyContinue; if ($t -and ($t.TpmPresent -eq $true)) { [PSCustomObject]@{ Present = $true; Ready = [bool]$t.TpmReady; Version = [string]$t.ManufacturerVersion; Id = $t.ManufacturerId; IdTxt = [string]$t.ManufacturerIdTxt } | ConvertTo-Json -Compress } else { [PSCustomObject]@{ Present = $false } | ConvertTo-Json -Compress } } catch { [PSCustomObject]@{ Present = $false } | ConvertTo-Json -Compress }\"",
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
                        if (root.TryGetProperty("Present", out var pElem) && pElem.ValueKind == JsonValueKind.True)
                        {
                            info.IsPresent = true;
                            info.IsEnabled = true;
                            info.IsActivated = true;
                            info.SpecVersion = "2.0";

                            if (root.TryGetProperty("Ready", out var rElem) && (rElem.ValueKind == JsonValueKind.True || rElem.ValueKind == JsonValueKind.False))
                            {
                                info.IsOwned = rElem.GetBoolean();
                            }
                            if (root.TryGetProperty("Version", out var vElem) && vElem.ValueKind == JsonValueKind.String)
                            {
                                var ver = vElem.GetString();
                                if (!string.IsNullOrWhiteSpace(ver)) info.ManufacturerVersion = ver;
                            }

                            string? idTxt = null;
                            if (root.TryGetProperty("IdTxt", out var txtElem) && txtElem.ValueKind == JsonValueKind.String)
                            {
                                idTxt = txtElem.GetString();
                            }

                            object? rawId = null;
                            if (root.TryGetProperty("Id", out var idElem))
                            {
                                if (idElem.ValueKind == JsonValueKind.Number && idElem.TryGetInt64(out var numId))
                                {
                                    rawId = numId;
                                }
                                else if (idElem.ValueKind == JsonValueKind.String)
                                {
                                    rawId = idElem.GetString();
                                }
                            }

                            var decodedName = DecodeTpmManufacturer(rawId, idTxt);
                            if (!string.IsNullOrWhiteSpace(decodedName) && decodedName != "Unknown")
                            {
                                info.ManufacturerName = decodedName;
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

    /// <summary>
    /// Decodes a raw TPM Manufacturer ID (Big-Endian/Little-Endian ASCII packed integer, hex or string) into a user-friendly vendor brand.
    /// </summary>
    public static string DecodeTpmManufacturer(object? rawId, string? idTxt = null)
    {
        string tag = string.Empty;

        if (!string.IsNullOrWhiteSpace(idTxt))
        {
            tag = idTxt.Trim('\0', ' ');
        }
        else if (rawId != null)
        {
            if (rawId is int intVal)
            {
                tag = UnpackAscii((uint)intVal);
            }
            else if (rawId is long longVal)
            {
                tag = UnpackAscii((uint)longVal);
            }
            else if (rawId is uint uVal)
            {
                tag = UnpackAscii(uVal);
            }
            else
            {
                var rawStr = rawId.ToString()?.Trim() ?? string.Empty;
                if (rawStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                    uint.TryParse(rawStr.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
                {
                    tag = UnpackAscii(hexVal);
                }
                else if (uint.TryParse(rawStr, out var parsedVal))
                {
                    tag = UnpackAscii(parsedVal);
                }
                else
                {
                    tag = rawStr.Trim('\0', ' ');
                }
            }
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            return "Unknown";
        }

        var upper = tag.ToUpperInvariant();
        var mapped = MapVendorTag(upper);
        if (mapped != null) return mapped;

        // Try little-endian reverse if 3-4 chars
        if (upper.Length is 3 or 4)
        {
            var reversed = new string(upper.Reverse().ToArray());
            var revMapped = MapVendorTag(reversed);
            if (revMapped != null) return revMapped;
        }

        // If tag is a plausible vendor acronym/name, return it rather than "Unknown"
        if (tag.Length <= 10 && tag.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' '))
        {
            return tag;
        }

        return "Unknown";
    }

    private static string? MapVendorTag(string tag) => tag switch
    {
        "IFX" => "Infineon Technologies (英飛凌)",
        "INTC" => "Intel PTT (英特爾)",
        "AMD" => "AMD fTPM (超微)",
        "MSFT" => "Microsoft (微軟)",
        "NTC" => "Nuvoton Technology (新唐科技)",
        "STM" => "STMicroelectronics (意法半導體)",
        "QCOM" => "Qualcomm (高通)",
        "ATML" => "Atmel (愛特梅爾)",
        "BRCM" => "Broadcom (博通)",
        "SMSC" => "SMSC (微芯科技)",
        "TI" => "Texas Instruments (德州儀器)",
        "WEC" => "Winbond (華邦電子)",
        "NSM" => "National Semiconductor (國家半導體)",
        "ETHZ" => "ETH Zürich",
        "FSL" => "Freescale (飛思卡爾)",
        "GOOG" => "Google (谷歌 Titan)",
        "HPE" => "Hewlett Packard Enterprise (慧與)",
        "IBM" => "IBM",
        "CSCO" => "Cisco (思科)",
        "LEN" => "Lenovo (聯想)",
        "DELL" => "Dell (戴爾)",
        "HP" => "HP (惠普)",
        "AAPL" or "APPLE" => "Apple (蘋果 Secure Enclave)",
        _ => null
    };

    private static string UnpackAscii(uint val)
    {
        char c1 = (char)((val >> 24) & 0xFF);
        char c2 = (char)((val >> 16) & 0xFF);
        char c3 = (char)((val >> 8) & 0xFF);
        char c4 = (char)(val & 0xFF);
        return $"{c1}{c2}{c3}{c4}".Trim('\0', ' ');
    }
}

