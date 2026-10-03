using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class DiskHealthInfo : ObservableObject
{
    [ObservableProperty] private int _deviceId;
    [ObservableProperty] private string _friendlyName = "";
    [ObservableProperty] private string _mediaType = "";
    [ObservableProperty] private string _busType = "";
    [ObservableProperty] private string _healthStatus = "";
    [ObservableProperty] private string _operationalStatus = "";
    [ObservableProperty] private long _sizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemperatureDisplay))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int? _temperatureCelsius;

    public string SizeDisplay
    {
        get
        {
            double size = SizeBytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
            return $"{size:F1} {units[i]}";
        }
    }

    public string TemperatureDisplay => TemperatureCelsius.HasValue ? $"{TemperatureCelsius}°C" : "N/A";

    public string HealthIcon => HealthStatus switch
    {
        "Healthy" => "✅",
        "Warning" => "⚠️",
        "Unhealthy" => "❌",
        _ => "❓"
    };

    public string Summary => $"{HealthIcon} {FriendlyName} — {MediaType} ({BusType}) — {SizeDisplay} — {HealthStatus} — {TemperatureDisplay}";
}
