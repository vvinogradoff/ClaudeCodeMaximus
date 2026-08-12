using System;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Determines whether a cached <see cref="ClaudeUsageData"/> snapshot's 5-hour or 7-day
/// window has already rolled over since it was fetched, and zeroes out whichever window(s)
/// are stale (FR.18.8). Staleness is judged against the reset timestamps the Anthropic API
/// already supplies with the data (<c>FiveHourResetsAt</c>/<c>SevenDayResetsAt</c>) rather than
/// a fixed elapsed-time threshold, since those timestamps are the authoritative point at which
/// the cached percentage stops describing the current window.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class ClaudeUsageStalenessService
{
	/// <summary>True once the current time is at or past the 5-hour window's reset instant.</summary>
	public static bool IsFiveHourStale(ClaudeUsageData data, DateTimeOffset now) =>
		now >= data.FiveHourResetsAt;

	/// <summary>True once the current time is at or past the 7-day window's reset instant.</summary>
	public static bool IsSevenDayStale(ClaudeUsageData data, DateTimeOffset now) =>
		now >= data.SevenDayResetsAt;

	/// <summary>
	/// Returns <paramref name="data"/> unchanged if neither window is stale, otherwise returns
	/// a copy with the stale window(s) zeroed (utilization 0, severity reset to "normal").
	/// The two windows are evaluated independently.
	/// </summary>
	public static ClaudeUsageData ZeroStaleWindows(ClaudeUsageData data, DateTimeOffset now)
	{
		var fiveHourStale = IsFiveHourStale(data, now);
		var sevenDayStale = IsSevenDayStale(data, now);

		if (!fiveHourStale && !sevenDayStale)
			return data;

		return data with
		{
			FiveHourUtilization = fiveHourStale ? 0 : data.FiveHourUtilization,
			FiveHourSeverity    = fiveHourStale ? Constants.Usage.NormalSeverity : data.FiveHourSeverity,
			SevenDayUtilization = sevenDayStale ? 0 : data.SevenDayUtilization,
			SevenDaySeverity    = sevenDayStale ? Constants.Usage.NormalSeverity : data.SevenDaySeverity,
		};
	}
}
