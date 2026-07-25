using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TessynDesktop.Converters;

/// <remarks>Created by Claude</remarks>
public sealed class InverseBooleanConverter : IValueConverter
{
	public static readonly InverseBooleanConverter Instance = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value is bool b ? !b : value;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value is bool b ? !b : value;
}
