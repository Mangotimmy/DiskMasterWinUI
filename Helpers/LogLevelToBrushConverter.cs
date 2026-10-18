using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Converts ActionLogItem level text (ACTION, REQUEST, ERROR, WARN, SUCCESS, etc.)
/// into safe UI thread SolidColorBrush instances.
/// </summary>
public class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var level = value as string ?? string.Empty;
        return level switch
        {
            "ACTION" => new SolidColorBrush(Color.FromArgb(255, 14, 165, 233)),   // Sky Blue
            "REQUEST" => new SolidColorBrush(Color.FromArgb(255, 139, 92, 246)), // Violet
            "ERROR" => new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)),    // Red
            "WARN" => new SolidColorBrush(Color.FromArgb(255, 245, 158, 11)),    // Amber
            "SUCCESS" => new SolidColorBrush(Color.FromArgb(255, 16, 185, 129)), // Emerald Green
            "OUTPUT" => new SolidColorBrush(Color.FromArgb(255, 20, 184, 166)),  // Teal: raw CMD output
            _ => new SolidColorBrush(Color.FromArgb(255, 107, 114, 128))        // Gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
