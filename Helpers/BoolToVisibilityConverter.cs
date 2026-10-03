using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace DiskMasterWinUI.Helpers;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = value is bool flag && flag;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility v)
        {
            var b = (v == Visibility.Visible);
            return Invert ? !b : b;
        }
        return false;
    }
}
