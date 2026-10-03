namespace DiskMasterWinUI.Models;

/// <summary>
/// Represents an active power request preventing Windows from entering sleep or powering down display.
/// </summary>
public class SleepBlockerItem
{
    public string Category { get; set; } = ""; // DISPLAY, SYSTEM, AWAYMODE, EXECUTION, PERFBOOST
    public string CallerType { get; set; } = ""; // PROCESS, SERVICE, DRIVER
    public string CallerName { get; set; } = "";
    public string Reason { get; set; } = "";

    public string CategoryIcon => Category.ToUpperInvariant() switch
    {
        "DISPLAY" => "🖥️",
        "SYSTEM" => "⚡",
        "AWAYMODE" => "🌙",
        "EXECUTION" => "⚙️",
        "PERFBOOST" => "🚀",
        _ => "⚠️"
    };

    public string Summary => $"{CategoryIcon} [{Category}] {CallerType}: {CallerName} — {Reason}";
}
