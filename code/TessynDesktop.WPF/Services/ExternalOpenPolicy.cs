using System.Globalization;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The only place that decides which files may be handed to the system's default app ("Open in default app"). An allowlist of
/// document and media types (<see cref="Constants.FileTypes.ExternallyOpenable"/>); nothing else is ever launched, so an executable
/// or script (.exe, .bat, .cmd, .ps1, .lnk, .url, .js, .vbs, .msi, .scr, ...) cannot be started from a preview by any route. The
/// name is judged the way Windows will resolve it: trailing dots and spaces are dropped, alternate data streams are refused. A name
/// with a control, format (right-to-left override, zero-width), private-use or surrogate character, or a non-ASCII extension, is
/// refused (V8); the extension is compared ordinal and case-insensitively on the raw text, never through culture or invariant lowering.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ExternalOpenPolicy
{
	private const char Replacement = '�';
	private static readonly char[] Separators = ['\\', '/'];
	private static readonly char[] ForbiddenInName = ['<', '>', '"', '|', '?', '*', ':'];

	/// <summary>True when a file with this name or path may be opened in the default app.</summary>
	public bool IsAllowed(string? fileNameOrPath)
	{
		var extension = ExtensionOf(fileNameOrPath);
		return extension != null && Constants.FileTypes.ExternallyOpenable.Contains(extension);
	}

	/// <summary>The extension Windows would resolve for this name (as written, with the dot), or null when the name is not acceptable.</summary>
	public string? ExtensionOf(string? fileNameOrPath)
	{
		if (string.IsNullOrWhiteSpace(fileNameOrPath))
			return null;

		var name = fileNameOrPath[(fileNameOrPath.LastIndexOfAny(Separators) + 1)..];
		if (name.IndexOfAny(ForbiddenInName) >= 0 || name.Any(IsSpoofingCharacter))
			return null;

		// Windows ignores trailing dots and spaces ("run.exe." is run.exe).
		name = name.TrimEnd('.', ' ');
		var dot = name.LastIndexOf('.');
		if (dot <= 0 || dot == name.Length - 1)
			return null;

		var extension = name[dot..];
		return extension.Any(c => c > 127) ? null : extension;
	}

	/// <summary>The name for titles and labels: spoofing characters (see above) are shown as U+FFFD, so the visible extension is the real one.</summary>
	public string DisplayName(string name) => string.Concat(name.Select(c => IsSpoofingCharacter(c) ? Replacement : c));

	/// <summary>True for a control, format (Cf), private-use (Co) or surrogate (Cs) character.</summary>
	public bool IsSpoofingCharacter(char c) =>
		char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate;
}
