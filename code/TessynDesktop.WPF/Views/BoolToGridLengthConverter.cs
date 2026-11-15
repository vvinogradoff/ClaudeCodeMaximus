using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TessynDesktop.WPF.Views;

/// <summary>True gives a star-sized row or column (1*), false gives zero: a cell that takes space only while its content is shown.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BoolToGridLengthConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
		value is true ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
