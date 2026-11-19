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
	private readonly ExternalOpenPolicy _names = new();

	/// <summary>True when <paramref name="path"/> has the shape of a local path with a file name, absolute or relative (FR.26.7); the same test the detector applies.</summary>
	public bool IsPathShaped(string? path)
	{
		if (string.IsNullOrEmpty(path) || path.Length > Constants.Limits.MaxPathChipLength || path.Any(c => _names.IsSpoofingCharacter(c)))
			return false;

		if (Shape().IsMatch(path))
			return LeafHasExtension(path);

		return IsRelativeShaped(path);
	}

	/// <summary>The local file paths in <paramref name="content"/> (absolute ones first, then relative ones), without duplicates.</summary>
	public IReadOnlyList<string> Detect(string content)
	{
		if (string.IsNullOrEmpty(content) || content.AsSpan().IndexOfAny('\\', '/', '`') < 0)
			return [];

		var found = new List<string>();
		foreach (Match match in Quoted().Matches(content))
			Add(found, match.Groups[1].Value);

		foreach (Match match in Bare().Matches(content))
			Add(found, match.Value);

		var relative = new List<(int At, string Text)>();
		var quoted = new List<(int Start, int End)>();
		foreach (Match match in QuotedRelative().Matches(content))
		{
			relative.Add((match.Index, match.Groups["p"].Value));
			quoted.Add((match.Index, match.Index + match.Length));
		}

		foreach (Match match in BacktickName().Matches(content))
			relative.Add((match.Index, match.Groups[1].Value));

		foreach (Match match in BareRelative().Matches(content))
		{
			if (!quoted.Any(q => match.Index >= q.Start && match.Index < q.End))   // the tail of a quoted path with spaces is not a path of its own
				relative.Add((match.Index, match.Value));
		}

		foreach (var (_, text) in relative.OrderBy(r => r.At))
			Add(found, text);

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

	/// <summary>A relative path (FR.26.7): a folder separator and a lettered extension on the last part, a first part that is no web host; or a bare lower-case file name.</summary>
	private static bool IsRelativeShaped(string path)
	{
		if (BareName().IsMatch(path))
			return true;

		if (!RelativeShape().IsMatch(path) || path[0] is '\\' or '/' || char.IsWhiteSpace(path[^1]))
			return false;

		var firstPart = path[..path.IndexOfAny(['\\', '/'])];
		var dot = firstPart.LastIndexOf('.');
		return dot < 0 || !Constants.FileTypes.WebTopLevelDomains.Contains(firstPart[(dot + 1)..]);
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

	/// <summary>A relative path: at least one separator, a last part with an extension that has a letter (so <c>1.2.3</c> is none), no characters Windows forbids in a name.</summary>
	[GeneratedRegex(@"^[^<>""|?*:\r\n]*[\\/][^<>""|?*:\r\n\\/]*\.[A-Za-z][A-Za-z0-9]{0,9}$")]
	private static partial Regex RelativeShape();

	/// <summary>A bare file name: dotted, lower-case extension of at most six characters (<c>README.md</c>); only found inside backticks.</summary>
	[GeneratedRegex(@"^[\w\-]+(?:\.[\w\-]+)*\.[a-z][a-z0-9]{0,5}$")]
	private static partial Regex BareName();

	/// <summary>A single dotted word alone in a pair of backticks.</summary>
	[GeneratedRegex(@"(?<!`)`([\w.\-]+)`(?!`)")]
	private static partial Regex BacktickName();

	/// <summary>A relative path with a separator inside a pair of backticks or quotes (spaces allowed there).</summary>
	[GeneratedRegex(@"(?<q>[`""'])(?<p>(?:(?!\k<q>)[^\r\n<>|?*:])*[\\/](?:(?!\k<q>)[^\r\n<>|?*:])*)\k<q>")]
	private static partial Regex QuotedRelative();

	/// <summary>A bare relative path starts where no word, path, URL or query character precedes it (so a URL, a link target inside a URL and the tail of an absolute path are never matched).</summary>
	[GeneratedRegex(@"(?<![\w\\/.:@%#=&~+?\-])[\w.\-@+~]+(?:[\\/][\w.\-@+~]+)+")]
	private static partial Regex BareRelative();
}
