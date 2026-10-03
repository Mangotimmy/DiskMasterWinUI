using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

public partial class VssShadowItem : ObservableObject
{
    [ObservableProperty] private string _shadowId = "";
    [ObservableProperty] private string _originalVolume = "";
    [ObservableProperty] private string _creationTime = "";
    [ObservableProperty] private string _attributes = "";

    public string DisplayTitle => $"快照: {OriginalVolume} ({CreationTime})";
    public string DisplayTitleEn => $"Shadow: {OriginalVolume} ({CreationTime})";
    public string Summary => $"ID: {ShadowId} | {Attributes}";
}
