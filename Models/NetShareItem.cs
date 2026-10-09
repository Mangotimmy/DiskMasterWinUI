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
}
