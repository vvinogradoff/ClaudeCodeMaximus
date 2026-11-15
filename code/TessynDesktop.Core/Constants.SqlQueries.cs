namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>SQL of the session search index (the queries touched by the upkeep work; search and title SQL stay inline for now).</summary>
	public static class SqlQueries
	{
		public const string TableExists = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name;";

		public const string CreateSchema = """
			CREATE TABLE IF NOT EXISTS sessions (
				external_id TEXT PRIMARY KEY,
				project_slug TEXT NOT NULL,
				jsonl_path TEXT NOT NULL,
				mtime_ticks INTEGER NOT NULL,
				title TEXT,
				title_generated_at_ticks INTEGER,
				first_user_prompt TEXT,
				message_count INTEGER NOT NULL,
				created_ticks INTEGER NOT NULL,
				last_used_ticks INTEGER NOT NULL
			);

			CREATE VIRTUAL TABLE IF NOT EXISTS messages USING fts5(
				content,
				role UNINDEXED,
				external_id UNINDEXED,
				project_slug UNINDEXED,
				message_index UNINDEXED,
				timestamp_ticks UNINDEXED
			);

			CREATE TABLE IF NOT EXISTS session_rows (
				external_id TEXT PRIMARY KEY,
				first_rowid INTEGER NOT NULL,
				last_rowid INTEGER NOT NULL,
				row_count INTEGER NOT NULL
			);
			""";

		public const string MarkAllSessionsStale = "UPDATE sessions SET mtime_ticks = 0;";

		/// <summary>One scan of the old FTS table builds the map; rows of a session were inserted by one transaction, so they are contiguous.</summary>
		public const string BuildRowMap = """
			INSERT OR REPLACE INTO session_rows (external_id, first_rowid, last_rowid, row_count)
			SELECT external_id, MIN(rowid), MAX(rowid), COUNT(*) FROM messages GROUP BY external_id;
			""";

		public const string SelectRowRange = "SELECT first_rowid, last_rowid, row_count FROM session_rows WHERE external_id = @externalId;";

		/// <summary>Rowid range lookup in the FTS content table; the external_id guard keeps a stale or overlapping range from touching other sessions.</summary>
		public const string DeleteMessagesByRange = "DELETE FROM messages WHERE rowid BETWEEN @first AND @last AND external_id = @externalId;";

		/// <summary>Full scan of the FTS table - only the fallback when a recorded range does not account for every row of the session.</summary>
		public const string DeleteMessagesByScan = "DELETE FROM messages WHERE external_id = @externalId;";

		public const string DeleteRowRange = "DELETE FROM session_rows WHERE external_id = @externalId;";

		public const string InsertMessage = """
			INSERT INTO messages (content, role, external_id, project_slug, message_index, timestamp_ticks)
			VALUES (@content, @role, @externalId, @projectSlug, @messageIndex, @timestampTicks);
			""";

		public const string UpsertRowRange = """
			INSERT INTO session_rows (external_id, first_rowid, last_rowid, row_count)
			VALUES (@externalId, @first, @last, @count)
			ON CONFLICT(external_id) DO UPDATE SET
				first_rowid = excluded.first_rowid,
				last_rowid = excluded.last_rowid,
				row_count = excluded.row_count;
			""";

		public const string UpsertSession = """
			INSERT INTO sessions (external_id, project_slug, jsonl_path, mtime_ticks, first_user_prompt, message_count, created_ticks, last_used_ticks)
			VALUES (@externalId, @projectSlug, @jsonlPath, @mtimeTicks, @firstUserPrompt, @messageCount, @createdTicks, @lastUsedTicks)
			ON CONFLICT(external_id) DO UPDATE SET
				project_slug = excluded.project_slug,
				jsonl_path = excluded.jsonl_path,
				mtime_ticks = excluded.mtime_ticks,
				first_user_prompt = excluded.first_user_prompt,
				message_count = excluded.message_count,
				created_ticks = excluded.created_ticks,
				last_used_ticks = excluded.last_used_ticks;
			""";

		/// <summary>Stored mtime, but only for sessions whose rows are mapped (a title-only row has no messages yet and must be indexed).</summary>
		public const string SelectIndexedMtime = """
			SELECT s.mtime_ticks FROM sessions s
			JOIN session_rows r ON r.external_id = s.external_id
			WHERE s.external_id = @externalId;
			""";

		/// <summary>Every external id the index knows: sessions rows plus mapped message rows.</summary>
		public const string SelectAllKnownIds = """
			SELECT external_id FROM sessions
			UNION
			SELECT external_id FROM session_rows;
			""";

		public const string DeleteSession = "DELETE FROM sessions WHERE external_id = @externalId;";

		public const string OptimizeFts = "INSERT INTO messages(messages) VALUES('optimize');";

		public const string FreelistCount = "PRAGMA freelist_count;";
		public const string PageCount = "PRAGMA page_count;";
		public const string PageSize = "PRAGMA page_size;";
		public const string Vacuum = "VACUUM;";
		public const string CheckpointTruncate = "PRAGMA wal_checkpoint(TRUNCATE);";
		public const string SelectUserVersion = "PRAGMA user_version;";
		public const string EnableWal = "PRAGMA journal_mode=WAL;";
	}
}
