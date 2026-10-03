using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Model representing the Windows TCP/IP network protocol stack parameters and optimization states.
/// </summary>
public partial class TcpSettingsItem : ObservableObject
{
    [ObservableProperty]
    private string _autoTuningLevel = "Normal";

    [ObservableProperty]
    private string _congestionProvider = "BBR";

    [ObservableProperty]
    private string _ecnCapability = "Enabled";

    [ObservableProperty]
    private string _rss = "Enabled";

    [ObservableProperty]
    private string _rsc = "Enabled";

    [ObservableProperty]
    private string _timestamps = "Disabled";

    [ObservableProperty]
    private string _initialRto = "3000";

    [ObservableProperty]
    private string _minRto = "300";

    [ObservableProperty]
    private bool _isMaxThroughputProfile;

    [ObservableProperty]
    private string _rawGlobalOutput = "";

    public string Summary => $"AutoTuning: {AutoTuningLevel} | Congestion: {CongestionProvider} | ECN: {EcnCapability} | RSS: {Rss} | RSC: {Rsc}";
}
