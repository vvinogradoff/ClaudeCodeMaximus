using System.Globalization;

namespace TessynDesktop.Services;

/// <summary>
/// The tag cloud font size (the chips and the "+N" hint of the session tag area, FR.24): its default, its limits and how a typed value is
/// read. Shared by the settings editor and the code that applies the size, so both agree on what a valid value is.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class TagFontSizeRules
{
	/// <summary>The Avalonia client's size before the setting existed.</summary>
	public const double Default = 11;

	public const double Min = 8;

	public const double Max = 40;

	/// <summary>The size to apply for a stored value: unusable values (NaN, out of range) fall back to <see cref="Default"/>.</summary>
	public static double Normalize(double size) => double.IsFinite(size) && size >= Min && size <= Max ? size : Default;

	/// <summary>Reads a typed size (invariant or current culture); false for blank text, non-numbers and values outside <see cref="Min"/>..<see cref="Max"/>.</summary>
	public static bool TryParse(string? text, out double size)
	{
		size = Default;
		if (string.IsNullOrWhiteSpace(text))
			return false;

		var trimmed = text.Trim();
		if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
			&& !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
			return false;

		if (!double.IsFinite(parsed) || parsed < Min || parsed > Max)
			return false;

		size = parsed;
		return true;
	}

	public static string Format(double size) => size.ToString("0.##", CultureInfo.InvariantCulture);
}
