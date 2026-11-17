using System.Globalization;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// How a session row presents time, as the Avalonia client does: the date of the last prompt (<c>yyyy-MM-dd</c>, or the relative age such as <c>107w</c>, by the Appearance setting)
/// and the recency tier that colours the row (<c>SessionNodeViewModel.RefreshRecencyBrush</c>: 15 minutes, 1 hour, 4 hours, 12 hours; a draft keeps the palest tier; the
/// dotted border marks the last 24 hours). Pure functions of a time and the clock; the format is one process-wide choice the Settings window changes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class SessionDisplay
{
	/// <summary>The date format of the rows (set from the Appearance settings at start and on every change).</summary>
	public static SessionDateFormat DateFormat { get; set; } = SessionDateFormat.Absolute;

	/// <summary>The date of the last prompt in the current format; empty without a prompt.</summary>
	public static string DateText(DateTimeOffset? at, DateTimeOffset localNow) => DateText(at, localNow, DateFormat);

	public static string DateText(DateTimeOffset? at, DateTimeOffset localNow, SessionDateFormat format)
	{
		if (at is not { } time)
			return string.Empty;

		return format == SessionDateFormat.Absolute
			? time.ToLocalTime().ToString(Constants.DateFormats.Absolute, CultureInfo.InvariantCulture)
			: Age(localNow - time);
	}

	/// <summary>The relative form, as an age: <c>now</c>, <c>5m</c>, <c>3h</c>, <c>9d</c>, then weeks from two weeks on, however many (<c>107w</c>).</summary>
	public static string Age(TimeSpan elapsed)
	{
		if (elapsed.TotalSeconds < 60)
			return Constants.DateFormats.NowText;
		if (elapsed.TotalMinutes < 60)
			return Number((long)elapsed.TotalMinutes) + Constants.DateFormats.MinutesUnit;
		if (elapsed.TotalHours < 24)
			return Number((long)elapsed.TotalHours) + Constants.DateFormats.HoursUnit;

		var days = (long)elapsed.TotalDays;
		return days < Constants.DateFormats.MaxDaysAsDays
			? Number(days) + Constants.DateFormats.DaysUnit
			: Number(days / Constants.DateFormats.DaysPerWeek) + Constants.DateFormats.WeeksUnit;
	}

	private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
	/// <summary>4 = activity within 15 minutes, 3 = within an hour, 2 = within 4 hours, 1 = within 12 hours or an unsent draft, 0 = none.</summary>
	public static int RecencyLevel(DateTimeOffset? lastActivity, bool hasDraft, DateTimeOffset now)
	{
		if (lastActivity is not { } at)
			return hasDraft ? 1 : 0;

		var minutes = (now - at).TotalMinutes;
		if (minutes <= Constants.SessionLists.RecencyTier1Minutes)
			return 4;
		if (minutes <= Constants.SessionLists.RecencyTier2Minutes)
			return 3;
		if (minutes <= Constants.SessionLists.RecencyTier3Minutes)
			return 2;
		if (minutes <= Constants.SessionLists.RecencyTier4Minutes)
			return 1;

		return hasDraft ? 1 : 0;
	}

	/// <summary>Activity within the last 24 hours (the dotted border).</summary>
	public static bool IsRecentlyActive(DateTimeOffset? lastActivity, DateTimeOffset now) =>
		lastActivity is { } at && (now - at).TotalHours <= Constants.SessionLists.ActiveWithinHours;
}
