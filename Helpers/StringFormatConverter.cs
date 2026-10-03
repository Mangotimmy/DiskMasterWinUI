using Microsoft.UI.Xaml.Data;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Formats an object value with a string pattern specified via ConverterParameter.
/// e.g. ConverterParameter='IP: {0}'
/// </summary>
public class StringFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null) return string.Empty;
        if (parameter is string format && !string.IsNullOrWhiteSpace(format))
        {
            try
            {
                return string.Format(format, value);
            }
            catch
            {
                return value.ToString() ?? string.Empty;
            }
        }
        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
