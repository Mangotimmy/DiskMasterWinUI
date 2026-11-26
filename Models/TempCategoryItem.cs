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
    // Opt-in by default: nothing is ever destroyed without the user ticking it.
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isSafe = true;
    [ObservableProperty] private string _icon = "📁";

    /// <summary>
    /// True for categories whose deletion is irreversible or breaks installed software
    /// (Windows.old rollback image, installer Package Cache, component store logs, ...).
    /// Risky categories are never pre-selected and are never bulk-selected by "select all".
    /// </summary>
    [ObservableProperty] private bool _isRisky;

    [ObservableProperty] private string _riskText = "";

    public string Summary => $"{Name} ({FileCount:N0} 個檔案，共 {SizeDisplay})";

    public bool HasRiskText => !string.IsNullOrWhiteSpace(RiskText);
}
