using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PasteDock.Desktop.Converters;

public sealed class VisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value switch { bool boolean => boolean, int number => number > 0, string text => !string.IsNullOrEmpty(text), _ => false };
        if (parameter is string invert && invert == "Invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
