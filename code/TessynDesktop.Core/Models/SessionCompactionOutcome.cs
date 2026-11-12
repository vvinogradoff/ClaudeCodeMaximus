namespace TessynDesktop.Models;

/// <summary>How a compaction run ended (FR.11.6).</summary>
/// <remarks>Created by Claude</remarks>
public enum SessionCompactionOutcome
{
	/// <summary>The session file was rewritten with the compacted conversation.</summary>
	Compacted,

	/// <summary>No USER/ASSISTANT entries after the last COMPACTION: nothing was sent and the file is untouched.</summary>
	NothingToCompact,

	/// <summary>The CLI reported an error, returned no content or the rewrite failed: the file is untouched.</summary>
	Failed,
}
