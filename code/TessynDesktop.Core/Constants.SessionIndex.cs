using System;

namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Upkeep policy of the cross-project session search index (FR.13.15, decision A5).</summary>
	public static class SessionIndex
	{
		/// <summary>
		/// Content scope of the index: bump when what <c>IndexSession</c> extracts widens. A stored version below this
		/// marks every session stale. History: 2 = tool_result content indexed.
		/// </summary>
		public const int ContentVersion = 2;

		/// <summary>
		/// Physical schema of the index (stored in <c>PRAGMA user_version</c>, same marker as the content version).
		/// 3 = the <c>session_rows</c> rowid map that makes delete-by-session an indexed lookup. Always at least <see cref="ContentVersion"/>.
		/// </summary>
		public const int SchemaVersion = 3;

		/// <summary>How long a connection waits for a competing writer or checkpoint before SQLITE_BUSY.</summary>
		public static readonly TimeSpan BusyTimeout = TimeSpan.FromSeconds(30);

		/// <summary>A JSONL written to more recently than this is considered live and is left for a later pass.</summary>
		public static readonly TimeSpan ActiveFileQuietPeriod = TimeSpan.FromSeconds(20);

		/// <summary>Sessions indexed back to back before the reindex yields.</summary>
		public const int ReindexBatchSize = 10;

		/// <summary>Pause between reindex batches so searches and other writers get the database.</summary>
		public static readonly TimeSpan ReindexYieldDelay = TimeSpan.FromMilliseconds(25);

		/// <summary>Extra passes the startup run makes to pick up files that were still being written.</summary>
		public const int DeferredPassLimit = 3;

		/// <summary>Sessions (re)indexed in one run at or above which the FTS5 <c>optimize</c> command runs.</summary>
		public const int OptimizeMinIndexedSessions = 200;

		/// <summary>Free pages must be at least this share of the file for a VACUUM to run.</summary>
		public const double VacuumMinFreeRatio = 0.20;

		/// <summary>Free space must also be at least this large (bytes); small files are not worth rewriting.</summary>
		public const long VacuumMinFreeBytes = 64L * 1024 * 1024;

		/// <summary>VACUUM rewrites the file through the WAL: require this multiple of the database size free on disk.</summary>
		public const double VacuumDiskSpaceFactor = 2.0;

		/// <summary>The purge refuses to remove more than this share of the indexed sessions in one run (a sanity cap against a missing home).</summary>
		public const double PurgeMaxShare = 0.5;

		public const string BackgroundThreadName = "SessionIndexUpkeep";
	}
}
