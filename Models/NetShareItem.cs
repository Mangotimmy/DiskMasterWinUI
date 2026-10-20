using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class NetShareItem : ObservableObject
{
    [ObservableProperty] private string _shareName = "";
    [ObservableProperty] private string _resourcePath = "";
    [ObservableProperty] private string _remark = "";
    [ObservableProperty] private bool _isAdminShare;
    [ObservableProperty] private string _uncIpPath = "";
    [ObservableProperty] private string _uncHostPath = "";
    [ObservableProperty] private string _accessSummary = "";
    [ObservableProperty] private int _concurrentUserLimit;
    [ObservableProperty] private string _cachingMode = "";
    [ObservableProperty] private bool _encryptData;
    [ObservableProperty] private string _folderEnumerationMode = "";
    [ObservableProperty] private int _permissionCount;
}
