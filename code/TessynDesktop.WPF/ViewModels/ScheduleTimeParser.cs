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
			// A very long run of digits parses to infinity (never fails); only NaN and non-positive amounts are not a delay.
			if (!double.TryParse(part.Groups["n"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || double.IsNaN(amount) || amount <= 0)
				return null;

			var unit = part.Groups["u"].Value.ToLowerInvariant();

			// A bare number is only valid on its own: "1h30" would be ambiguous.
			if (unit.Length == 0 && parts.Count > 1)
				return null;

			var unitMinutes = unit switch
			{
				"" or "m" or "min" or "mins" or "minute" or "minutes" => 1d,
				"h" or "hr" or "hrs" or "hour" or "hours" => 60d,
				"d" or "day" or "days" => 1440d,
				_ => 0d,
			};
			if (unitMinutes == 0)
				return null;

			// Too far ahead is judged before anything is converted: TimeSpan.FromDays(1e20) and now + a huge span throw. An amount (or a running total) beyond the
			// limit answers a time just past it, which TryParse then reports as "too far ahead".
			var minutes = amount * unitMinutes;
			if (!double.IsFinite(minutes) || minutes > Constants.Schedules.MaxAhead.TotalMinutes)
				return new ScheduleTime(ScheduleKindDto.Delay, now + Constants.Schedules.MaxAhead + TimeSpan.FromMinutes(1));

			total += TimeSpan.FromMinutes(minutes);
			if (total > Constants.Schedules.MaxAhead)
				return new ScheduleTime(ScheduleKindDto.Delay, now + Constants.Schedules.MaxAhead + TimeSpan.FromMinutes(1));
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

		// A wall-clock time inside the spring-forward gap does not exist: it is read an hour later (02:30 -> 03:30), as a calendar does. An ambiguous
		// time of the autumn hour (it happens twice) is read as its first occurrence. Everything else has the one offset of that moment.
		if (zone.IsInvalidTime(unspecified))
			unspecified = unspecified.AddHours(1);

		var offset = zone.IsAmbiguousTime(unspecified) ? zone.GetAmbiguousTimeOffsets(unspecified).Max() : zone.GetUtcOffset(unspecified);
		return new DateTimeOffset(unspecified, offset).ToUniversalTime();
	}
}
