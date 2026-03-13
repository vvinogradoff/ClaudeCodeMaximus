using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Views;

/// <remarks>Created by Claude</remarks>
public sealed class SymbolKindConverter : IValueConverter
{
	public static readonly SymbolKindConverter Instance = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is not CodeSymbolKind kind)
			return "F"; // File

		return kind switch
		{
			CodeSymbolKind.Class     => "C",
			CodeSymbolKind.Enum      => "E",
			CodeSymbolKind.Struct    => "S",
			CodeSymbolKind.Record    => "R",
			CodeSymbolKind.Interface => "I",
			CodeSymbolKind.Method    => "M",
			CodeSymbolKind.Property  => "P",
			_                        => "?",
		};
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
