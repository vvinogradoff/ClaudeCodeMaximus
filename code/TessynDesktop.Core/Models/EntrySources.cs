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

	/// <summary>The source as recorded in the file: only USER entries carry one. This is what a rewrite of the file writes back.</summary>
	public static string? Stored(string role, string? stored)
		=> role == Constants.SessionFile.RoleUser && !string.IsNullOrEmpty(stored) ? stored : null;

	/// <summary>
	/// The source to show for an entry, resolved on read only: the recorded one, or for an unmarked USER entry whose text starts with the
	/// scheduled-turn prefix (a scheduled input written before the source was recorded) "scheduled". The legacy result is never written back.
	/// </summary>
	public static string? Resolve(string role, string? stored, string content)
	{
		var recorded = Stored(role, stored);
		if (recorded != null)
			return recorded;

		if (role != Constants.SessionFile.RoleUser)
			return null;

		return content.StartsWith(Constants.Agent.ScheduledTurnSystemPrefix, System.StringComparison.Ordinal) ? Scheduled : null;
	}
}
