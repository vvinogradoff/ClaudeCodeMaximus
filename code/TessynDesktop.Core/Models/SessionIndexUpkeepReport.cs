using System;

namespace TessynDesktop.Models;

/// <summary>What one upkeep run of the session search index did (reindex, orphan purge, optimize, vacuum, checkpoint).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionIndexUpkeepReport
{
	/// <summary>The rowid map was built from an old-schema index during this run.</summary>
	public bool SchemaMigrated { get; set; }

	public TimeSpan MigrationDuration { get; set; }

	/// <summary>Sessions (re)indexed across all passes of the run.</summary>
	public int Indexed { get; set; }

	/// <summary>Files skipped because they were written to a moment ago; they are picked up by a later pass.</summary>
	public int Deferred { get; set; }

	/// <summary>Sessions removed because their JSONL no longer exists in any Claude home.</summary>
	public int Purged { get; set; }

	/// <summary>Sessions without a JSONL that were kept because a tree node still refers to them.</summary>
	public int KeptForTree { get; set; }

	/// <summary>The purge was skipped because it would have removed more than the allowed share of the index.</summary>
	public bool PurgeSkippedOverCap { get; set; }

	/// <summary>Orphans left alone because their stored JSONL home was not listed in this scan.</summary>
	public int PurgeSkippedUnlisted { get; set; }

	/// <summary>Configured homes that do not exist but still hold indexed sessions; their sessions are kept.</summary>
	public int MissingHomesWithRows { get; set; }

	public bool PurgeSkippedUnsafeScan { get; set; }

	/// <summary>The rowid map disagreed with the FTS table and was rebuilt.</summary>
	public bool MapRebuilt { get; set; }

	/// <summary>Sessions whose write failed in this run; the run carried on without them.</summary>
	public int Failed { get; set; }

	public bool Optimized { get; set; }

	public bool Vacuumed { get; set; }

	/// <summary>VACUUM was due but a search was in flight or the disk lacked room.</summary>
	public bool VacuumSkipped { get; set; }

	public bool Checkpointed { get; set; }
}
