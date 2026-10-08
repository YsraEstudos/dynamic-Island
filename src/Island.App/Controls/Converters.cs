using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Island.App.Controls;

/// <summary>true -> Visible, false -> Collapsed. ConverterParameter "Invert" flips the result.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (IsInverted(parameter)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool IsInverted(object? parameter) =>
        parameter is string s && string.Equals(s, "Invert", StringComparison.OrdinalIgnoreCase);
}

/// <summary>null -> Visible, non-null -> Collapsed. ConverterParameter "Invert" flips the result.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNull = value is null;
        if (parameter is string s && string.Equals(s, "Invert", StringComparison.OrdinalIgnoreCase)) isNull = !isNull;
        return isNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
