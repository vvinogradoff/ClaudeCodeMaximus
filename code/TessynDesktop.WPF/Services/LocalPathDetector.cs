using System.Text.RegularExpressions;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Finds absolute local file paths (<c>C:\dir\file.ext</c>, <c>\\server\share\file.ext</c>) in an entry's text (FR.26.5). A path in
/// backticks or quotes may contain spaces; a bare one ends at whitespace. Only paths whose last segment has an extension count (a
/// directory is not previewable), at most <see cref="Constants.Limits.MaxPathChipsPerEntry"/> per entry. The result is exactly the
/// text as it occurs in the entry, because the daemon verifies that before it issues a file handle.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class LocalPathDetector
{
	/// <summary>True when <paramref name="path"/> has the shape of an absolute local path with a file name; the same test the detector applies.</summary>
	public bool IsPathShaped(string? path)
	{
		if (string.IsNullOrEmpty(path) || path.Length > Constants.Limits.MaxPathChipLength || path.Any(char.IsControl))
			return false;

		return Shape().IsMatch(path) && LeafHasExtension(path);
	}

	/// <summary>The local file paths in <paramref name="content"/>, in order of appearance, without duplicates.</summary>
	public IReadOnlyList<string> Detect(string content)
	{
		if (string.IsNullOrEmpty(content) || (!content.Contains(":\\", StringComparison.Ordinal) && !content.Contains(@"\\", StringComparison.Ordinal)))
			return [];

		var found = new List<string>();
		foreach (Match match in Quoted().Matches(content))
			Add(found, match.Groups[1].Value);

		foreach (Match match in Bare().Matches(content))
			Add(found, match.Value);

		return found;
	}

	private void Add(List<string> found, string raw)
	{
		if (found.Count >= Constants.Limits.MaxPathChipsPerEntry)
			return;

		var path = raw.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '\'', '"', '*').Trim();
		if (IsPathShaped(path) && !found.Contains(path, StringComparer.Ordinal))
			found.Add(path);
	}

	private static bool LeafHasExtension(string path)
	{
		var leaf = path[(path.LastIndexOf('\\') + 1)..];
		var dot = leaf.LastIndexOf('.');
		return dot > 0 && dot < leaf.Length - 1;
	}

	[GeneratedRegex(@"^(?:[A-Za-z]:\\|\\\\[^\\/\s]+\\)[^<>""|?*]+$")]
	private static partial Regex Shape();

	[GeneratedRegex(@"[`""']((?:[A-Za-z]:\\|\\\\[^\\/\s`""']+\\)[^`""'<>|?*\r\n]+)[`""']")]
	private static partial Regex Quoted();

	[GeneratedRegex(@"(?<![\w\\/])(?:[A-Za-z]:\\|\\\\[^\\/\s""'`<>|?*]+\\)[^\s""'`<>|?*]+")]
	private static partial Regex Bare();
}
