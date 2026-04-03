using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TessynDesktop.Converters;

/// <remarks>Created by Claude</remarks>
public sealed class HexToColorConverter : IValueConverter
{
	public static readonly HexToColorConverter Instance = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is string hex && Color.TryParse(hex, out var color))
			return color;
		return Colors.Transparent;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is Color color)
			return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
		return "#000000";
	}
}
