using System.Globalization;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// How a session row presents time, as the Avalonia client does: the date of the last prompt (<c>yyyy-MM-dd HH:mm</c>, or the relative form by the Appearance setting)
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

		var local = time.ToLocalTime();
		if (format == SessionDateFormat.Absolute)
			return local.ToString(Constants.DateFormats.Absolute, CultureInfo.InvariantCulture);

		return local.Date == localNow.ToLocalTime().Date
			? local.ToString(Constants.DateFormats.TodayFormat, CultureInfo.InvariantCulture)
			: local.ToString(Constants.DateFormats.OtherDayFormat, CultureInfo.InvariantCulture);
	}

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
