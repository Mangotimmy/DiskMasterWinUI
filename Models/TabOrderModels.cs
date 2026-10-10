using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Model representing a configurable tab item for custom reordering, position priority,
/// and enable/disable visibility toggle in application settings.
/// </summary>
public partial class TabOrderItem : ObservableObject
{
    [ObservableProperty] private string _tag = "";
    [ObservableProperty] private string _header = "";
    [ObservableProperty] private string _iconGlyph = "\uE735";
    [ObservableProperty] private int _displayOrder = 1;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string _description = "";

    public string DisplayOrderText => $"#{DisplayOrder}";

    public event Action? IsEnabledChanged;

    partial void OnDisplayOrderChanged(int value)
    {
        OnPropertyChanged(nameof(DisplayOrderText));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        IsEnabledChanged?.Invoke();
    }
}
