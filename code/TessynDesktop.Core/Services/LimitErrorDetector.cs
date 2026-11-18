using System;
using System.Text.RegularExpressions;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Decides whether a stream event ended a turn with a usage or limits error (FR.1.29). The structured signal is preferred: an assistant message the CLI stamped
/// <c>error: "rate_limit"</c> (or whose <c>quotaLimits.status</c> is <c>rejected</c>). A result or system error has no such stamp, so its text is matched against
/// <see cref="Constants.LimitErrors.TextPattern"/>. Assistant prose is never text-matched, so a session that talks about limits does not warn.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal static partial class LimitErrorDetector
{
	[GeneratedRegex(Constants.LimitErrors.TextPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex TextRegex();

	/// <summary>An assistant event carrying the CLI's structured rate-limit stamp.</summary>
	public static bool IsStructuredLimit(ClaudeStreamEvent evt) =>
		string.Equals(evt.ApiError, Constants.LimitErrors.RateLimitApiError, StringComparison.OrdinalIgnoreCase) || evt.QuotaRejected;

	/// <summary>A result or system error event: structured when it has the stamp, else by its text.</summary>
	public static bool IsLimit(ClaudeStreamEvent evt) =>
		IsStructuredLimit(evt) || (!string.IsNullOrWhiteSpace(evt.Content) && TextRegex().IsMatch(evt.Content));

	/// <summary>The text for a tooltip: one line, trimmed, at most <see cref="Constants.LimitErrors.TooltipMaxChars"/> characters.</summary>
	public static string Shorten(string text)
	{
		var line = text.Trim();
		var newline = line.IndexOfAny(['\r', '\n']);
		if (newline >= 0)
			line = line[..newline].TrimEnd();

		return line.Length <= Constants.LimitErrors.TooltipMaxChars ? line : line[..(Constants.LimitErrors.TooltipMaxChars - 1)].TrimEnd() + "…";
	}
}
