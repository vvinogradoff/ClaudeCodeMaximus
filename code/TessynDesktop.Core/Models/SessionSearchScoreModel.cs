namespace TessynDesktop.Models;

/// <remarks>Created by Claude</remarks>
public sealed class SessionSearchScoreModel
{
	public required string ExternalId { get; init; }

	/// <summary>Messages containing the query as an exact phrase.</summary>
	public int StrictMatchCount { get; init; }

	/// <summary>Messages containing all query words anywhere (&gt;= <see cref="StrictMatchCount"/>).</summary>
	public int LooseMatchCount { get; init; }
}
