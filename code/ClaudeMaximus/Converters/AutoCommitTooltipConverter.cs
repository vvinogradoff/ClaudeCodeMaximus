using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ClaudeMaximus.Converters;

/// <summary>
/// Renders the tooltip for the tri-state Auto-Commit toggle (FR.11.3, FR.11.9), describing the
/// active state and which state a click will move to next.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AutoCommitTooltipConverter : IValueConverter
{
	public static readonly AutoCommitTooltipConverter Instance = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value switch
		{
			true  => "Auto-Commit: Commit (click for No preference)",
			false => "Auto-Commit: Do-not-commit (click for Commit)",
			_     => "Auto-Commit: No preference (click for Do-not-commit)",
		};

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
