using System.Text.Json;

namespace DiskMasterWinUI.Services;

public class AppSettings
{
    public string Theme { get; set; } = "System";
    public int ScalePercent { get; set; } = 100;
    public string Language { get; set; } = "Auto";
    public bool EnableSafetyConfirmations { get; set; } = true;
    public string DefaultPartitionStyle { get; set; } = "GPT";
    public string DefaultFileSystem { get; set; } = "NTFS";
    public string DefaultBackupFolder { get; set; } = @"D:\Backup";
    public int MaxLogBufferLines { get; set; } = 5000;

    // Custom Background Wallpaper (JPG, PNG, WEBP, BMP)
    public string BackgroundImagePath { get; set; } = "";
    public double BackgroundOpacity { get; set; } = 0.25;
    public double BackgroundBlur { get; set; } = 0.0;

    // Live2D / 3D VRM Companion Avatar (CrystalDiskInfo-Style 2D Model)
    public bool EnableCompanionAvatar { get; set; } = true;
    public string CompanionAvatarType { get; set; } = "VRM"; // "VRM" or "Live2D"
    public string CompanionModelPath { get; set; } = "";
    public double CompanionScale { get; set; } = 1.0;
    public string CompanionPosition { get; set; } = "BottomRight"; // "BottomRight", "BottomLeft", "TopRight", "TopLeft", "Free"
    public double CompanionOffsetX { get; set; } = 0.0;
    public double CompanionOffsetY { get; set; } = 0.0;
    public bool CompanionShowBadge { get; set; } = true;
    public bool CompanionEnableGazeTracking { get; set; } = true;
    public string CompanionEmotionState { get; set; } = "Auto"; // "Auto", "Happy", "Caution", "Bad", "Working"

    // Glass / Card Transparency
    public bool EnableGlassTransparency { get; set; } = true;
    public double CardOpacity { get; set; } = 0.60;

    // ── HTTP Range & Parallel Downloader Settings ──
    public bool HttpRangeParallelEnabled { get; set; } = true;
    public int ParallelThreadCount { get; set; } = 8;
    public int ChunkBufferSizeKB { get; set; } = 256;
    public bool KeepResumeCache { get; set; } = true;
    public string GlobalDownloadPath { get; set; } = "";
    public string ProxyMode { get; set; } = "System"; // "Direct", "System", "Custom"
    public string CustomProxyUrl { get; set; } = "";
    public int DownloadTimeoutSeconds { get; set; } = 60;
    public int DownloadMaxRetries { get; set; } = 3;

    // ── TCP Network Stack Settings ──
    public string TcpAutoTuningLevel { get; set; } = "Normal";
    public string TcpCongestionProvider { get; set; } = "BBR";
    public bool TcpEcnEnabled { get; set; } = true;
    public bool TcpRssEnabled { get; set; } = true;
    public bool TcpRscEnabled { get; set; } = true;
    public bool TcpTimestampsDisabled { get; set; } = true;

    // ── Prompt Windows & Diagnostics ──
    public bool EnablePromptWindows { get; set; } = true;

    // ── Custom Fonts & Typography ──
    public string CustomFontFamily { get; set; } = "Segoe UI Variable, Microsoft JhengHei UI";
    public double CustomFontScale { get; set; } = 100.0;

    // ── Adobe Workspace Presets ──
    public string WorkspacePreset { get; set; } = "Master"; // "Master", "Deploy", "Repair", "Gaming", "Lite"

    // ── Dual Mode & Starter Hub ──
    public bool IsEasyMode { get; set; } = false;

    // ── Auto Updater ──
    public bool AutoCheckUpdates { get; set; } = true;
    public string UpdateFrequency { get; set; } = "Daily"; // "Manual", "Daily", "Weekly"

    // ── System Tray & Window Notifications ──
    public bool MinimizeToTray { get; set; } = false;
    public bool CloseToTray { get; set; } = false;
    public bool EnableTaskbarFlash { get; set; } = true;
    public bool EnableAudioFeedback { get; set; } = true;
    public string NotificationSoundScheme { get; set; } = "Default"; // "Default", "Chimes", "Ding", "Tada", "Crystal", "Custom"
    public string CustomNotificationSoundPath { get; set; } = "";
    public bool EnableWindowsNotificationCenter { get; set; } = true;

    // ── Chrome Modular Tab & Layout Settings ──
    public bool EnableCustomTabReorder { get; set; } = true;
    public List<string> CustomTabOrder { get; set; } = new();
    public List<string> DisabledTabTags { get; set; } = new();
    public Dictionary<string, bool> ComponentVisibility { get; set; } = new();
    public Dictionary<string, double> LayoutSplitterSizes { get; set; } = new();
    public List<string> DiskHealthCardOrder { get; set; } = new();

    // ── Performance Overlay / FPS HUD Settings ──
    public string HudStyle { get; set; } = "Bar"; // "Bar", "Card", "Badge"
    public double HudOpacity { get; set; } = 0.85; // 0.0 - 1.0 (MSI Afterburner style)
    public bool HudShowFps { get; set; } = true;
    public bool HudShowLow1Percent { get; set; } = true;
    public bool HudShowMsDelay { get; set; } = true;
    public bool HudShowCpu { get; set; } = true;
    public bool HudShowGpu { get; set; } = true;
    public bool HudShowRam { get; set; } = true;
    public bool HudShowNet { get; set; } = true;
    public bool HudShowApi { get; set; } = true;
    public bool HudShowAppName { get; set; } = true;
    public int? HudCustomPositionX { get; set; } = null;
    public int? HudCustomPositionY { get; set; } = null;

    // ── Process CPU Priority Rules ──
    public Dictionary<string, string> ProcessCpuPriorities { get; set; } = new();

    // ── Debug & Diagnostic Logging ──
    public bool EnableDebugLogging { get; set; } = false;
}

public class SettingsService
{
    private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
    public static SettingsService Instance => _instance.Value;

    private readonly string _settingsFilePath;
    private readonly object _saveLock = new();
    public AppSettings Current { get; private set; } = new();

    public event Action? SettingsChanged;

    private SettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskMaster_Portable");
        Directory.CreateDirectory(dir);
        _settingsFilePath = Path.Combine(dir, "settings.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    Current = loaded;
                }
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            lock (_saveLock)
            {
                var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });

                // Atomic swap: File.WriteAllText truncates first, and a torn/partial write makes
                // Load() silently fall back to defaults for every setting in the file.
                var tempPath = _settingsFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                try
                {
                    if (File.Exists(_settingsFilePath))
                    {
                        File.Replace(tempPath, _settingsFilePath, null);
                    }
                    else
                    {
                        File.Move(tempPath, _settingsFilePath);
                    }
                }
                catch (IOException)
                {
                    // File.Replace can fail while another reader holds the file briefly.
                    File.Move(tempPath, _settingsFilePath, overwrite: true);
                }
            }

            SettingsChanged?.Invoke();
        }
        catch { }
    }
}
