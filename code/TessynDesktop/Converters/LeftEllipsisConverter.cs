using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TessynDesktop.Converters;

/// <summary>
/// Left-truncates a string to at most <c>ConverterParameter</c> characters, replacing the dropped
/// prefix with an ellipsis so the distinctive SUFFIX remains visible (e.g. a model ID's version
/// suffix, "claude-opus-4-8" → "…pus-4-8" for ConverterParameter=8) (FR.12.22). Avalonia's
/// <c>TextTrimming</c> only trims trailing text, hence this converter.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LeftEllipsisConverter : IValueConverter
{
	public static readonly LeftEllipsisConverter Instance = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is not string s || string.IsNullOrEmpty(s))
			return value;

		// Presentation-only (FR.12.22): drop a redundant Ollama ":latest" tag before truncating,
		// so the truncated suffix isn't wasted on a tag that carries no distinguishing information.
		s = ModelDisplayText.StripOllamaLatestSuffix(s);

		if (parameter == null || !int.TryParse(parameter.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxChars) || maxChars <= 1)
			return s;

		return s.Length <= maxChars ? s : "…" + s[^(maxChars - 1)..];
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
