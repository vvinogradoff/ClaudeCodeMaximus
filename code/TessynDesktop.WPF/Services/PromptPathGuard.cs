using System.Globalization;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Decides which paths may go into the prompt. A name with control, format (bidi overrides, zero-width) or line-separator characters is legal
/// on Windows but shows different text than the model receives, so such a path is refused in both modes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PromptPathGuard
{
	/// <summary>True when the path has no character of the Unicode categories Cc, Cf, Zl or Zp.</summary>
	public bool IsSafe(string path) => !path.Any(IsHidden);

	/// <summary>The file name (or the folder's name) for a message, with every hidden character shown as U+FFFD.</summary>
	public string DisplayName(string path)
	{
		var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var name = Path.GetFileName(trimmed);
		var shown = name.Length > 0 ? name : trimmed;
		return string.Concat(shown.Select(c => IsHidden(c) ? '\uFFFD' : c));
	}

	private static bool IsHidden(char c) =>
		CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
