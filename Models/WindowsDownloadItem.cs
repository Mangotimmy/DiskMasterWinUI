using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class WindowsDownloadItem : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ReleaseCategory { get; set; } = "Modern"; // "Modern", "Classic", "Legacy"
    public string Category { get => ReleaseCategory; set => ReleaseCategory = value; }
    public string VersionTitle { get; set; } = "";         // e.g. "Windows 7 Ultimate SP1"
    public string Edition { get; set; } = "";              // "Ultimate", "Pro", "Home", "Enterprise"
    public string Language { get; set; } = "zh-TW";        // "zh-TW", "zh-CN", "en-US"
    public string LanguageDisplay { get; set; } = "繁體中文";
    public string Architecture { get; set; } = "x64";      // "x64", "x86", "ARM64"
    public string BuildNumber { get; set; } = "";          // "7601", "9600", "26100"
    public string FileName { get; set; } = "";
    public string SizeDisplay { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha1Hash { get; set; } = "";
    public string Sha256Hash { get; set; } = "";
    public string PrimaryUrl { get; set; } = "";
    public List<string> MirrorUrls { get; set; } = new();
    public string ReleaseDate { get; set; } = "";
    public string Description { get; set; } = "";

    // Hardware & Boot Requirements
    public bool RequiresMbr { get; set; }                  // e.g. Win7 32-bit, WinXP
    public bool RequiresUefiCsm { get; set; }              // e.g. Win7 64-bit on GPT needs CSM
    public bool SupportsSecureBoot { get; set; }           // Win8/8.1, Win10, Win11
    public bool NeedsNvmeUsb3Patch { get; set; }           // Win7 needs USB3 & NVMe drivers on modern hardware

    // Real-Time Download States
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _speedText = "";
    [ObservableProperty] private string _etaText = "";
    [ObservableProperty] private string _downloadStatus = "待命";
    [ObservableProperty] private string _localFilePath = "";
    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private bool _isHashVerified;
    [ObservableProperty] private string _hashVerificationBadge = "";

    public string DisplayTitle => $"{VersionTitle} ({Architecture}) — {LanguageDisplay}";
    public string HashBadgeDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Sha1Hash)) return $"MSDN SHA-1: {Sha1Hash}";
            if (!string.IsNullOrWhiteSpace(Sha256Hash)) return $"SHA-256: {Sha256Hash}";
            return string.Empty;
        }
    }
}
