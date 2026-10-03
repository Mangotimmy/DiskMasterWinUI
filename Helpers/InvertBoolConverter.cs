using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace DiskMasterWinUI.Helpers;

public class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = value is bool flag && flag;

        // If target is Visibility, return Collapsed for true, Visible for false
        if (targetType == typeof(Visibility))
        {
            return b ? Visibility.Collapsed : Visibility.Visible;
        }

        // If target is string, return inverted boolean as text
        if (targetType == typeof(string))
        {
            if (value is bool) return (!b).ToString();
            return value?.ToString() ?? "";
        }

        if (value is bool)
        {
            return !b;
        }

        // Fallback for non-bool inputs
        return value ?? false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility v)
        {
            return v != Visibility.Visible;
        }
        if (value is bool b)
        {
            return !b;
        }
        return false;
    }
}
