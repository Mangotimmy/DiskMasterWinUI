using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

/// <summary>An editable share permission row used by the share editor dialog.</summary>
public partial class ShareAclGrantItem : ObservableObject
{
    [ObservableProperty] private string _accountName = "";
    [ObservableProperty] private string _accessRight = "Read";

    public string DisplayText => $"{AccountName}  -  {AccessRight}";
}
