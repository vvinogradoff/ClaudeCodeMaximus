using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TessynDesktop.WPF.Views;

/// <summary>Collapsed when the bound value is true, visible otherwise.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class InvertedBooleanToVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
		value is true ? Visibility.Collapsed : Visibility.Visible;

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
