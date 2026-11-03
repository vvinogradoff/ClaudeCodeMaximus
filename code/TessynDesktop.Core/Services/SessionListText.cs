using System.Globalization;
using System.Text;

namespace TessynDesktop.Services;

/// <summary>
/// Shared text rules of the session listing (FR.15.13): control characters never break a line and
/// names are cut to <see cref="Constants.Agent.ListSessionsNameMaxLength"/> characters.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionListText
{
	private const string Ellipsis = "...";

	/// <summary>Turns control characters and Unicode line/paragraph separators into spaces; everything else is kept as is.</summary>
	public string Sanitize(string text)
	{
		var sb = new StringBuilder(text.Length);
		foreach (var c in text)
		{
			var category = char.GetUnicodeCategory(c);
			var isBreak  = char.IsControl(c)
				|| category == UnicodeCategory.LineSeparator
				|| category == UnicodeCategory.ParagraphSeparator;
			sb.Append(isBreak ? ' ' : c);
		}
		return sb.ToString();
	}

	/// <summary>
	/// <see cref="Sanitize"/>, then cut to the maximum name length ending in <c>...</c>; never splits a
	/// surrogate pair (the result may then be one character shorter).
	/// </summary>
	public string FormatName(string name)
	{
		var clean = Sanitize(name);
		if (clean.Length <= Constants.Agent.ListSessionsNameMaxLength)
			return clean;

		var keep = Constants.Agent.ListSessionsNameMaxLength - Ellipsis.Length;
		if (char.IsHighSurrogate(clean[keep - 1]))
			keep--;
		return clean.Substring(0, keep) + Ellipsis;
	}
}
