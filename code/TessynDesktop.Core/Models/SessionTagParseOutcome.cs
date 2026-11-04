namespace TessynDesktop.Models;

/// <summary>What <c>SessionTagParser.Parse</c> made of one entry (FR.24.4).</summary>
/// <remarks>Created by Claude</remarks>
public enum SessionTagParseOutcome
{
	/// <summary>Blank entry: ignored, no result line.</summary>
	Empty,
	Valid,
	Invalid,

	/// <summary>A short Linear key whose team is not in the workspace map.</summary>
	UnknownTeam,
}
