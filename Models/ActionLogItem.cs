using System;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Model representing a real-time recorded user action, system request, or diagnostic entry.
/// </summary>
public class ActionLogItem
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Level { get; set; } = "ACTION"; // ACTION, REQUEST, INFO, WARN, ERROR, SUCCESS, OUTPUT
    public string Category { get; set; } = "General";
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;

    public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    public string FullExportLine => $"[{FormattedTime}] [{Level}] [{Category}] {Action}{(string.IsNullOrWhiteSpace(Details) ? "" : $" | {Details}")}";

    /// <summary>Single-line rendering for the compact live console (no timestamp/level prefix).</summary>
    public string FullLineForConsole
    {
        get
        {
            var line = string.IsNullOrWhiteSpace(Details) ? $"{Category} · {Action}" : $"{Category} · {Action} — {Details}";
            return line.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
