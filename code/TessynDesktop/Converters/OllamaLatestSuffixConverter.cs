using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TessynDesktop.Converters;

/// <summary>
/// Strips a trailing Ollama ":latest" tag for display (FR.12.22) — no truncation. Used on the Model
/// dropdown's full-width item list so long entries stay untruncated while still losing the redundant tag.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class OllamaLatestSuffixConverter : IValueConverter
{
	public static readonly OllamaLatestSuffixConverter Instance = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is string s ? ModelDisplayText.StripOllamaLatestSuffix(s) : value;

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
