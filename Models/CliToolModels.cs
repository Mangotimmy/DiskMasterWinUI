using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class PingOptionsModel : ObservableObject
{
    [ObservableProperty] private string _targetHost = "8.8.8.8";
    [ObservableProperty] private int _count = 4;
    [ObservableProperty] private int _bufferSize = 32;
    [ObservableProperty] private int _timeoutMs = 1000;
    [ObservableProperty] private int _ttl = 64;
    [ObservableProperty] private bool _dontFragment = false;
    [ObservableProperty] private bool _resolveHostname = false;
    [ObservableProperty] private bool _continuous = false;
    [ObservableProperty] private bool _forceIPv4 = true;
    [ObservableProperty] private bool _forceIPv6 = false;
}

public partial class NetstatConnectionItem : ObservableObject
{
    [ObservableProperty] private string _protocol = "TCP";
    [ObservableProperty] private string _localAddress = "";
    [ObservableProperty] private int _localPort;
    [ObservableProperty] private string _foreignAddress = "";
    [ObservableProperty] private int _foreignPort;
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private int _pid;
    [ObservableProperty] private string _processName = "";
    [ObservableProperty] private string _executablePath = "";
}

public partial class OpenSharedFileItem : ObservableObject
{
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _accessedBy = "";
    [ObservableProperty] private string _type = "";
    [ObservableProperty] private string _openMode = "";
    [ObservableProperty] private string _openPath = "";
}

public partial class WhereResultItem : ObservableObject
{
    [ObservableProperty] private string _commandName = "";
    [ObservableProperty] private string _fullPath = "";
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private string _displaySize = "";
    [ObservableProperty] private DateTime _timestamp;
    [ObservableProperty] private int _precedenceRank;
    [ObservableProperty] private bool _isConflicting;
}
