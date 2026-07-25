using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TessynDesktop.Converters;

/// <summary>
/// Renders the glyph for the tri-state Auto-Commit toggle (FR.11.3, FR.11.9):
/// Commit (true), Do-not-commit (false), No preference (null).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AutoCommitGlyphConverter : IValueConverter
{
	public static readonly AutoCommitGlyphConverter Instance = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value switch
		{
			true  => "✓", // checkmark — Commit
			false => "⊘", // circled division slash — Do-not-commit
			_     => "–", // en dash — No preference
		};

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
