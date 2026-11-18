using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Pure logic of the preview groups (FR.29.2, FR.29.3): parses and validates the pattern text of a group and finds the group of a file or a URL.
/// When several groups match, the most specific pattern (most characters other than <c>*</c>) wins and a tie goes to the group that comes first.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewGroupMatcher
{
	private static readonly char[] Separators = [' ', ',', '\t', '\r', '\n'];

	private readonly UrlPolicy _urlPolicy = new();

	/// <summary>Splits on whitespace and commas, skips empty tokens, and sorts the tokens into valid patterns and invalid ones.</summary>
	public PreviewPatternParseResult Parse(string? patterns)
	{
		var valid = new List<PreviewPattern>();
		var invalid = new List<string>();
		foreach (var token in (patterns ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries))
		{
			var pattern = ParseToken(token);
			if (pattern == null)
				invalid.Add(token);
			else
				valid.Add(pattern);
		}

		return new PreviewPatternParseResult(valid, invalid);
	}

	/// <summary>The group whose extension pattern matches the file's last extension; null for no group.</summary>
	public PreviewGroupMatch? MatchFile(IReadOnlyList<PreviewGroupModel> groups, string? fileNameOrPath)
	{
		var extension = ExtensionOf(fileNameOrPath);
		if (extension == null)
			return null;

		return Best(groups, PreviewPatternKind.Extension, pattern => string.Equals(pattern.Text, extension, StringComparison.Ordinal));
	}

	/// <summary>The group whose URL pattern matches an http or https URL; null for no group.</summary>
	public PreviewGroupMatch? MatchUrl(IReadOnlyList<PreviewGroupModel> groups, string? url)
	{
		if (!_urlPolicy.TryParse(url, out var uri))
			return null;

		var absolute = uri.AbsoluteUri;
		return Best(groups, PreviewPatternKind.Url, pattern => GlobMatches(pattern.Text, absolute));
	}

	private PreviewGroupMatch? Best(IReadOnlyList<PreviewGroupModel> groups, PreviewPatternKind kind, Func<PreviewPattern, bool> matches)
	{
		PreviewGroupMatch? best = null;
		foreach (var group in groups)
		foreach (var pattern in Parse(group.Patterns).Patterns)
		{
			if (pattern.Kind != kind || !matches(pattern))
				continue;

			// Strictly greater: a tie keeps the earlier group (and the earlier pattern of one group).
			if (best == null || pattern.Specificity > best.Pattern.Specificity)
				best = new PreviewGroupMatch(group, pattern);
		}

		return best;
	}

	private static PreviewPattern? ParseToken(string token)
	{
		if (token.StartsWith(Constants.Previews.HttpPrefix, StringComparison.OrdinalIgnoreCase) || token.StartsWith(Constants.Previews.HttpsPrefix, StringComparison.OrdinalIgnoreCase))
		{
			var afterScheme = token.IndexOf("//", StringComparison.Ordinal) + 2;
			if (afterScheme >= token.Length)
				return null;

			return new PreviewPattern(PreviewPatternKind.Url, token, token.Count(c => c != Constants.Previews.Wildcard));
		}

		var extension = token.StartsWith('.') ? token[1..] : token;
		if (extension.Length is < 1 or > Constants.Previews.MaxExtensionLength || !extension.All(char.IsAsciiLetterOrDigit))
			return null;

		return new PreviewPattern(PreviewPatternKind.Extension, extension.ToLowerInvariant(), extension.Length);
	}

	/// <summary>The last extension of a file name or path, lower case, without the dot; null when there is none (same name handling as <c>FileKindClassifier</c>).</summary>
	private static string? ExtensionOf(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return null;

		var file = name[(name.LastIndexOfAny(['\\', '/']) + 1)..].TrimEnd('.', ' ');
		var dot = file.LastIndexOf('.');
		if (dot < 0 || dot == file.Length - 1)
			return null;

		var extension = file[(dot + 1)..];
		return extension.All(char.IsAsciiLetterOrDigit) ? extension.ToLowerInvariant() : null;
	}

	/// <summary>Iterative two-pointer glob (<c>*</c> = any run of characters), ordinal ignoring case; it remembers only the last star, so it never blows up.</summary>
	private static bool GlobMatches(string pattern, string text)
	{
		int p = 0, t = 0, starP = -1, starT = 0;
		while (t < text.Length)
		{
			if (p < pattern.Length && pattern[p] == Constants.Previews.Wildcard)
			{
				starP = p++;
				starT = t;
			}
			else if (p < pattern.Length && char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t]))
			{
				p++;
				t++;
			}
			else if (starP >= 0)
			{
				p = starP + 1;
				t = ++starT;
			}
			else
				return false;
		}

		while (p < pattern.Length && pattern[p] == Constants.Previews.Wildcard)
			p++;

		return p == pattern.Length;
	}
}
