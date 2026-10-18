namespace DiskMasterWinUI.Models;

/// <summary>
/// State snapshot of Explorer Advanced and Shell latency/responsiveness registry settings
/// located under HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced & Explorer.
/// </summary>
public class ExplorerAdvancedState
{
    // UI Latency & Animations
    public bool TaskbarAnimationsDisabled { get; set; }
    public bool DesktopLivePreviewInstant { get; set; }
    public bool ExtendedUIHoverTimeInstant { get; set; }
    public bool DisallowShakingEnabled { get; set; }

    // Disk I/O & Shell Stability
    public bool SeparateProcessEnabled { get; set; }
    public bool NonetCrawlingDisabled { get; set; }
    public bool IconsOnlyEnabled { get; set; }
    public bool HideFileExtDisabled { get; set; } // true when HideFileExt == 0 (showing extensions)

    // Memory, Telemetry & CPU C-State Preservation
    public bool TaskbarDaDisabled { get; set; } // true when Widgets are disabled (TaskbarDa == 0)
    public bool ShowCopilotButtonDisabled { get; set; } // true when Copilot is disabled
    public bool StartIrisRecommendationsDisabled { get; set; } // true when Start ads/telemetry disabled
    public bool StartTrackDocsDisabled { get; set; } // true when MRU doc logging disabled
    public bool ShowSecondsInSystemClockDisabled { get; set; } // true when seconds disabled (C-state friendly)
    public bool QuickAccessRecentDisabled { get; set; } // true when ShowRecent and ShowFrequent == 0

    /// <summary>
    /// Calculated percentage of recommended /boost optimizations currently applied (0-100%).
    /// </summary>
    public int OptimizationScorePercent
    {
        get
        {
            int total = 10;
            int matched = 0;
            if (TaskbarAnimationsDisabled) matched++;
            if (DesktopLivePreviewInstant) matched++;
            if (DisallowShakingEnabled) matched++;
            if (SeparateProcessEnabled) matched++;
            if (NonetCrawlingDisabled) matched++;
            if (TaskbarDaDisabled) matched++;
            if (ShowCopilotButtonDisabled) matched++;
            if (StartIrisRecommendationsDisabled) matched++;
            if (StartTrackDocsDisabled) matched++;
            if (QuickAccessRecentDisabled) matched++;
            return (int)Math.Round((double)matched / total * 100);
        }
    }
}
