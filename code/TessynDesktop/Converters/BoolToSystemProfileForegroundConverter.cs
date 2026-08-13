using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TessynDesktop.Converters;

/// <summary>Renders the Profile dropdown's "Default" entry in dark grey; stored profiles stay black.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BoolToSystemProfileForegroundConverter : IValueConverter
{
	public static readonly BoolToSystemProfileForegroundConverter Instance = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value is true ? Brushes.DimGray : Brushes.Black;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
