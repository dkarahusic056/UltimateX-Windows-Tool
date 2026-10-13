using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using UltimateX.Core.Common;

namespace UltimateX.App.Converters;

/// <summary>Grün = OK, Orange = Hinweis, Rot = Problem, Grau = nicht verfügbar (§4).</summary>
public sealed class DataStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            DataState.Ok => "Brush.Ok",
            DataState.Warning => "Brush.Warning",
            DataState.Problem => "Brush.Problem",
            _ => "Brush.Na",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>null/leer → Collapsed. Mit ConverterParameter="invert" umgekehrt.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = value is null
                      || (value is string s && string.IsNullOrWhiteSpace(s))
                      || (value is int i && i == 0) // z. B. Binding auf Collection.Count
                      || (value is System.Collections.ICollection c && c.Count == 0);
        if (parameter as string == "invert") isEmpty = !isEmpty;
        return isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
