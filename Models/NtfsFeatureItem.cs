using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class NtfsAceItem : ObservableObject
{
    [ObservableProperty] private string _principal = "";
    [ObservableProperty] private string _accessType = "Allow";
    [ObservableProperty] private string _permissions = "";
    [ObservableProperty] private string _inheritance = "";
    [ObservableProperty] private bool _isInherited;
}

public partial class AlternateDataStreamItem : ObservableObject
{
    [ObservableProperty] private string _streamName = "";
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private string _displaySize = "";
    [ObservableProperty] private bool _isZoneIdentifier;
}

public partial class NtfsFeatureSummary : ObservableObject
{
    [ObservableProperty] private string _owner = "";
    [ObservableProperty] private int _hardlinkCount = 1;
    [ObservableProperty] private List<string> _hardlinkPaths = new();
    [ObservableProperty] private bool _isReparsePoint;
    [ObservableProperty] private string _reparseTarget = "";
    [ObservableProperty] private bool _isCompressed;
    [ObservableProperty] private bool _isEncrypted;
    [ObservableProperty] private bool _isSparse;
    [ObservableProperty] private bool _hasZoneIdentifier;
    [ObservableProperty] private List<NtfsAceItem> _aclEntries = new();
    [ObservableProperty] private List<AlternateDataStreamItem> _alternateStreams = new();
}
