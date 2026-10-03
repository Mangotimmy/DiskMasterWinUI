using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskMasterWinUI.Models;

/// <summary>
/// Encapsulates Windows installation image index metadata (WIM / ESD / ISO).
/// Intelligently formats architecture, build version, and human-readable capacity.
/// </summary>
public partial class WimImageInfo : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private int _index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _sizeDisplay = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _architecture = "";

    [ObservableProperty]
    private string _edition = "";

    [ObservableProperty]
    private string _version = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _buildNumber = "";

    [ObservableProperty]
    private long _sizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _formattedSize = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _marketingVersion = "";

    /// <summary>
    /// Intelligently formats the image display title for UI selection.
    /// Never outputs empty parenthesis '()' and provides clean human-readable sizing.
    /// E.g.: [1] Windows 11 企業評估版 (x64, 24H2) — 21.68 GB
    /// </summary>
    public string DisplayName
    {
        get
        {
            var tags = new List<string>();
            if (!string.IsNullOrWhiteSpace(Architecture))
            {
                tags.Add(Architecture);
            }
            if (!string.IsNullOrWhiteSpace(MarketingVersion))
            {
                tags.Add(MarketingVersion);
            }
            else if (!string.IsNullOrWhiteSpace(BuildNumber))
            {
                tags.Add($"Build {BuildNumber}");
            }

            var tagPart = tags.Count > 0 ? $" ({string.Join(", ", tags)})" : "";
            var sizeStr = !string.IsNullOrWhiteSpace(FormattedSize) ? FormattedSize : SizeDisplay;
            var sizePart = !string.IsNullOrWhiteSpace(sizeStr) ? $" — {sizeStr}" : "";

            return $"[{Index}] {Name}{tagPart}{sizePart}";
        }
    }
}
