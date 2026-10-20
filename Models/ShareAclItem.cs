using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class ShareAclItem : ObservableObject
{
    [ObservableProperty] private string _principal = "";
    [ObservableProperty] private string _accessRight = "";
    [ObservableProperty] private string _accessType = "Allow";
    [ObservableProperty] private string _scopeName = "";
}

public partial class ShareDetailsItem : ObservableObject
{
    [ObservableProperty] private string _shareName = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private int _concurrentUserLimit;
    [ObservableProperty] private string _cachingMode = "";
    [ObservableProperty] private bool _encryptData;
    [ObservableProperty] private string _folderEnumerationMode = "";
    [ObservableProperty] private string _availabilityType = "";
    [ObservableProperty] private string _scopeName = "";
    [ObservableProperty] private List<ShareAclItem> _aclEntries = new();
}
