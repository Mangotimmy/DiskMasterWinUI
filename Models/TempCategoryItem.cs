using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Represents a category of temporary or cache files eligible for deep cleaning.
/// </summary>
public partial class TempCategoryItem : ObservableObject
{
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private string _sizeDisplay = "0 B";
    [ObservableProperty] private int _fileCount;
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private bool _isSafe = true;
    [ObservableProperty] private string _icon = "📁";

    public string Summary => $"{Name} ({FileCount:N0} 個檔案，共 {SizeDisplay})";
}
