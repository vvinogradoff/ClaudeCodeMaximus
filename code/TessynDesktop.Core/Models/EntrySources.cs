namespace TessynDesktop.Models;

/// <summary>
/// What produced a USER entry, as recorded in the session file header (<c>source="..."</c>) and exposed on the wire
/// (<c>SessionEntryDto.Source</c>). The desktop user is the default and is never recorded (null).
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class EntrySources
{
	public const string Scheduled    = "scheduled";
	public const string Orchestrated = "orchestrated";
	public const string Telegram     = "telegram";
	public const string Web          = "web";

	/// <summary>The header value for a turn source; null for <see cref="TurnSource.User"/>, which is the unmarked default.</summary>
	public static string? FromTurnSource(TurnSource source) => source switch
	{
		TurnSource.Scheduled    => Scheduled,
		TurnSource.Orchestrated => Orchestrated,
		TurnSource.Telegram     => Telegram,
		TurnSource.Web          => Web,
		_                       => null,
	};

	/// <summary>
	/// The source of a parsed entry. Only USER entries carry one; an unmarked USER entry whose text starts with the scheduled-turn
	/// prefix is a scheduled input written before the source was recorded.
	/// </summary>
	public static string? Resolve(string role, string? stored, string content)
	{
		if (role != Constants.SessionFile.RoleUser)
			return null;

		if (!string.IsNullOrEmpty(stored))
			return stored;

		return content.StartsWith(Constants.Agent.ScheduledTurnSystemPrefix, System.StringComparison.Ordinal) ? Scheduled : null;
	}
}
