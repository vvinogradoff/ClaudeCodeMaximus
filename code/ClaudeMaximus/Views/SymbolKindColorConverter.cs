using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Views;

/// <remarks>Created by Claude</remarks>
public sealed class SymbolKindColorConverter : IValueConverter
{
	public static readonly SymbolKindColorConverter Instance = new();

	private static readonly SolidColorBrush ClassBrush     = new(Color.Parse("#4EC9B0"));
	private static readonly SolidColorBrush EnumBrush      = new(Color.Parse("#B8D7A3"));
	private static readonly SolidColorBrush StructBrush    = new(Color.Parse("#86C691"));
	private static readonly SolidColorBrush RecordBrush    = new(Color.Parse("#4EC9B0"));
	private static readonly SolidColorBrush InterfaceBrush = new(Color.Parse("#B8D7A3"));
	private static readonly SolidColorBrush MethodBrush    = new(Color.Parse("#DCDCAA"));
	private static readonly SolidColorBrush PropertyBrush  = new(Color.Parse("#9CDCFE"));
	private static readonly SolidColorBrush FileBrush      = new(Color.Parse("#C8C8C8"));

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is not CodeSymbolKind kind)
			return FileBrush;

		return kind switch
		{
			CodeSymbolKind.Class     => ClassBrush,
			CodeSymbolKind.Enum      => EnumBrush,
			CodeSymbolKind.Struct    => StructBrush,
			CodeSymbolKind.Record    => RecordBrush,
			CodeSymbolKind.Interface => InterfaceBrush,
			CodeSymbolKind.Method    => MethodBrush,
			CodeSymbolKind.Property  => PropertyBrush,
			_                        => FileBrush,
		};
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
