using System.Globalization;
using System.Text.RegularExpressions;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>When a delayed send fires: <see cref="Kind"/> is <see cref="ScheduleKindDto.Delay"/> for a relative answer and <see cref="ScheduleKindDto.At"/> for a clock time.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ScheduleTime(ScheduleKindDto Kind, DateTimeOffset FireAtUtc);

/// <summary>
/// Reads the answer of the "Send later" dialog. A delay is a number with a unit (<c>30m</c>, <c>2h</c>, <c>1d</c>, <c>1h30m</c>; a bare number is minutes, as in the
/// Avalonia popup); a time is <c>HH:mm</c> (today, or tomorrow when it has passed) or <c>yyyy-MM-dd HH:mm</c>, in the local time zone. No WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static partial class ScheduleTimeParser
{
	private static readonly string[] DateTimeFormats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-ddTHH:mm"];

	[GeneratedRegex(@"^(?:\d+(?:[.,]\d+)?\s*[a-z]*\s*)+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex DelayShape();

	[GeneratedRegex(@"(?<n>\d+(?:[.,]\d+)?)\s*(?<u>[a-z]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex DelayPart();

	/// <summary>The time, or null with <paramref name="error"/> naming what is wrong.</summary>
	public static ScheduleTime? TryParse(string? text, DateTimeOffset now, TimeZoneInfo zone, out string? error)
	{
		error = null;
		var answer = text?.Trim() ?? string.Empty;
		if (answer.Length == 0)
		{
			error = "Enter a delay or a time.";
			return null;
		}

		var time = ParseDelay(answer, now) ?? ParseClock(answer, now, zone);
		if (time == null)
		{
			error = "Not understood. Use a delay like 30m, 2h, 1d, or a time like 14:30 or 2026-11-20 09:30.";
			return null;
		}

		if (time.FireAtUtc <= now)
		{
			error = "That time has already passed.";
			return null;
		}

		if (time.FireAtUtc - now > Constants.Schedules.MaxAhead)
		{
			error = "That is too far ahead (ten years at most).";
			return null;
		}

		return time;
	}

	private static ScheduleTime? ParseDelay(string answer, DateTimeOffset now)
	{
		if (!DelayShape().IsMatch(answer))
			return null;

		var parts = DelayPart().Matches(answer);
		var total = TimeSpan.Zero;
		foreach (Match part in parts)
		{
			if (!double.TryParse(part.Groups["n"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
				return null;

			var unit = part.Groups["u"].Value.ToLowerInvariant();

			// A bare number is only valid on its own: "1h30" would be ambiguous.
			if (unit.Length == 0 && parts.Count > 1)
				return null;

			switch (unit)
			{
				case "" or "m" or "min" or "mins" or "minute" or "minutes":
					total += TimeSpan.FromMinutes(amount);
					break;
				case "h" or "hr" or "hrs" or "hour" or "hours":
					total += TimeSpan.FromHours(amount);
					break;
				case "d" or "day" or "days":
					total += TimeSpan.FromDays(amount);
					break;
				default:
					return null;
			}
		}

		return total > TimeSpan.Zero ? new ScheduleTime(ScheduleKindDto.Delay, now + total) : null;
	}

	private static ScheduleTime? ParseClock(string answer, DateTimeOffset now, TimeZoneInfo zone)
	{
		var localNow = TimeZoneInfo.ConvertTime(now, zone);
		if (TimeSpan.TryParseExact(answer, ["h\\:mm", "hh\\:mm"], CultureInfo.InvariantCulture, out var clock) && clock < TimeSpan.FromDays(1))
		{
			var today = localNow.Date + clock;
			var target = today > localNow.DateTime ? today : today.AddDays(1);
			return new ScheduleTime(ScheduleKindDto.At, ToUtc(target, zone));
		}

		if (DateTime.TryParseExact(answer, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var absolute))
			return new ScheduleTime(ScheduleKindDto.At, ToUtc(absolute, zone));

		return null;
	}

	private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone)
	{
		var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
		return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToUniversalTime();
	}
}
