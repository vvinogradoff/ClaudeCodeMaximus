using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TessynDesktop.Views;

/// <summary>
/// Converts color name strings ("Green", "Orange", "Red", "Gray") to SolidColorBrush instances.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Green"  => new SolidColorBrush(Color.Parse("#4CAF50")),
            "Orange" => new SolidColorBrush(Color.Parse("#FF9800")),
            "Red"    => new SolidColorBrush(Color.Parse("#F44336")),
            _        => new SolidColorBrush(Color.Parse("#9E9E9E")),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
