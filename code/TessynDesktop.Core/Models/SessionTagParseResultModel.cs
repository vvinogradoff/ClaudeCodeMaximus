namespace TessynDesktop.Models;

/// <summary>
/// Result of parsing one tag entry. <see cref="Tag"/> is set for <see cref="SessionTagParseOutcome.Valid"/>
/// (source and added time are left for the caller); the learned pair is set when the entry was a full Linear URL.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagParseResultModel
{
	public SessionTagParseOutcome Outcome { get; init; }
	public SessionTagModel? Tag { get; init; }

	/// <summary>The unknown team key (outcome UnknownTeam) with the full issue key, e.g. "ABC" and "ABC-123".</summary>
	public string? UnknownTeam { get; init; }
	public string? UnknownIssueKey { get; init; }

	/// <summary>Team key and workspace taught by a full Linear issue URL (FR.24.3); null otherwise.</summary>
	public string? LearnedTeam { get; init; }
	public string? LearnedWorkspace { get; init; }
}
