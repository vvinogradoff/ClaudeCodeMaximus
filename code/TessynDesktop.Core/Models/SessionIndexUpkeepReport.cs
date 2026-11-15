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

	public bool PurgeSkippedUnsafeScan { get; set; }

	public bool Optimized { get; set; }

	public bool Vacuumed { get; set; }

	/// <summary>VACUUM was due but a search was in flight or the disk lacked room.</summary>
	public bool VacuumSkipped { get; set; }

	public bool Checkpointed { get; set; }
}
