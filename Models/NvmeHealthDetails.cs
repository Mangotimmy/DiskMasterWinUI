using CommunityToolkit.Mvvm.ComponentModel;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Detailed NVMe Health Information Log per NVMe 1.4 / 2.0 specifications.
/// Inherits from ObservableObject to support dynamic temperature unit switching (°C / °F)
/// and live UI data binding.
/// </summary>
public partial class NvmeHealthDetails : ObservableObject
{
    [ObservableProperty] private int _deviceId;
    [ObservableProperty] private string _modelName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompositeTemperatureDisplay))]
    [NotifyPropertyChangedFor(nameof(CompositeTemperatureCelsius))]
    [NotifyPropertyChangedFor(nameof(CompositeTemperatureFahrenheit))]
    private int _compositeTemperatureKelvin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompositeTemperatureDisplay))]
    private bool _isFahrenheit;

    [ObservableProperty] private short _criticalTemperature = 94;
    [ObservableProperty] private short _warningTemperature = 90;

    // Critical Warnings bitmask:
    [ObservableProperty] private byte _criticalWarning;
    public bool SpareBelowThreshold => (CriticalWarning & 0x01) != 0;
    public bool TemperatureThresholdExceeded => (CriticalWarning & 0x02) != 0;
    public bool ReliabilityDegraded => (CriticalWarning & 0x04) != 0;
    public bool ReadOnlyMode => (CriticalWarning & 0x08) != 0;
    public bool VolatileMemoryBackupFailed => (CriticalWarning & 0x10) != 0;

    // Temperature calculations
    public int CompositeTemperatureCelsius => CompositeTemperatureKelvin > 273 ? CompositeTemperatureKelvin - 273 : CompositeTemperatureKelvin;
    public int CompositeTemperatureFahrenheit => (int)Math.Round(CompositeTemperatureCelsius * 9.0 / 5.0 + 32.0);

    public string CompositeTemperatureDisplay => IsFahrenheit
        ? $"{CompositeTemperatureFahrenheit}°F"
        : $"{CompositeTemperatureCelsius}°C";

    // Spare & Wear
    [ObservableProperty] private byte _availableSpare = 100;
    [ObservableProperty] private byte _availableSpareThreshold = 10;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HealthPercent))]
    [NotifyPropertyChangedFor(nameof(HealthPercentDisplay))]
    [NotifyPropertyChangedFor(nameof(PercentageUsedDisplay))]
    [NotifyPropertyChangedFor(nameof(HealthStatus))]
    [NotifyPropertyChangedFor(nameof(HealthIcon))]
    private byte _percentageUsed;

    public int HealthPercent => Math.Max(0, 100 - PercentageUsed);
    public string HealthPercentDisplay => $"{HealthPercent}% ({HealthStatus})";
    public string PercentageUsedDisplay => $"{PercentageUsed}% 壽命耗損";

    // Host Reads & Writes
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalWrittenDisplay))]
    private double _totalBytesWrittenTB;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalReadDisplay))]
    private double _totalBytesReadTB;

    // Reliability & Lifetime
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PowerOnHoursDisplay))]
    private ulong _powerOnHours;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PowerCyclesDisplay))]
    private ulong _powerCycles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnsafeShutdownsDisplay))]
    private ulong _unsafeShutdowns;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MediaErrorsDisplay))]
    private ulong _mediaErrors;

    [ObservableProperty] private ulong _numErrLogEntries;

    // Up to 8 thermal sensors (°C)
    public List<int> SensorTemperatures { get; set; } = new();

    public string HealthStatus => PercentageUsed switch
    {
        <= 10 => LocalizationService.T("良好 (極佳)", "良好 (极佳)", "Excellent", "良好 (極めて良好)"),
        <= 30 => LocalizationService.T("正常 (良好)", "正常 (良好)", "Good", "正常 (良好)"),
        <= 60 => LocalizationService.T("注意 (尚可)", "注意 (尚可)", "Fair", "注意 (普通)"),
        _ => LocalizationService.T("警告 (壽命告急)", "警告 (寿命告急)", "Warning", "警告 (寿命低下)")
    };

    public string HealthIcon => PercentageUsed switch
    {
        <= 30 => "✅",
        <= 60 => "⚠️",
        _ => "❌"
    };

    public string TotalWrittenDisplay => TotalBytesWrittenTB > 0 ? $"{TotalBytesWrittenTB:F2} TB" : "< 0.01 TB";
    public string TotalReadDisplay => TotalBytesReadTB > 0 ? $"{TotalBytesReadTB:F2} TB" : "< 0.01 TB";

    public string PowerOnHoursDisplay => PowerOnHours > 0
        ? LocalizationService.T(
            $"{PowerOnHours:N0} 小時 ({PowerOnHours / 24:N0} 天)",
            $"{PowerOnHours:N0} 小时 ({PowerOnHours / 24:N0} 天)",
            $"{PowerOnHours:N0} hrs ({PowerOnHours / 24:N0} days)",
            $"{PowerOnHours:N0} 時間 ({PowerOnHours / 24:N0} 日)")
        : LocalizationService.T("剛啟用", "刚启用", "Newly deployed", "稼働開始直後");

    public string PowerCyclesDisplay => PowerCycles > 0
        ? LocalizationService.T($"{PowerCycles:N0} 次", $"{PowerCycles:N0} 次", $"{PowerCycles:N0} cycles", $"{PowerCycles:N0} 回")
        : LocalizationService.T("1 次", "1 次", "1 cycle", "1 回");

    public string UnsafeShutdownsDisplay => LocalizationService.T(
        $"{UnsafeShutdowns:N0} 次",
        $"{UnsafeShutdowns:N0} 次",
        $"{UnsafeShutdowns:N0} times",
        $"{UnsafeShutdowns:N0} 回");

    public string MediaErrorsDisplay => $"{MediaErrors:N0}";

    public string AvailableSpareDisplay => LocalizationService.T(
        $"{AvailableSpare}% (門檻: {AvailableSpareThreshold}%)",
        $"{AvailableSpare}% (阈值: {AvailableSpareThreshold}%)",
        $"{AvailableSpare}% (Threshold: {AvailableSpareThreshold}%)",
        $"{AvailableSpare}% (しきい値: {AvailableSpareThreshold}%)");
}
