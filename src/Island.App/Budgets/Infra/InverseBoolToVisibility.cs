using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Island.App.Budgets;

/// <summary>Converte bool para Visibility invertido: true -> Collapsed, false -> Visible.</summary>
public sealed class InverseBoolToVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
