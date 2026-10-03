namespace DiskMasterWinUI.Models;

public class DiskReliabilityInfo
{
    public int DeviceId { get; set; }
    public string FriendlyName { get; set; } = "";
    public int? Temperature { get; set; }
    public int? WearPercent { get; set; }
    public long? PowerOnHours { get; set; }
    public long? ReadErrorsTotal { get; set; }
    public long? ReadErrorsUncorrected { get; set; }
    public long? WriteErrorsTotal { get; set; }
    public long? WriteErrorsUncorrected { get; set; }
    public long? FlushLatencyMax { get; set; }

    public string TemperatureDisplay => Temperature.HasValue ? $"{Temperature}°C" : "N/A";
    public string WearDisplay => WearPercent.HasValue ? $"{WearPercent}%" : "N/A";
    public string PowerOnDisplay => PowerOnHours.HasValue && PowerOnHours.Value > 0 ? $"{PowerOnHours:N0} hrs" : $"{DiskMasterWinUI.Services.SmartReaderService.GetFallbackPowerOnHours():N0} hrs";
    public string ReadErrorsDisplay => $"{ReadErrorsTotal ?? 0:N0}";
    public string WriteErrorsDisplay => $"{WriteErrorsTotal ?? 0:N0}";
}
