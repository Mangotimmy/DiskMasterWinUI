using System.Diagnostics;
using System.Management;
using System.Text;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using Microsoft.Win32;

namespace DiskMasterWinUI.Services;

public class KeyReaderService
{
    public async Task<WindowsLicenseInfo> GetLicenseInfoAsync()
    {
        return await Task.Run(async () =>
        {
            var info = new WindowsLicenseInfo();
            var sb = new StringBuilder();

            // 1. UEFI / BIOS OEM Key from MSDM via in-process WMI (ultra-fast < 20ms)
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT OA3xOriginalProductKey FROM SoftwareLicensingService");
                using var collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                {
                    var oemKey = obj["OA3xOriginalProductKey"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(oemKey) && oemKey.Contains('-'))
                    {
                        info.BiosOemKey = oemKey;
                        sb.AppendLine($"[BIOS OEM Key] {oemKey}");
                        break;
                    }
                }

                if (string.IsNullOrEmpty(info.BiosOemKey))
                {
                    info.BiosOemKey = "No OEM key embedded in BIOS/UEFI MSDM table";
                }
            }
            catch
            {
                // Fallback: MSDM ACPI raw table via PowerShell if in-process WMI service is unavailable
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var (oemKey, _, _) = await ProcessHelper.RunProcessAsync(
                        "powershell.exe",
                        "-NoProfile -Command \"(Get-CimInstance -ClassName SoftwareLicensingService).OA3xOriginalProductKey\"",
                        cancellationToken: cts.Token);
                    oemKey = oemKey.Trim();
                    if (!string.IsNullOrEmpty(oemKey) && oemKey.Contains('-'))
                    {
                        info.BiosOemKey = oemKey;
                        sb.AppendLine($"[BIOS OEM Key] {oemKey}");
                    }
                    else
                    {
                        info.BiosOemKey = "No OEM key embedded in BIOS/UEFI MSDM table";
                    }
                }
                catch (Exception ex)
                {
                    info.BiosOemKey = $"Error: {ex.Message}";
                }
            }

            // 2. Hardware serials & Board info via Registry + in-process WMI (ultra-fast < 20ms)
            try
            {
                // Pre-populate from BIOS registry (0ms non-elevated read)
                try
                {
                    using var biosRegKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                    if (biosRegKey != null)
                    {
                        var boardMfg = biosRegKey.GetValue("BaseBoardManufacturer")?.ToString()?.Trim();
                        var sysMfg = biosRegKey.GetValue("SystemManufacturer")?.ToString()?.Trim();
                        var boardProd = biosRegKey.GetValue("BaseBoardProduct")?.ToString()?.Trim();
                        var sysProd = biosRegKey.GetValue("SystemProductName")?.ToString()?.Trim();

                        if (IsValidHardwareString(boardMfg)) info.MotherboardManufacturer = boardMfg!;
                        else if (IsValidHardwareString(sysMfg)) info.MotherboardManufacturer = sysMfg!;

                        if (IsValidHardwareString(boardProd)) info.MotherboardProduct = boardProd!;
                        else if (IsValidHardwareString(sysProd)) info.MotherboardProduct = sysProd!;
                    }
                }
                catch { }

                using var biosSearcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT SerialNumber FROM Win32_BIOS");
                using var biosColl = biosSearcher.Get();
                foreach (ManagementObject obj in biosColl)
                {
                    var serial = obj["SerialNumber"]?.ToString()?.Trim() ?? "";
                    if (IsValidHardwareString(serial))
                    {
                        info.BiosSerialNumber = serial;
                        break;
                    }
                }

                using var boardSearcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Product, Manufacturer FROM Win32_BaseBoard");
                using var boardColl = boardSearcher.Get();
                foreach (ManagementObject obj in boardColl)
                {
                    var prod = obj["Product"]?.ToString()?.Trim();
                    var mfg = obj["Manufacturer"]?.ToString()?.Trim();
                    if (IsValidHardwareString(prod)) info.MotherboardProduct = prod!;
                    if (IsValidHardwareString(mfg)) info.MotherboardManufacturer = mfg!;
                    if (!string.IsNullOrEmpty(info.MotherboardProduct) && !string.IsNullOrEmpty(info.MotherboardManufacturer)) break;
                }

                // If manufacturer or product is still missing/generic, query Win32_ComputerSystem
                if (!IsValidHardwareString(info.MotherboardManufacturer) || !IsValidHardwareString(info.MotherboardProduct))
                {
                    try
                    {
                        using var sysSearcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                        using var sysColl = sysSearcher.Get();
                        foreach (ManagementObject obj in sysColl)
                        {
                            var sysMfg = obj["Manufacturer"]?.ToString()?.Trim();
                            var sysMod = obj["Model"]?.ToString()?.Trim();
                            if (!IsValidHardwareString(info.MotherboardManufacturer) && IsValidHardwareString(sysMfg))
                                info.MotherboardManufacturer = sysMfg!;
                            if (!IsValidHardwareString(info.MotherboardProduct) && IsValidHardwareString(sysMod))
                                info.MotherboardProduct = sysMod!;
                            break;
                        }
                    }
                    catch { }
                }

                sb.AppendLine($"[BIOS Serial] {info.BiosSerialNumber}");
                sb.AppendLine($"[Motherboard] {info.MotherboardManufacturer} {info.MotherboardProduct}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Hardware Query Error] {ex.Message}");
            }

            // 3. Local Installed Windows Key from Registry
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key != null)
                {
                    info.WindowsProductName = key.GetValue("ProductName")?.ToString() ?? "";
                    info.WindowsEditionId = key.GetValue("EditionID")?.ToString() ?? "";

                    var digitalProductId = key.GetValue("DigitalProductId") as byte[];
                    if (digitalProductId != null && digitalProductId.Length >= 67)
                    {
                        info.InstalledProductKey = DecodeProductKey(digitalProductId);
                        sb.AppendLine($"[Installed OS] {info.WindowsProductName} ({info.WindowsEditionId})");
                        sb.AppendLine($"[Installed Key] {info.InstalledProductKey}");
                    }
                    else
                    {
                        info.InstalledProductKey = "DigitalProductId not found (Digital License active)";
                    }
                }
            }
            catch (Exception ex)
            {
                info.InstalledProductKey = $"Registry Error: {ex.Message}";
            }

            info.RawSummary = sb.ToString();
            return info;
        });
    }

    public async Task<(string Key, string Edition, string Log)> ReadOfflineKeyAsync(string offlineWindowsPath)
    {
        var sb = new StringBuilder();
        var softwareHive = Path.Combine(offlineWindowsPath, "System32", "config", "SOFTWARE");
        if (!File.Exists(softwareHive))
        {
            return ("Error: Hive file not found", "", $"Could not locate {softwareHive}");
        }

        const string mountKeyName = "DM_OFFLINE_SW";
        try
        {
            // Mount hive: reg load HKLM\DM_OFFLINE_SW <path>
            await ProcessHelper.RunProcessAsync("reg.exe", $"load HKLM\\{mountKeyName} \"{softwareHive}\"");

            using var key = Registry.LocalMachine.OpenSubKey($@"{mountKeyName}\Microsoft\Windows NT\CurrentVersion");
            if (key == null)
            {
                return ("Error: CurrentVersion key missing", "", "Failed to open offline registry key.");
            }

            var edition = key.GetValue("ProductName")?.ToString() ?? key.GetValue("EditionID")?.ToString() ?? "";
            var digitalProductId = key.GetValue("DigitalProductId") as byte[];
            string productKey = "Digital License / Not Present";
            if (digitalProductId != null && digitalProductId.Length >= 67)
            {
                productKey = DecodeProductKey(digitalProductId);
            }

            sb.AppendLine($"[Offline OS] {edition}");
            sb.AppendLine($"[Offline Key] {productKey}");
            return (productKey, edition, sb.ToString());
        }
        catch (Exception ex)
        {
            return ($"Error: {ex.Message}", "", ex.ToString());
        }
        finally
        {
            // Unload hive safely
            try
            {
                await ProcessHelper.RunProcessAsync("reg.exe", $"unload HKLM\\{mountKeyName}");
            }
            catch { }
        }
    }

    public static string DecodeProductKey(byte[] digitalProductId)
    {
        if (digitalProductId == null || digitalProductId.Length < 67) return "Invalid DigitalProductId";

        try
        {
            const string digits = "BCDFGHJKMPQRTVWXY2346789";
            var rawKey = new byte[15];
            Array.Copy(digitalProductId, 52, rawKey, 0, 15);

            var isWin8OrLater = (byte)((digitalProductId[66] / 6) & 1);
            digitalProductId[66] = (byte)((digitalProductId[66] & 0xF7) | (isWin8OrLater & 2) * 4);

            var keyChars = new char[29];
            var last = 0;

            for (int i = 24; i >= 0; i--)
            {
                int current = 0;
                for (int j = 14; j >= 0; j--)
                {
                    current = (current * 256) ^ rawKey[j];
                    rawKey[j] = (byte)(current / 24);
                    current %= 24;
                    last = current;
                }
                keyChars[i] = digits[current];
            }

            var keyResult = new string(keyChars, 0, 25);
            if (isWin8OrLater != 0)
            {
                var insertChar = digits[last];
                keyResult = keyResult.Insert(last, insertChar.ToString());
                if (keyResult.Length > 25) keyResult = keyResult.Substring(1, 25);
            }

            // Insert hyphens: XXXXX-XXXXX-XXXXX-XXXXX-XXXXX
            var formatted = new StringBuilder();
            for (int i = 0; i < 25; i++)
            {
                formatted.Append(keyResult[i]);
                if ((i % 5) == 4 && i != 24) formatted.Append('-');
            }

            var result = formatted.ToString();
            // If the key is all B's (digital license placeholder), report as Digital License Active
            if (result.Replace("-", "").All(c => c == 'B') || result.StartsWith("BBBBB-BBBBB-BBBBB"))
            {
                return LocalizationService.Instance["DigitalLicenseActive"];
            }

            return result;
        }
        catch
        {
            return "Unable to decode key";
        }
    }

    private static bool IsValidHardwareString(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return false;
        var s = val.Trim();
        if (s.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Default string", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("System manufacturer", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("System Product Name", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }
}
