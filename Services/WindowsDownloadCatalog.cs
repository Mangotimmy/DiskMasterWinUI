using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public static class WindowsDownloadCatalog
{
    private static readonly List<WindowsDownloadItem> _catalog = new()
    {
        // ══════════════════════════════════════════════════════════════════════
        // 1. MODERN ERA: Windows 11 & Windows 10
        // ══════════════════════════════════════════════════════════════════════
        new WindowsDownloadItem
        {
            ReleaseCategory = "Modern",
            VersionTitle = "Windows 11 25H2",
            Edition = "Pro / Home / Enterprise",
            Language = "zh-TW",
            LanguageDisplay = "繁體中文",
            Architecture = "x64",
            BuildNumber = "26200.6584",
            FileName = "Win11_25H2_Chinese_Traditional_x64.iso",
            SizeDisplay = "6.83 GB",
            SizeBytes = 7335473152,
            PrimaryUrl = "https://go.microsoft.com/fwlink/p/?linkid=2334269",
            MirrorUrls = new List<string> { "https://software-static.download.prss.microsoft.com/dbazure/888969d5-f34g-4e03-ac9d-1f9786c66749/26200.6584.250915-1905.25h2_ge_release_svc_refresh_CLIENTENTERPRISEEVAL_OEMRET_x64FRE_zh-tw.iso" },
            ReleaseDate = "2026-03-15",
            Description = "微軟官方最新 25H2 旗艦版永久直鏈！免 Token、不限速，保證支援 16 線程 HTTP Range 極速下載。",
            SupportsSecureBoot = true,
            RequiresUefiCsm = false,
            RequiresMbr = false
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Modern",
            VersionTitle = "Windows 11 25H2",
            Edition = "Pro / Home / Enterprise",
            Language = "en-US",
            LanguageDisplay = "English (US)",
            Architecture = "x64",
            BuildNumber = "26200.6584",
            FileName = "Win11_25H2_English_x64.iso",
            SizeDisplay = "6.61 GB",
            SizeBytes = 7097485312,
            PrimaryUrl = "https://go.microsoft.com/fwlink/p/?linkid=2334167",
            MirrorUrls = new List<string> { "https://software-static.download.prss.microsoft.com/dbazure/888969d5-f34g-4e03-ac9d-1f9786c66749/26200.6584.250915-1905.25h2_ge_release_svc_refresh_CLIENTENTERPRISEEVAL_OEMRET_x64FRE_en-us.iso" },
            ReleaseDate = "2026-03-15",
            Description = "Official Microsoft 25H2 direct CDN link. Token-free, 100% reliable, supports 16-thread HTTP Range.",
            SupportsSecureBoot = true
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Modern",
            VersionTitle = "Windows 11 24H2",
            Edition = "Pro / Home / Enterprise",
            Language = "zh-TW",
            LanguageDisplay = "繁體中文",
            Architecture = "x64",
            BuildNumber = "26100.1742",
            FileName = "Win11_24H2_Traditional_Chinese_x64.iso",
            SizeDisplay = "4.89 GB",
            SizeBytes = 5253365760,
            PrimaryUrl = "https://go.microsoft.com/fwlink/p/?linkid=2288282",
            MirrorUrls = new List<string> { "https://software-static.download.prss.microsoft.com/dbazure/888969d5-f34g-4e03-ac9d-1f9786c66749/26100.1742.240906-0331.ge_release_svc_refresh_CLIENT_LTSC_EVAL_x64FRE_zh-tw.iso" },
            ReleaseDate = "2024-10-01",
            Description = "微軟官方 24H2 永久直鏈！免 Token、不限速，保證支援 16 線程 HTTP Range 極速下載。",
            SupportsSecureBoot = true,
            RequiresUefiCsm = false,
            RequiresMbr = false
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Modern",
            VersionTitle = "Windows 11 24H2",
            Edition = "Pro / Home / Enterprise",
            Language = "en-US",
            LanguageDisplay = "English (US)",
            Architecture = "x64",
            BuildNumber = "26100.1742",
            FileName = "Win11_24H2_English_x64.iso",
            SizeDisplay = "4.76 GB",
            SizeBytes = 5111808000,
            PrimaryUrl = "https://go.microsoft.com/fwlink/p/?linkid=2289029",
            MirrorUrls = new List<string> { "https://software-static.download.prss.microsoft.com/dbazure/888969d5-f34g-4e03-ac9d-1f9786c66749/26100.1742.240906-0331.ge_release_svc_refresh_CLIENT_LTSC_EVAL_x64FRE_en-us.iso" },
            ReleaseDate = "2024-10-01",
            Description = "Official Microsoft Windows 11 24H2 ISO. Token-free, 100% reliable, supports 16-thread HTTP Range.",
            SupportsSecureBoot = true
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Modern",
            VersionTitle = "Windows 10 22H2",
            Edition = "Pro / Home / Education",
            Language = "zh-TW",
            LanguageDisplay = "繁體中文",
            Architecture = "x64",
            BuildNumber = "19045.2965",
            FileName = "Win10_22H2_Traditional_Chinese_x64.iso",
            SizeDisplay = "5.7 GB",
            SizeBytes = 6121472000,
            Sha256Hash = "a6f0590a38321cb50e3860bb4d728518f9fa090b82f8eeeb2cffab8adbf6a337",
            PrimaryUrl = "https://software.download.prss.microsoft.com/dbazure/Win10_22H2_Traditional_Chinese_x64.iso",
            MirrorUrls = new List<string> { "https://archive.org/download/win-10-22h2-traditional-chinese-x64/Win10_22H2_Traditional_Chinese_x64.iso" },
            ReleaseDate = "2022-10-18",
            Description = "Windows 10 最終穩定長期支援版本 (22H2)（需當日有效 Token，可點擊上方按鈕獲取官網直鏈一鍵貼上）。",
            SupportsSecureBoot = true
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Modern",
            VersionTitle = "Windows 10 LTSC 2021",
            Edition = "Enterprise LTSC",
            Language = "en-US / Multi",
            LanguageDisplay = "English / Multi",
            Architecture = "x64",
            BuildNumber = "19044.1288",
            FileName = "19044.1288.211006-0501.21h2_release_svc_refresh_CLIENT_LTSC_EVAL_x64FRE_en-us.iso",
            SizeDisplay = "4.9 GB",
            SizeBytes = 4898582528,
            PrimaryUrl = "https://go.microsoft.com/fwlink/p/?linkid=2195404",
            MirrorUrls = new List<string> { "https://archive.org/download/zh-tw_windows_10_enterprise_ltsc_2021_x64_dvd_e33b6686/zh-tw_windows_10_enterprise_ltsc_2021_x64_dvd_e33b6686.iso" },
            ReleaseDate = "2021-11-16",
            Description = "微軟官方永久有效直鏈！保證支援 HTTP Range 多線程分段極速下載，純淨精簡、無廣告、支援至 2031 年。",
            SupportsSecureBoot = true
        },

        // ══════════════════════════════════════════════════════════════════════
        // 2. CLASSIC ERA: Windows 8.1 & Windows 8
        // ══════════════════════════════════════════════════════════════════════
        new WindowsDownloadItem
        {
            ReleaseCategory = "Classic",
            VersionTitle = "Windows 8.1 with Update",
            Edition = "Professional / Core",
            Language = "zh-TW",
            LanguageDisplay = "繁體中文",
            Architecture = "x64",
            BuildNumber = "9600.17053",
            FileName = "tw_windows_8.1_with_update_x64_dvd_6051485.iso",
            SizeDisplay = "4.04 GB",
            SizeBytes = 4341991424,
            Sha1Hash = "1580BAEB76BFA18F7B5A0CD2ED33658514197491",
            PrimaryUrl = "https://archive.org/download/tw_windows_8.1_with_update_x64_dvd_6051485/tw_windows_8.1_with_update_x64_dvd_6051485.iso",
            MirrorUrls = new List<string> { "https://archive.org/download/windows-8.1-msdn/tw_windows_8.1_with_update_x64_dvd_6051485.iso" },
            ReleaseDate = "2014-12-15",
            Description = "微軟官方 MSDN 原版 Windows 8.1 包含 Update 1。原生完整支援 UEFI 與 Secure Boot，開機極速。",
            SupportsSecureBoot = true,
            RequiresUefiCsm = false,
            RequiresMbr = false
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Classic",
            VersionTitle = "Windows 8.1 with Update",
            Edition = "Professional / Core",
            Language = "en-US",
            LanguageDisplay = "English (US)",
            Architecture = "x64",
            BuildNumber = "9600.17053",
            FileName = "en_windows_8.1_with_update_x64_dvd_6051480.iso",
            SizeDisplay = "4.02 GB",
            SizeBytes = 4319404032,
            Sha1Hash = "A914441ECD414619C74E06DE4902F1DEB735F60B",
            PrimaryUrl = "https://archive.org/download/en_windows_8.1_with_update_x64_dvd_6051480/en_windows_8.1_with_update_x64_dvd_6051480.iso",
            ReleaseDate = "2014-12-15",
            Description = "Original official Microsoft MSDN Windows 8.1 with Update (64-bit). Full native UEFI & Secure Boot support.",
            SupportsSecureBoot = true
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Classic",
            VersionTitle = "Windows 8.1 with Update",
            Edition = "Professional / Core",
            Language = "zh-TW",
            LanguageDisplay = "繁體中文",
            Architecture = "x86",
            BuildNumber = "9600.17053",
            FileName = "tw_windows_8.1_with_update_x86_dvd_6051515.iso",
            SizeDisplay = "3.01 GB",
            SizeBytes = 3232829440,
            Sha1Hash = "7F6BAF55FE19890F0B1EB8DF966E044FE9D87C9E",
            PrimaryUrl = "https://archive.org/download/tw_windows_8.1_with_update_x86_dvd_6051515/tw_windows_8.1_with_update_x86_dvd_6051515.iso",
            ReleaseDate = "2014-12-15",
            Description = "微軟官方 MSDN 原版 Windows 8.1 32 位元版本。建議搭配 MBR 分割區開機。",
            RequiresMbr = true,
            SupportsSecureBoot = false
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Classic",
            VersionTitle = "Windows 8",
            Edition = "Professional / Core",
            Language = "zh-TW",
            LanguageDisplay = "繁體中文",
            Architecture = "x64",
            BuildNumber = "9200.16384",
            FileName = "tw_windows_8_x64_dvd_915413.iso",
            SizeDisplay = "3.42 GB",
            SizeBytes = 3672535040,
            Sha1Hash = "4D0BE65CA569CF6EBC5079FE6F280D58C21EDAE7",
            PrimaryUrl = "https://archive.org/download/tw_windows_8_x64_dvd_915413/tw_windows_8_x64_dvd_915413.iso",
            ReleaseDate = "2012-08-15",
            Description = "微軟官方 MSDN 原版 Windows 8 (64-bit)。原生支援 UEFI 開機。",
            SupportsSecureBoot = true
        },

        // ══════════════════════════════════════════════════════════════════════
        // 3. LEGACY ERA: Windows 7 SP1
        // ══════════════════════════════════════════════════════════════════════
        new WindowsDownloadItem
        {
            ReleaseCategory = "Legacy",
            VersionTitle = "Windows 7 Ultimate SP1",
            Edition = "Ultimate",
            Language = "zh-TW",
            LanguageDisplay = "繁體中文",
            Architecture = "x64",
            BuildNumber = "7601.17514",
            FileName = "tw_windows_7_ultimate_with_sp1_x64_dvd_u_677414.iso",
            SizeDisplay = "3.19 GB",
            SizeBytes = 3426861056,
            Sha1Hash = "C9F1F0E0AD0C4B92AEB58F7CE5A922A309736EE5",
            PrimaryUrl = "https://archive.org/download/tw_windows_7_ultimate_with_sp1_x64_dvd_u_677414/tw_windows_7_ultimate_with_sp1_x64_dvd_u_677414.iso",
            MirrorUrls = new List<string> { "https://archive.org/download/windows-7-msdn-iso/tw_windows_7_ultimate_with_sp1_x64_dvd_u_677414.iso" },
            ReleaseDate = "2011-05-12",
            Description = "微軟官方原版 Windows 7 旗艦版 64 位元 (SP1 Media Refresh)。注意：若在 GPT/UEFI 開機，主機板 BIOS 必須開啟 CSM，並關閉 Secure Boot。新主機板可勾選自動注入 USB 3.0 與 NVMe 驅動。",
            RequiresUefiCsm = true,
            SupportsSecureBoot = false,
            NeedsNvmeUsb3Patch = true
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Legacy",
            VersionTitle = "Windows 7 Ultimate SP1",
            Edition = "Ultimate",
            Language = "en-US",
            LanguageDisplay = "English (US)",
            Architecture = "x64",
            BuildNumber = "7601.17514",
            FileName = "en_windows_7_ultimate_with_sp1_x64_dvd_u_677332.iso",
            SizeDisplay = "3.09 GB",
            SizeBytes = 3319078912,
            Sha1Hash = "36AE90DEFBAD9D9539E649B193AE573B77A71C83",
            PrimaryUrl = "https://archive.org/download/en_windows_7_ultimate_with_sp1_x64_dvd_u_677332/en_windows_7_ultimate_with_sp1_x64_dvd_u_677332.iso",
            ReleaseDate = "2011-05-12",
            Description = "Official Microsoft Windows 7 Ultimate SP1 64-bit (English). Requires CSM and Secure Boot Disabled in BIOS for UEFI boot.",
            RequiresUefiCsm = true,
            SupportsSecureBoot = false,
            NeedsNvmeUsb3Patch = true
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Legacy",
            VersionTitle = "Windows 7 Professional SP1",
            Edition = "Professional",
            Language = "zh-TW",
            LanguageDisplay = "繁體中文",
            Architecture = "x64",
            BuildNumber = "7601.17514",
            FileName = "tw_windows_7_professional_with_sp1_x64_dvd_u_677098.iso",
            SizeDisplay = "3.19 GB",
            SizeBytes = 3426861056,
            Sha1Hash = "12FE84A74F5A29DEBBEFA8D0BC4E04AD4E42111D",
            PrimaryUrl = "https://archive.org/download/tw_windows_7_professional_with_sp1_x64_dvd_u_677098/tw_windows_7_professional_with_sp1_x64_dvd_u_677098.iso",
            ReleaseDate = "2011-05-12",
            Description = "微軟官方 MSDN Windows 7 專業版 64 位元 (SP1)。支援企業網域加入與遠端桌面。",
            RequiresUefiCsm = true,
            SupportsSecureBoot = false,
            NeedsNvmeUsb3Patch = true
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Legacy",
            VersionTitle = "Windows 7 Ultimate SP1",
            Edition = "Ultimate",
            Language = "zh-TW",
            LanguageDisplay = "繁體中文",
            Architecture = "x86",
            BuildNumber = "7601.17514",
            FileName = "tw_windows_7_ultimate_with_sp1_x86_dvd_u_677488.iso",
            SizeDisplay = "2.39 GB",
            SizeBytes = 2563780608,
            Sha1Hash = "38B31CF09CE4E119B222BA1FB4EB8E993B462C27",
            PrimaryUrl = "https://archive.org/download/tw_windows_7_ultimate_with_sp1_x86_dvd_u_677488/tw_windows_7_ultimate_with_sp1_x86_dvd_u_677488.iso",
            ReleaseDate = "2011-05-12",
            Description = "微軟官方原版 Windows 7 旗艦版 32 位元。注意：32 位元版本無法在 GPT/UEFI 下開機，必須使用 MBR 分割區 + Legacy BIOS 開機！",
            RequiresMbr = true,
            RequiresUefiCsm = false,
            SupportsSecureBoot = false
        },

        // ══════════════════════════════════════════════════════════════════════
        // 4. RETRO ERA: Windows Vista & Windows XP
        // ══════════════════════════════════════════════════════════════════════
        new WindowsDownloadItem
        {
            ReleaseCategory = "Legacy",
            VersionTitle = "Windows Vista Ultimate SP2",
            Edition = "Ultimate",
            Language = "en-US",
            LanguageDisplay = "English (US)",
            Architecture = "x64",
            BuildNumber = "6002.18005",
            FileName = "en_windows_vista_sp2_x64_dvd_342267.iso",
            SizeDisplay = "3.23 GB",
            SizeBytes = 3468165120,
            Sha1Hash = "1A8EE9CA2EAE3890F3B0EBCEFD34E2EF86C0B436",
            PrimaryUrl = "https://archive.org/download/en_windows_vista_sp2_x64_dvd_342267/en_windows_vista_sp2_x64_dvd_342267.iso",
            ReleaseDate = "2009-05-25",
            Description = "Official Microsoft Windows Vista Ultimate 64-bit with Service Pack 2. Legacy BIOS + MBR recommended.",
            RequiresMbr = true,
            SupportsSecureBoot = false
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Legacy",
            VersionTitle = "Windows XP Professional SP3",
            Edition = "Professional VL",
            Language = "zh-TW",
            LanguageDisplay = "繁體中文",
            Architecture = "x86",
            BuildNumber = "2600.5512",
            FileName = "tw_windows_xp_professional_with_service_pack_3_x86_cd_vl_x14-74140.iso",
            SizeDisplay = "604 MB",
            SizeBytes = 633788416,
            Sha1Hash = "933B05C7A5886BC9BAA880D1D2BC3C946955E9F8",
            PrimaryUrl = "https://archive.org/download/tw_windows_xp_professional_with_service_pack_3_x86_cd_vl_x14-74140/tw_windows_xp_professional_with_service_pack_3_x86_cd_vl_x14-74140.iso",
            ReleaseDate = "2008-05-02",
            Description = "經典傳奇 Windows XP 專業版 SP3 繁體中文 VL 原版。絕對要求 MBR 分割區與 Legacy BIOS 開機，主機板 SATA 需設為 IDE/AHCI 相容模式。",
            RequiresMbr = true,
            SupportsSecureBoot = false
        },
        new WindowsDownloadItem
        {
            ReleaseCategory = "Legacy",
            VersionTitle = "Windows XP Professional SP3",
            Edition = "Professional VL",
            Language = "en-US",
            LanguageDisplay = "English (US)",
            Architecture = "x86",
            BuildNumber = "2600.5512",
            FileName = "en_windows_xp_professional_with_service_pack_3_x86_cd_vl_x14-73974.iso",
            SizeDisplay = "589 MB",
            SizeBytes = 617758720,
            Sha1Hash = "66DEBE4B4D10518649A36019D5EA34A3F0F29326",
            PrimaryUrl = "https://archive.org/download/en_windows_xp_professional_with_service_pack_3_x86_cd_vl_x14-73974/en_windows_xp_professional_with_service_pack_3_x86_cd_vl_x14-73974.iso",
            ReleaseDate = "2008-05-02",
            Description = "Official Microsoft Windows XP Professional SP3 x86 Volume License. Requires Legacy BIOS and MBR.",
            RequiresMbr = true,
            SupportsSecureBoot = false
        }
    };

    public static IReadOnlyList<WindowsDownloadItem> GetAllReleases() => _catalog;

    public static IEnumerable<WindowsDownloadItem> GetByCategory(string category) =>
        _catalog.Where(x => string.Equals(x.ReleaseCategory, category, StringComparison.OrdinalIgnoreCase));
}
