using DiskMasterWinUI.Helpers;

namespace DiskMasterWinUI.Helpers;

public enum BootCompatibilityStatus
{
    Compatible,
    WarningNeedsBiosConfig,
    IncompatibleBlocked
}

public class BootCompatibilityResult
{
    public BootCompatibilityStatus Status { get; set; } = BootCompatibilityStatus.Compatible;
    public bool IsBootable => Status != BootCompatibilityStatus.IncompatibleBlocked;
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string SuggestedBcdFirmware { get; set; } = "UEFI"; // "UEFI" or "BIOS"
    public bool NeedsCsmGuide { get; set; }
    public bool NeedsMbrConversion { get; set; }
    public bool NeedsSecureBootDisabled { get; set; }
    public string ActionButtonText { get; set; } = "";
}

public static class BootGuardHelper
{
    /// <summary>
    /// Evaluates if the combination of target partition style (MBR vs GPT) and Windows version is bootable.
    /// </summary>
    public static BootCompatibilityResult CheckCompatibility(bool isGpt, string osNameOrVersion, string architecture)
    {
        var is64Bit = architecture.Contains("64", StringComparison.OrdinalIgnoreCase);
        var isWin7 = osNameOrVersion.Contains("Windows 7", StringComparison.OrdinalIgnoreCase) ||
                     osNameOrVersion.Contains("Win7", StringComparison.OrdinalIgnoreCase) ||
                     osNameOrVersion.Contains("6.1", StringComparison.OrdinalIgnoreCase);
        var isWin8 = osNameOrVersion.Contains("Windows 8", StringComparison.OrdinalIgnoreCase) ||
                     osNameOrVersion.Contains("Win8", StringComparison.OrdinalIgnoreCase) ||
                     osNameOrVersion.Contains("6.2", StringComparison.OrdinalIgnoreCase) ||
                     osNameOrVersion.Contains("6.3", StringComparison.OrdinalIgnoreCase);
        var isWinXP = osNameOrVersion.Contains("Windows XP", StringComparison.OrdinalIgnoreCase) ||
                      osNameOrVersion.Contains("WinXP", StringComparison.OrdinalIgnoreCase) ||
                      osNameOrVersion.Contains("5.1", StringComparison.OrdinalIgnoreCase);

        // 1. Windows XP
        if (isWinXP)
        {
            if (isGpt)
            {
                return new BootCompatibilityResult
                {
                    Status = BootCompatibilityStatus.IncompatibleBlocked,
                    Title = "❌ Windows XP 不支援 GPT 分割區開機",
                    Message = "Windows XP 僅能在 MBR 分割區 + Legacy BIOS 模式下開機。請將目標磁碟轉換為 MBR。",
                    SuggestedBcdFirmware = "BIOS",
                    NeedsMbrConversion = true,
                    ActionButtonText = "一鍵轉換磁碟為 MBR"
                };
            }
            return new BootCompatibilityResult
            {
                Status = BootCompatibilityStatus.Compatible,
                Title = "✅ Windows XP (MBR + Legacy BIOS 相容)",
                Message = "磁碟為 MBR 分割區，支援 Legacy BIOS 開機。主機板 SATA 需設為 IDE/AHCI 相容模式。",
                SuggestedBcdFirmware = "BIOS"
            };
        }

        // 2. Windows 7
        if (isWin7)
        {
            if (!is64Bit && isGpt)
            {
                // Win7 32-bit CANNOT boot on GPT
                return new BootCompatibilityResult
                {
                    Status = BootCompatibilityStatus.IncompatibleBlocked,
                    Title = "❌ Windows 7 32位元無法在 GPT/UEFI 下開機",
                    Message = "Windows 7 32 位元 (x86) 微軟官方架構完全不支援 GPT 分割區與 UEFI 開機！請轉換磁碟為 MBR 或改用 64 位元映像。",
                    SuggestedBcdFirmware = "BIOS",
                    NeedsMbrConversion = true,
                    ActionButtonText = "一鍵轉換磁碟為 MBR"
                };
            }

            if (is64Bit && isGpt)
            {
                // Win7 64-bit on GPT requires CSM and Secure Boot OFF
                return new BootCompatibilityResult
                {
                    Status = BootCompatibilityStatus.WarningNeedsBiosConfig,
                    Title = "⚠️ Windows 7 64位元 (GPT/UEFI 需調整主機板 BIOS)",
                    Message = "Windows 7 64 位元可在 GPT 下以 UEFI 開機，但主機板 BIOS 必須【開啟 CSM (相容性模組)】並【關閉 Secure Boot (安全開機)】！純 UEFI Class 3 (無傳統 VGA INT 10h) 主機板將無法開機。",
                    SuggestedBcdFirmware = "UEFI",
                    NeedsCsmGuide = true,
                    NeedsSecureBootDisabled = true,
                    ActionButtonText = "檢視 BIOS 設定教學"
                };
            }

            // Win7 on MBR
            return new BootCompatibilityResult
            {
                Status = BootCompatibilityStatus.Compatible,
                Title = "✅ Windows 7 (MBR + Legacy BIOS 完美相容)",
                Message = "磁碟為 MBR 格式，Windows 7 原生完美支援。將自動寫入 Legacy BIOS 開機代碼並設為使用中 (Active)。",
                SuggestedBcdFirmware = "BIOS"
            };
        }

        // 3. Windows 8 / 8.1
        if (isWin8)
        {
            if (!is64Bit && isGpt)
            {
                return new BootCompatibilityResult
                {
                    Status = BootCompatibilityStatus.WarningNeedsBiosConfig,
                    Title = "⚠️ Windows 8 32位元 GPT 限制",
                    Message = "Windows 8 32 位元僅支援少數 32 位元 UEFI 晶片（如舊款 Atom 平板）。一般 64 位元電腦建議使用 MBR 或改用 64 位元版本。",
                    SuggestedBcdFirmware = "BIOS",
                    NeedsMbrConversion = true
                };
            }

            if (is64Bit && isGpt)
            {
                return new BootCompatibilityResult
                {
                    Status = BootCompatibilityStatus.Compatible,
                    Title = "✅ Windows 8/8.1 64位元 (GPT + UEFI 原生完整支援)",
                    Message = "Windows 8.1 原生完整支援 UEFI Class 2/3 及 Secure Boot (安全開機)，開機極速。",
                    SuggestedBcdFirmware = "UEFI"
                };
            }

            return new BootCompatibilityResult
            {
                Status = BootCompatibilityStatus.Compatible,
                Title = "✅ Windows 8/8.1 (MBR + Legacy BIOS 相容)",
                Message = "磁碟為 MBR 格式，支援 Legacy BIOS 模式開機。",
                SuggestedBcdFirmware = "BIOS"
            };
        }

        // 4. Windows 10 & 11 (Modern)
        if (isGpt)
        {
            return new BootCompatibilityResult
            {
                Status = BootCompatibilityStatus.Compatible,
                Title = "✅ 現代 Windows (GPT + UEFI 官方標準)",
                Message = "目標為 GPT 分割區，原生支援 UEFI、安全開機與快速啟動。",
                SuggestedBcdFirmware = "UEFI"
            };
        }
        else
        {
            return new BootCompatibilityResult
            {
                Status = BootCompatibilityStatus.Compatible,
                Title = "✅ Windows (MBR + Legacy BIOS 相容)",
                Message = "目標為 MBR 分割區。若欲安裝 Windows 11，建議轉換為 GPT 分割區以滿足 UEFI 官方需求。",
                SuggestedBcdFirmware = "BIOS"
            };
        }
    }

    /// <summary>
    /// Ensures that Windows 7 64-bit UEFI EFI\Boot\bootx64.efi exists on the ESP.
    /// Many Win7 installation media lack this file, causing UEFI firmware to fail.
    /// </summary>
    public static void EnsureWin7UefiBootFiles(string espDriveLetter)
    {
        try
        {
            var cleanEsp = espDriveLetter.TrimEnd('\\', ':') + ":";
            var efiBootDir = Path.Combine(cleanEsp, "EFI", "Boot");
            var efiMicrosoftBootDir = Path.Combine(cleanEsp, "EFI", "Microsoft", "Boot");

            var bootx64 = Path.Combine(efiBootDir, "bootx64.efi");
            var bootmgfw = Path.Combine(efiMicrosoftBootDir, "bootmgfw.efi");

            if (!Directory.Exists(efiBootDir)) Directory.CreateDirectory(efiBootDir);

            if (!File.Exists(bootx64) && File.Exists(bootmgfw))
            {
                File.Copy(bootmgfw, bootx64, overwrite: true);
            }
        }
        catch { }
    }
}
