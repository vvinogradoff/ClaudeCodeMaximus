namespace TessynDesktop.WPF.Services;

/// <summary>
/// The only place that decides which files may be handed to the system's default app ("Open in default app"). An allowlist of
/// document and media types (<see cref="Constants.FileTypes.ExternallyOpenable"/>); nothing else is ever launched, so an executable
/// or script (.exe, .bat, .cmd, .ps1, .lnk, .url, .js, .vbs, .msi, .scr, ...) cannot be started from a preview by any route. The
/// name is judged the way Windows will resolve it: trailing dots and spaces are dropped, alternate data streams are refused.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ExternalOpenPolicy
{
	private static readonly char[] Separators = ['\\', '/'];
	private static readonly char[] ForbiddenInName = ['<', '>', '"', '|', '?', '*', ':'];

	/// <summary>True when a file with this name or path may be opened in the default app.</summary>
	public bool IsAllowed(string? fileNameOrPath)
	{
		var extension = ExtensionOf(fileNameOrPath);
		return extension != null && Constants.FileTypes.ExternallyOpenable.Contains(extension);
	}

	/// <summary>The extension Windows would resolve for this name (lower case, with the dot), or null when the name is not acceptable.</summary>
	public string? ExtensionOf(string? fileNameOrPath)
	{
		if (string.IsNullOrWhiteSpace(fileNameOrPath))
			return null;

		var name = fileNameOrPath[(fileNameOrPath.LastIndexOfAny(Separators) + 1)..];
		if (name.IndexOfAny(ForbiddenInName) >= 0 || name.Any(char.IsControl))
			return null;

		// Windows ignores trailing dots and spaces ("run.exe." is run.exe).
		name = name.TrimEnd('.', ' ');
		var dot = name.LastIndexOf('.');
		if (dot <= 0 || dot == name.Length - 1)
			return null;

		return name[dot..].ToLowerInvariant();
	}
}
