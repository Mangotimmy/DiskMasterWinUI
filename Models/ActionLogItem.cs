using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Model representing a real-time recorded user action, system request, or diagnostic entry.
/// </summary>
public class ActionLogItem
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Level { get; set; } = "ACTION"; // ACTION, REQUEST, INFO, WARN, ERROR, SUCCESS
    public string Category { get; set; } = "General";
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;

    public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");

    private static readonly SolidColorBrush s_actionBrush = new(Color.FromArgb(255, 14, 165, 233));   // Sky Blue
    private static readonly SolidColorBrush s_requestBrush = new(Color.FromArgb(255, 139, 92, 246)); // Violet
    private static readonly SolidColorBrush s_errorBrush = new(Color.FromArgb(255, 239, 68, 68));    // Red
    private static readonly SolidColorBrush s_warnBrush = new(Color.FromArgb(255, 245, 158, 11));    // Amber
    private static readonly SolidColorBrush s_successBrush = new(Color.FromArgb(255, 16, 185, 129)); // Emerald Green
    private static readonly SolidColorBrush s_defaultBrush = new(Color.FromArgb(255, 107, 114, 128)); // Gray

    public SolidColorBrush LevelBadgeBrush => Level switch
    {
        "ACTION" => s_actionBrush,
        "REQUEST" => s_requestBrush,
        "ERROR" => s_errorBrush,
        "WARN" => s_warnBrush,
        "SUCCESS" => s_successBrush,
        _ => s_defaultBrush
    };

    public Visibility HasDetailsVisibility => string.IsNullOrWhiteSpace(Details) ? Visibility.Collapsed : Visibility.Visible;

    public string FullExportLine => $"[{FormattedTime}] [{Level}] [{Category}] {Action}{(string.IsNullOrWhiteSpace(Details) ? "" : $" | {Details}")}";
}
