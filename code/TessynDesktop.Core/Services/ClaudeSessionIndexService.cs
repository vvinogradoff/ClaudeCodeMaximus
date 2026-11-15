using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Microsoft.Data.Sqlite;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeSessionIndexService : IClaudeSessionIndexService
{
	private static readonly ILogger _log = Log.ForContext<ClaudeSessionIndexService>();

	private readonly IClaudeSessionImportService _importService;
	private readonly IClaudeAssistService _assistService;
	private readonly string _dbPath;
	private readonly IReadOnlyList<string> _claudeProjectsDirs;
	private readonly TimeSpan _activeFileQuietPeriod;
	private readonly SemaphoreSlim _writeGate = new(1, 1);

	// Searches in flight: VACUUM never starts while this is above zero.
	private readonly object _searchSync = new();
	private int _activeSearches;

	/// <summary>VACUUM thresholds; defaults from <see cref="Constants.SessionIndex"/>, adjustable so tests need not write 64 MB of free pages.</summary>
	internal long VacuumMinFreeBytes { get; set; } = Constants.SessionIndex.VacuumMinFreeBytes;
	internal double VacuumMinFreeRatio { get; set; } = Constants.SessionIndex.VacuumMinFreeRatio;

	// False until the rowid map is known to exist for the whole index (set under the write gate).
	private volatile bool _schemaMigrated;

	public ClaudeSessionIndexService(
		IClaudeSessionImportService importService,
		IClaudeAssistService assistService,
		string? dbPath = null,
		IReadOnlyList<string>? claudeProjectsDirs = null,
		TimeSpan? activeFileQuietPeriod = null)
	{
		_importService = importService;
		_assistService = assistService;
		_dbPath = dbPath ?? GetDefaultDbPath();
		_claudeProjectsDirs = claudeProjectsDirs ?? GetDefaultClaudeProjectsDirs();
		_activeFileQuietPeriod = activeFileQuietPeriod ?? Constants.SessionIndex.ActiveFileQuietPeriod;

		Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
		InitializeSchema();
	}

	private static string GetDefaultDbPath()
	{
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		return Path.Combine(appData, Constants.AppDataFolderName, "session-index.db");
	}

	/// <summary>
	/// Every Claude CLI home whose <c>projects/</c> folder may hold session JSONL for this app.
	/// See <see cref="Constants.ClaudeSessions.GetAllProjectsDirs"/> for the full rationale.
	/// </summary>
	private static IReadOnlyList<string> GetDefaultClaudeProjectsDirs()
		=> Constants.ClaudeSessions.GetAllProjectsDirs();

	private string ConnectionString => $"Data Source={_dbPath}";

	/// <summary>Opens a connection with an explicit busy timeout, so a competing writer or checkpoint waits instead of failing with SQLITE_BUSY.</summary>
	private SqliteConnection OpenConnection()
	{
		var conn = new SqliteConnection(ConnectionString);
		conn.Open();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = $"PRAGMA busy_timeout = {(int)Constants.SessionIndex.BusyTimeout.TotalMilliseconds};";
		cmd.ExecuteNonQuery();
		return conn;
	}

	/// <summary>
	/// Cheap, startup-safe schema work: creates the tables, handles the content version (marks every session
	/// stale when the extracted content widened) and notes whether the rowid map still has to be built.
	/// The map itself is built later, off the caller's thread, by <see cref="EnsureSchemaMigrated"/>.
	/// <c>PRAGMA user_version</c> is the single marker: below <see cref="Constants.SessionIndex.ContentVersion"/> means
	/// stale content, below <see cref="Constants.SessionIndex.SchemaVersion"/> means the rowid map is missing.
	/// </summary>
	private void InitializeSchema()
	{
		using var conn = OpenConnection();
		Exec(conn, Constants.SqlQueries.EnableWal);

		var existing = TableExists(conn, "sessions") || TableExists(conn, "messages");
		Exec(conn, Constants.SqlQueries.CreateSchema);

		if (!existing)
		{
			// Brand-new database: nothing to migrate, nothing stale.
			Exec(conn, $"PRAGMA user_version = {Constants.SessionIndex.SchemaVersion};");
			_schemaMigrated = true;
			return;
		}

		var storedVersion = ReadUserVersion(conn);
		if (storedVersion < Constants.SessionIndex.ContentVersion)
		{
			// mtime_ticks = 0 never matches a real file's last-write-time, so ReindexAsync's
			// incremental check treats every stored session as stale and rebuilds it.
			using var tx = conn.BeginTransaction();
			Exec(conn, Constants.SqlQueries.MarkAllSessionsStale, tx);
			Exec(conn, $"PRAGMA user_version = {Constants.SessionIndex.ContentVersion};", tx);
			tx.Commit();
			storedVersion = Constants.SessionIndex.ContentVersion;
			_log.Information("InitializeSchema: index content version -> {New}, all sessions marked stale for rebuild",
				Constants.SessionIndex.ContentVersion);
		}

		_schemaMigrated = storedVersion >= Constants.SessionIndex.SchemaVersion;
		if (!_schemaMigrated)
			_log.Information("InitializeSchema: index schema {Old} -> {New} pending (session rowid map is built on the first upkeep run)",
				storedVersion, Constants.SessionIndex.SchemaVersion);
	}

	/// <summary>
	/// Builds the session -> FTS rowid map for an index created before it existed. Metadata only: the FTS table is
	/// not touched, so search keeps returning the same results throughout. One transaction (map rows and the version
	/// bump commit together), so closing the app midway rolls it back and the next run starts it again.
	/// Returns the elapsed time, or null when nothing needed doing.
	/// </summary>
	private TimeSpan? EnsureSchemaMigrated(SqliteConnection conn, CancellationToken cancellationToken)
	{
		if (_schemaMigrated)
			return null;

		_writeGate.Wait(cancellationToken);
		try
		{
			if (_schemaMigrated)
				return null;
			if (ReadUserVersion(conn) >= Constants.SessionIndex.SchemaVersion)
			{
				_schemaMigrated = true;
				return null;
			}

			var started = System.Diagnostics.Stopwatch.StartNew();
			using var tx = conn.BeginTransaction();
			Exec(conn, Constants.SqlQueries.BuildRowMap, tx);
			Exec(conn, $"PRAGMA user_version = {Constants.SessionIndex.SchemaVersion};", tx);
			tx.Commit();
			_schemaMigrated = true;
			_log.Information("EnsureSchemaMigrated: session rowid map built in {Elapsed} ms", (long)started.Elapsed.TotalMilliseconds);
			return started.Elapsed;
		}
		finally { _writeGate.Release(); }
	}

	private static void Exec(SqliteConnection conn, string sql, SqliteTransaction? tx = null)
	{
		using var cmd = conn.CreateCommand();
		cmd.Transaction = tx;
		cmd.CommandText = sql;
		cmd.ExecuteNonQuery();
	}

	private static long ScalarLong(SqliteConnection conn, string sql)
	{
		using var cmd = conn.CreateCommand();
		cmd.CommandText = sql;
		return Convert.ToInt64(cmd.ExecuteScalar());
	}

	private static int ReadUserVersion(SqliteConnection conn) => (int)ScalarLong(conn, Constants.SqlQueries.SelectUserVersion);

	private static bool TableExists(SqliteConnection conn, string name)
	{
		using var cmd = conn.CreateCommand();
		cmd.CommandText = Constants.SqlQueries.TableExists;
		cmd.Parameters.AddWithValue("@name", name);
		return cmd.ExecuteScalar() != null;
	}

	// ── Reindex and upkeep ──────────────────────────────────────────────────────────

	public event EventHandler<SessionIndexedEventArgs>? SessionIndexed;

	/// <summary>
	/// One upkeep run: schema migration if due, a throttled reindex, orphan purge, then optimize / vacuum / checkpoint
	/// as the policy asks. Runs on a dedicated below-normal-priority thread. Returns the number of sessions (re)indexed.
	/// Files still being written are deferred and retried in the background; a failure is logged and rethrown.
	/// </summary>
	public Task<int> ReindexAsync(CancellationToken cancellationToken = default) =>
		RunUpkeepAsync(cancellationToken).ContinueWith(t => t.Result.Indexed,
			CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

	/// <summary>Same as <see cref="ReindexAsync"/> but reports everything the run did.</summary>
	internal Task<SessionIndexUpkeepReport> RunUpkeepAsync(CancellationToken cancellationToken = default)
	{
		var task = RunOnUpkeepThread(() => RunUpkeep(cancellationToken), cancellationToken);
		return task.ContinueWith(t =>
		{
			if (t.IsCompletedSuccessfully && t.Result.Deferred > 0)
				ScheduleDeferredPasses();
			return t;
		}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
	}

	private Task<T> RunOnUpkeepThread<T>(Func<T> work, CancellationToken cancellationToken)
	{
		var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		var thread = new Thread(() =>
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				tcs.SetResult(work());
			}
			catch (OperationCanceledException)
			{
				tcs.TrySetCanceled(cancellationToken);
			}
			catch (Exception ex)
			{
				_log.Error(ex, "Session index upkeep failed");
				tcs.TrySetException(ex);
			}
		})
		{
			IsBackground = true,
			Priority = ThreadPriority.BelowNormal,
			Name = Constants.SessionIndex.BackgroundThreadName,
		};
		thread.Start();
		return tcs.Task;
	}

	/// <summary>
	/// Files skipped as "being written right now" get further passes, each a quiet period later. Detached and
	/// observed: any failure is logged, nothing is left unobserved.
	/// </summary>
	private void ScheduleDeferredPasses()
	{
		_ = Task.Run(async () =>
		{
			try
			{
				for (var attempt = 0; attempt < Constants.SessionIndex.DeferredPassLimit; attempt++)
				{
					await Task.Delay(_activeFileQuietPeriod + _activeFileQuietPeriod).ConfigureAwait(false);
					var report = await RunOnUpkeepThread(() => RunUpkeep(CancellationToken.None), CancellationToken.None)
						.ConfigureAwait(false);
					if (report.Deferred == 0)
						return;
				}
			}
			catch (Exception ex)
			{
				_log.Error(ex, "Deferred session index pass failed");
			}
		});
	}

	private SessionIndexUpkeepReport RunUpkeep(CancellationToken cancellationToken)
	{
		var report = new SessionIndexUpkeepReport();
		using var conn = OpenConnection();

		var migration = EnsureSchemaMigrated(conn, cancellationToken);
		if (migration != null)
		{
			report.SchemaMigrated = true;
			report.MigrationDuration = migration.Value;
		}

		var scan = ReindexPass(conn, report, cancellationToken);
		_log.Information("ReindexAsync: (re)indexed {Count} sessions, {Deferred} deferred (being written)", report.Indexed, report.Deferred);

		PurgeOrphans(conn, scan, report, cancellationToken);
		RunMaintenance(conn, report, cancellationToken);

		if (report.Indexed > 0)
			SessionIndexed?.Invoke(this, new SessionIndexedEventArgs());
		return report;
	}

	/// <summary>The external ids found on disk and whether every configured projects folder that exists was listed in full.</summary>
	private sealed record DiskScan(HashSet<string> SeenIds, bool Complete);

	private DiskScan ReindexPass(SqliteConnection conn, SessionIndexUpkeepReport report, CancellationToken cancellationToken)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var complete = true;
		var anyDir = false;
		var sinceYield = 0;

		foreach (var claudeProjectsDir in _claudeProjectsDirs)
		{
			if (!Directory.Exists(claudeProjectsDir))
			{
				_log.Debug("ReindexAsync: no {Dir} found, skipping", claudeProjectsDir);
				continue;
			}

			anyDir = true;
			string[] slugDirs;
			try { slugDirs = Directory.GetDirectories(claudeProjectsDir); }
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_log.Warning(ex, "ReindexAsync: could not list {Dir}", claudeProjectsDir);
				complete = false;
				continue;
			}

			foreach (var slugDir in slugDirs)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var slug = Path.GetFileName(slugDir);

				string[] files;
				try { files = Directory.GetFiles(slugDir, "*" + Constants.ClaudeSessions.SessionFileExtension); }
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					_log.Warning(ex, "ReindexAsync: could not list {Dir}", slugDir);
					complete = false;
					continue;
				}

				foreach (var jsonlPath in files)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var externalId = Path.GetFileNameWithoutExtension(jsonlPath);
					if (string.IsNullOrEmpty(externalId))
						continue;
					seen.Add(externalId);

					DateTime lastWriteUtc;
					try { lastWriteUtc = File.GetLastWriteTimeUtc(jsonlPath); }
					catch (IOException) { continue; } // file removed mid-scan
					var mtimeTicks = lastWriteUtc.Ticks;

					if (GetIndexedMtime(conn, externalId) == mtimeTicks)
						continue; // unchanged since last index

					if (DateTime.UtcNow - lastWriteUtc < _activeFileQuietPeriod)
					{
						report.Deferred++; // a live session: indexing it now would be redone on the next write
						continue;
					}

					var prepared = PrepareSession(externalId, slug, jsonlPath, mtimeTicks);
					if (prepared == null)
						continue;

					_writeGate.Wait(cancellationToken);
					try { WriteSession(conn, prepared); }
					finally { _writeGate.Release(); }
					report.Indexed++;

					if (++sinceYield >= Constants.SessionIndex.ReindexBatchSize)
					{
						sinceYield = 0;
						// Searches and other writers get the database between batches.
						cancellationToken.WaitHandle.WaitOne(Constants.SessionIndex.ReindexYieldDelay);
					}
				}
			}
		}

		return new DiskScan(seen, complete && anyDir);
	}

	/// <summary>
	/// Removes sessions whose JSONL no longer exists in any Claude home. Only after a complete scan that found
	/// at least one file: an unreadable or missing projects folder must never empty the index (it also holds the title cache).
	/// </summary>
	private void PurgeOrphans(SqliteConnection conn, DiskScan scan, SessionIndexUpkeepReport report, CancellationToken cancellationToken)
	{
		if (!scan.Complete)
			return;

		var known = new List<string>();
		using (var cmd = conn.CreateCommand())
		{
			cmd.CommandText = Constants.SqlQueries.SelectAllKnownIds;
			using var reader = cmd.ExecuteReader();
			while (reader.Read())
				known.Add(reader.GetString(0));
		}

		var orphans = known.Where(id => !scan.SeenIds.Contains(id)).ToList();
		if (orphans.Count == 0)
			return;

		if (scan.SeenIds.Count == 0)
		{
			report.PurgeSkippedUnsafeScan = true;
			_log.Warning("PurgeOrphans: scan saw no JSONL at all but the index holds {Count} sessions; purge skipped", orphans.Count);
			return;
		}

		foreach (var chunk in orphans.Chunk(Constants.SessionIndex.ReindexBatchSize))
		{
			cancellationToken.ThrowIfCancellationRequested();
			_writeGate.Wait(cancellationToken);
			try
			{
				using var tx = conn.BeginTransaction();
				foreach (var id in chunk)
				{
					DeleteSessionRows(conn, tx, id);
					using var del = conn.CreateCommand();
					del.Transaction = tx;
					del.CommandText = Constants.SqlQueries.DeleteSession;
					del.Parameters.AddWithValue("@externalId", id);
					del.ExecuteNonQuery();
				}
				tx.Commit();
			}
			finally { _writeGate.Release(); }
			cancellationToken.WaitHandle.WaitOne(Constants.SessionIndex.ReindexYieldDelay);
		}

		report.Purged = orphans.Count;
		_log.Information("PurgeOrphans: removed {Count} sessions whose JSONL no longer exists", orphans.Count);
	}

	/// <summary>Optimize after a large reindex or purge, VACUUM when enough pages are free, then truncate the WAL.</summary>
	private void RunMaintenance(SqliteConnection conn, SessionIndexUpkeepReport report, CancellationToken cancellationToken)
	{
		var worked = report.Indexed > 0 || report.Purged > 0 || report.SchemaMigrated;

		if (report.Indexed + report.Purged >= Constants.SessionIndex.OptimizeMinIndexedSessions)
		{
			_writeGate.Wait(cancellationToken);
			try
			{
				var started = System.Diagnostics.Stopwatch.StartNew();
				Exec(conn, Constants.SqlQueries.OptimizeFts);
				report.Optimized = true;
				_log.Information("RunMaintenance: FTS5 optimize took {Elapsed} ms", (long)started.Elapsed.TotalMilliseconds);
			}
			finally { _writeGate.Release(); }
		}

		if (VacuumIsDue(conn, out var freeBytes))
		{
			_writeGate.Wait(cancellationToken);
			try { TryVacuum(conn, freeBytes, report); }
			finally { _writeGate.Release(); }
		}

		if (worked || report.Vacuumed)
		{
			try
			{
				Exec(conn, Constants.SqlQueries.CheckpointTruncate);
				report.Checkpointed = true;
			}
			catch (SqliteException ex)
			{
				_log.Warning(ex, "RunMaintenance: WAL checkpoint(TRUNCATE) did not complete");
			}
		}
	}

	/// <summary>Free pages at or above both the ratio and the absolute size threshold.</summary>
	private bool VacuumIsDue(SqliteConnection conn, out long freeBytes)
	{
		var pageSize = ScalarLong(conn, Constants.SqlQueries.PageSize);
		var pages = ScalarLong(conn, Constants.SqlQueries.PageCount);
		var free = ScalarLong(conn, Constants.SqlQueries.FreelistCount);
		freeBytes = free * pageSize;
		return pages > 0
			&& freeBytes >= VacuumMinFreeBytes
			&& (double)free / pages >= VacuumMinFreeRatio;
	}

	/// <summary>
	/// VACUUM runs only on the upkeep thread, never while a search is in flight and only with disk room for the
	/// rewrite (WAL mode writes the new file through the log). A skipped vacuum is retried by the next run.
	/// </summary>
	private void TryVacuum(SqliteConnection conn, long freeBytes, SessionIndexUpkeepReport report)
	{
		lock (_searchSync)
		{
			if (_activeSearches > 0)
			{
				report.VacuumSkipped = true;
				_log.Information("RunMaintenance: VACUUM due ({Free} MB free) but a search is in flight; deferred", freeBytes / (1024 * 1024));
				return;
			}
		}

		try
		{
			var dbSize = new FileInfo(_dbPath).Length;
			var root = Path.GetPathRoot(Path.GetFullPath(_dbPath));
			if (root != null && new DriveInfo(root).AvailableFreeSpace < dbSize * Constants.SessionIndex.VacuumDiskSpaceFactor)
			{
				report.VacuumSkipped = true;
				_log.Warning("RunMaintenance: VACUUM due but the disk has less than {Factor}x the database size free; skipped",
					Constants.SessionIndex.VacuumDiskSpaceFactor);
				return;
			}
		}
		catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
		{
			_log.Debug(ex, "RunMaintenance: could not read free disk space; vacuuming anyway");
		}

		var started = System.Diagnostics.Stopwatch.StartNew();
		var before = new FileInfo(_dbPath).Length;
		Exec(conn, Constants.SqlQueries.Vacuum);
		report.Vacuumed = true;
		_log.Information("RunMaintenance: VACUUM reclaimed {Free} MB (file was {Size} MB) in {Elapsed} ms",
			freeBytes / (1024 * 1024), before / (1024 * 1024), (long)started.Elapsed.TotalMilliseconds);
	}

	/// <summary>Marks a search as in flight for the lifetime of the scope (see <see cref="TryVacuum"/>).</summary>
	internal readonly struct SearchScope : IDisposable
	{
		private readonly ClaudeSessionIndexService _owner;

		public SearchScope(ClaudeSessionIndexService owner)
		{
			_owner = owner;
			lock (owner._searchSync) owner._activeSearches++;
		}

		public void Dispose()
		{
			lock (_owner._searchSync) _owner._activeSearches--;
		}
	}

	/// <summary>
	/// Re-indexes a single session from its JSONL file, if the file changed since it was last indexed.
	/// Returns false when the file is missing, unchanged, or could not be indexed.
	/// </summary>
	public Task<bool> IndexSessionAsync(string externalId, string workingDirectory, CancellationToken cancellationToken = default) =>
		Task.Run(() => IndexSessionCore(externalId, workingDirectory, cancellationToken), cancellationToken);

	private bool IndexSessionCore(string externalId, string workingDirectory, CancellationToken cancellationToken)
	{
		var slug = Constants.ClaudeSessions.BuildProjectSlug(workingDirectory);
		var jsonlPath = FindSessionJsonl(slug, externalId);
		if (jsonlPath == null)
			return false;

		long mtimeTicks;
		try { mtimeTicks = File.GetLastWriteTimeUtc(jsonlPath).Ticks; }
		catch (IOException) { return false; }

		using var conn = OpenConnection();
		EnsureSchemaMigrated(conn, cancellationToken);
		if (GetIndexedMtime(conn, externalId) == mtimeTicks)
			return false;

		var prepared = PrepareSession(externalId, slug, jsonlPath, mtimeTicks);
		if (prepared == null)
			return false;

		_writeGate.Wait(cancellationToken);
		try { WriteSession(conn, prepared); }
		finally { _writeGate.Release(); }

		SessionIndexed?.Invoke(this, new SessionIndexedEventArgs { ExternalId = externalId });
		return true;
	}

	private string? FindSessionJsonl(string slug, string externalId)
	{
		var fileName = externalId + Constants.ClaudeSessions.SessionFileExtension;
		foreach (var dir in _claudeProjectsDirs)
		{
			var candidate = Path.Combine(dir, slug, fileName);
			if (File.Exists(candidate))
				return candidate;
		}
		return null;
	}

	/// <summary>A parsed JSONL, ready to be written. Parsing happens outside the write gate; only the write holds it.</summary>
	private sealed record PreparedSession(
		string ExternalId, string ProjectSlug, string JsonlPath, long MtimeTicks,
		IReadOnlyList<SessionEntryModel> Entries, ClaudeSessionSummaryModel Summary);

	private PreparedSession? PrepareSession(string externalId, string projectSlug, string jsonlPath, long mtimeTicks)
	{
		try
		{
			var entries = _importService.ParseJsonlSessionForIndexing(jsonlPath);
			var summary = _importService.ExtractSummary(externalId, jsonlPath);
			if (summary == null || summary.MessageCount == 0)
				return null;
			return new PreparedSession(externalId, projectSlug, jsonlPath, mtimeTicks, entries, summary);
		}
		catch (Exception ex)
		{
			_log.Debug("IndexSession: failed to parse {Path}: {Error}", jsonlPath, ex.Message);
			return null;
		}
	}

	/// <summary>Replaces one session's rows. The caller holds the write gate and the rowid map exists.</summary>
	private void WriteSession(SqliteConnection conn, PreparedSession session)
	{
		using var tx = conn.BeginTransaction();

		DeleteSessionRows(conn, tx, session.ExternalId);

		long firstRowId = 0, lastRowId = 0;
		var messageIndex = 0;
		using (var insert = conn.CreateCommand())
		{
			insert.Transaction = tx;
			insert.CommandText = Constants.SqlQueries.InsertMessage;
			var content = insert.Parameters.Add("@content", SqliteType.Text);
			var role = insert.Parameters.Add("@role", SqliteType.Text);
			var externalId = insert.Parameters.Add("@externalId", SqliteType.Text);
			var projectSlug = insert.Parameters.Add("@projectSlug", SqliteType.Text);
			var index = insert.Parameters.Add("@messageIndex", SqliteType.Integer);
			var timestamp = insert.Parameters.Add("@timestampTicks", SqliteType.Integer);
			externalId.Value = session.ExternalId;
			projectSlug.Value = session.ProjectSlug;

			foreach (var entry in session.Entries)
			{
				if (string.IsNullOrWhiteSpace(entry.Content))
					continue;

				// Every message is indexed in full: tool output is deliberately never capped or truncated (decision A5).
				content.Value = entry.Content;
				role.Value = entry.Role;
				index.Value = messageIndex;
				timestamp.Value = entry.Timestamp.UtcTicks;
				insert.ExecuteNonQuery();
				if (messageIndex == 0)
					firstRowId = ScalarLongIn(conn, tx, "SELECT last_insert_rowid();");
				messageIndex++;
			}
			if (messageIndex > 0)
				lastRowId = ScalarLongIn(conn, tx, "SELECT last_insert_rowid();");
		}

		using (var map = conn.CreateCommand())
		{
			map.Transaction = tx;
			map.CommandText = Constants.SqlQueries.UpsertRowRange;
			map.Parameters.AddWithValue("@externalId", session.ExternalId);
			map.Parameters.AddWithValue("@first", firstRowId);
			map.Parameters.AddWithValue("@last", lastRowId);
			map.Parameters.AddWithValue("@count", messageIndex);
			map.ExecuteNonQuery();
		}

		using (var upsert = conn.CreateCommand())
		{
			upsert.Transaction = tx;
			upsert.CommandText = Constants.SqlQueries.UpsertSession;
			upsert.Parameters.AddWithValue("@externalId", session.ExternalId);
			upsert.Parameters.AddWithValue("@projectSlug", session.ProjectSlug);
			upsert.Parameters.AddWithValue("@jsonlPath", session.JsonlPath);
			upsert.Parameters.AddWithValue("@mtimeTicks", session.MtimeTicks);
			upsert.Parameters.AddWithValue("@firstUserPrompt", (object?)session.Summary.FirstUserPrompt ?? DBNull.Value);
			upsert.Parameters.AddWithValue("@messageCount", session.Summary.MessageCount);
			upsert.Parameters.AddWithValue("@createdTicks", session.Summary.Created.UtcTicks);
			upsert.Parameters.AddWithValue("@lastUsedTicks", session.Summary.LastUsed.UtcTicks);
			upsert.ExecuteNonQuery();
		}

		tx.Commit();
	}

	private static long ScalarLongIn(SqliteConnection conn, SqliteTransaction tx, string sql)
	{
		using var cmd = conn.CreateCommand();
		cmd.Transaction = tx;
		cmd.CommandText = sql;
		return Convert.ToInt64(cmd.ExecuteScalar());
	}

	/// <summary>
	/// Deletes a session's FTS rows through the rowid map (an index lookup, then a rowid-range delete) instead of
	/// scanning the whole FTS table. When the range does not account for the recorded row count the map is not
	/// trustworthy for this session and a scan delete cleans up whatever is left.
	/// </summary>
	private void DeleteSessionRows(SqliteConnection conn, SqliteTransaction tx, string externalId)
	{
		long first, last, expected;
		using (var select = conn.CreateCommand())
		{
			select.Transaction = tx;
			select.CommandText = Constants.SqlQueries.SelectRowRange;
			select.Parameters.AddWithValue("@externalId", externalId);
			using var reader = select.ExecuteReader();
			if (!reader.Read())
				return; // never indexed with rows: nothing to delete
			first = reader.GetInt64(0);
			last = reader.GetInt64(1);
			expected = reader.GetInt64(2);
		}

		int removed = 0;
		if (expected > 0)
		{
			using var del = conn.CreateCommand();
			del.Transaction = tx;
			del.CommandText = Constants.SqlQueries.DeleteMessagesByRange;
			del.Parameters.AddWithValue("@first", first);
			del.Parameters.AddWithValue("@last", last);
			del.Parameters.AddWithValue("@externalId", externalId);
			removed = del.ExecuteNonQuery();
		}

		if (removed != expected)
		{
			_log.Warning("DeleteSessionRows: {Id} removed {Removed} of {Expected} mapped rows; falling back to a scan delete", externalId, removed, expected);
			using var scan = conn.CreateCommand();
			scan.Transaction = tx;
			scan.CommandText = Constants.SqlQueries.DeleteMessagesByScan;
			scan.Parameters.AddWithValue("@externalId", externalId);
			scan.ExecuteNonQuery();
		}

		using var drop = conn.CreateCommand();
		drop.Transaction = tx;
		drop.CommandText = Constants.SqlQueries.DeleteRowRange;
		drop.Parameters.AddWithValue("@externalId", externalId);
		drop.ExecuteNonQuery();
	}

	/// <summary>The stored mtime of an indexed session, or null if it is unknown or has no mapped rows yet.</summary>
	private static long? GetIndexedMtime(SqliteConnection conn, string externalId)
	{
		using var cmd = conn.CreateCommand();
		cmd.CommandText = Constants.SqlQueries.SelectIndexedMtime;
		cmd.Parameters.AddWithValue("@externalId", externalId);
		var result = cmd.ExecuteScalar();
		return result is long l ? l : null;
	}

	// ── Search ────────────────────────────────────────────────────────────────────

	public Task<IReadOnlyList<SessionSearchResultModel>> SearchAsync(
		string query,
		string? projectSlug = null,
		string? role = null,
		int? limit = null,
		int? offset = null,
		CancellationToken cancellationToken = default) =>
		Task.Run(() => SearchCore(query, projectSlug, role, limit, offset), cancellationToken);

	/// <summary>
	/// Returns the distinct set of external IDs with at least one matching message — unranked and
	/// uncapped. For membership tests (e.g. "which tree nodes should stay visible for this query")
	/// rather than a ranked top-N results list: applying <see cref="SearchAsync"/>'s default rank
	/// cutoff here would silently drop genuine matches in the caller's project whenever unrelated
	/// projects happen to score higher on the same terms. Optionally scoped to a single project slug.
	/// </summary>
	public Task<IReadOnlySet<string>> SearchMatchingSessionIdsAsync(
		string query, string? projectSlug = null, CancellationToken cancellationToken = default) =>
		Task.Run(() => SearchMatchingSessionIdsCore(query, projectSlug), cancellationToken);

	private IReadOnlySet<string> SearchMatchingSessionIdsCore(string query, string? projectSlug)
	{
		using var searchScope = new SearchScope(this);
		var ids = new HashSet<string>();
		var words = SplitQueryWords(query);
		if (words.Count == 0)
			return ids;

		using var conn = OpenConnection();

		using var cmd = conn.CreateCommand();
		cmd.CommandText = """
			SELECT DISTINCT external_id FROM messages
			WHERE messages MATCH @query
			  AND (@projectSlug IS NULL OR project_slug = @projectSlug);
			""";
		cmd.Parameters.AddWithValue("@query", BuildLooseQuery(words));
		cmd.Parameters.AddWithValue("@projectSlug", (object?)projectSlug ?? DBNull.Value);

		try
		{
			using var reader = cmd.ExecuteReader();
			while (reader.Read())
				ids.Add(reader.GetString(0));
		}
		catch (SqliteException)
		{
			// A degenerate query is rejected by FTS5 — treat it as "no matches", never throw.
		}
		return ids;
	}

	/// <summary>
	/// Per-session match counts for a query — strict (exact phrase) and loose (all words anywhere) —
	/// unranked and uncapped. Used to order the tree by relevance.
	/// </summary>
	public Task<IReadOnlyList<SessionSearchScoreModel>> SearchSessionScoresAsync(
		string query, string? projectSlug = null, CancellationToken cancellationToken = default) =>
		Task.Run(() => SearchSessionScoresCore(query, projectSlug), cancellationToken);

	private IReadOnlyList<SessionSearchScoreModel> SearchSessionScoresCore(string query, string? projectSlug)
	{
		using var searchScope = new SearchScope(this);
		var words = SplitQueryWords(query);
		if (words.Count == 0)
			return [];

		using var conn = OpenConnection();

		var loose = CountMatchesBySession(conn, BuildLooseQuery(words), projectSlug);
		if (loose.Count == 0)
			return [];

		var strict = CountMatchesBySession(conn, BuildStrictQuery(words), projectSlug);
		return loose
			.Select(kv => new SessionSearchScoreModel
			{
				ExternalId       = kv.Key,
				LooseMatchCount  = kv.Value,
				StrictMatchCount = strict.TryGetValue(kv.Key, out var count) ? count : 0,
			})
			.ToList();
	}

	private static Dictionary<string, int> CountMatchesBySession(SqliteConnection conn, string ftsQuery, string? projectSlug)
	{
		var counts = new Dictionary<string, int>();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = """
			SELECT external_id, COUNT(*) FROM messages
			WHERE messages MATCH @query
			  AND (@projectSlug IS NULL OR project_slug = @projectSlug)
			GROUP BY external_id;
			""";
		cmd.Parameters.AddWithValue("@query", ftsQuery);
		cmd.Parameters.AddWithValue("@projectSlug", (object?)projectSlug ?? DBNull.Value);

		try
		{
			using var reader = cmd.ExecuteReader();
			while (reader.Read())
				counts[reader.GetString(0)] = reader.GetInt32(1);
		}
		catch (SqliteException)
		{
			// A degenerate query is rejected by FTS5 — treat it as "no matches", never throw.
		}
		return counts;
	}

	private IReadOnlyList<SessionSearchResultModel> SearchCore(
		string query, string? projectSlug, string? role, int? limit, int? offset)
	{
		using var searchScope = new SearchScope(this);
		var words = SplitQueryWords(query);
		if (words.Count == 0)
			return [];

		using var conn = OpenConnection();

		// Prefer exact-phrase matches (adjacent tokens, e.g. "visual studio" as a phrase) — plain
		// bm25 over an implicit AND-of-terms ranks a document where the terms merely co-occur
		// anywhere the same as a genuine phrase hit, which is a major source of irrelevant results.
		// Only fall back to the looser AND-of-terms match when the phrase finds nothing.
		var phraseResults = RunSearchQuery(conn, BuildStrictQuery(words), projectSlug, role, limit, offset);
		if (phraseResults.Count > 0)
			return phraseResults;

		return RunSearchQuery(conn, BuildLooseQuery(words), projectSlug, role, limit, offset);
	}

	private static List<SessionSearchResultModel> RunSearchQuery(
		SqliteConnection conn, string ftsQuery, string? projectSlug, string? role, int? limit, int? offset)
	{
		using var cmd = conn.CreateCommand();
		cmd.CommandText = """
			SELECT m.external_id, m.project_slug, m.message_index, m.content, m.role, m.timestamp_ticks,
			       s.title, bm25(messages) AS rank
			FROM messages m
			LEFT JOIN sessions s ON s.external_id = m.external_id
			WHERE messages MATCH @query
			  AND (@projectSlug IS NULL OR m.project_slug = @projectSlug)
			  AND (@role IS NULL OR m.role = @role)
			ORDER BY rank
			LIMIT @limit OFFSET @offset;
			""";
		cmd.Parameters.AddWithValue("@query", ftsQuery);
		cmd.Parameters.AddWithValue("@projectSlug", (object?)projectSlug ?? DBNull.Value);
		cmd.Parameters.AddWithValue("@role", (object?)role ?? DBNull.Value);
		cmd.Parameters.AddWithValue("@limit", limit ?? 50);
		cmd.Parameters.AddWithValue("@offset", offset ?? 0);

		var results = new List<SessionSearchResultModel>();
		try
		{
			using var reader = cmd.ExecuteReader();
			while (reader.Read())
			{
				results.Add(new SessionSearchResultModel
				{
					ExternalId   = reader.GetString(0),
					ProjectSlug  = reader.GetString(1),
					MessageIndex = reader.GetInt32(2),
					Content      = reader.GetString(3),
					Role         = reader.GetString(4),
					Timestamp    = new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero),
					SessionTitle = reader.IsDBNull(6) ? null : reader.GetString(6),
					Rank         = reader.GetDouble(7),
				});
			}
		}
		catch (SqliteException)
		{
			// A degenerate phrase query (e.g. punctuation-only input the tokenizer reduces to
			// nothing) can be rejected as a syntax error by FTS5 — treat it as "no phrase matches"
			// and let the caller fall back to the AND-of-terms query instead of throwing.
		}
		return results;
	}

	/// <summary>
	/// Splits free-typed text into words and drops those with no letter or digit (they tokenize to
	/// nothing). Words are never passed to FTS5 as syntax — see <see cref="QuoteFtsString"/>.
	/// </summary>
	private static List<string> SplitQueryWords(string query) =>
		query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
			.Where(word => word.Any(char.IsLetterOrDigit))
			.ToList();

	/// <summary>Implicit AND: every word as its own quoted FTS5 string.</summary>
	private static string BuildLooseQuery(IReadOnlyList<string> words) =>
		string.Join(' ', words.Select(QuoteFtsString));

	/// <summary>Exact phrase: all words as ONE quoted FTS5 string.</summary>
	private static string BuildStrictQuery(IReadOnlyList<string> words) =>
		QuoteFtsString(string.Join(' ', words));

	private static string QuoteFtsString(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";

	// ── Titles ────────────────────────────────────────────────────────────────────

	public async Task<IReadOnlyDictionary<string, string>> EnsureTitlesAsync(
		IReadOnlyList<ClaudeSessionSummaryModel> summaries,
		Action<IReadOnlyDictionary<string, string>>? onBatchComplete = null,
		CancellationToken cancellationToken = default)
	{
		var result = new Dictionary<string, string>();
		var missing = new List<ClaudeSessionSummaryModel>();

		using (var conn = OpenConnection())
		{
			foreach (var summary in summaries)
			{
				var cached = GetStoredTitle(conn, summary.SessionId);
				if (cached != null)
					result[summary.SessionId] = cached;
				else
					missing.Add(summary);
			}
		}

		if (missing.Count == 0)
			return result;

		// Persist each batch as it lands (so a cancelled/interrupted run keeps partial progress),
		// and forward progress to the caller.
		var persisted = new HashSet<string>();
		void OnAssistBatchComplete(Dictionary<string, string> titlesSoFar)
		{
			var newOnes = titlesSoFar.Where(kv => !persisted.Contains(kv.Key)).ToList();
			if (newOnes.Count > 0)
			{
				PersistTitles(missing, newOnes.ToDictionary(kv => kv.Key, kv => kv.Value));
				foreach (var kv in newOnes)
					persisted.Add(kv.Key);
			}
			onBatchComplete?.Invoke(titlesSoFar);
		}

		var generated = await _assistService.GenerateTitlesAsync(missing, OnAssistBatchComplete, cancellationToken)
			.ConfigureAwait(false);

		var finalNew = generated.Where(kv => !persisted.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
		if (finalNew.Count > 0)
			PersistTitles(missing, finalNew);

		foreach (var (sessionId, title) in generated)
			result[sessionId] = title;

		return result;
	}

	// ── Session summary lookup ───────────────────────────────────────────────────────

	public Task<IReadOnlyList<ClaudeSessionSummaryModel>> GetSessionSummariesAsync(
		IReadOnlyList<string> externalIds, CancellationToken cancellationToken = default) =>
		Task.Run(() => GetSessionSummariesCore(externalIds), cancellationToken);

	private IReadOnlyList<ClaudeSessionSummaryModel> GetSessionSummariesCore(IReadOnlyList<string> externalIds)
	{
		if (externalIds.Count == 0)
			return [];

		using var conn = OpenConnection();

		using var cmd = conn.CreateCommand();
		var paramNames = externalIds.Select((_, i) => $"@id{i}").ToList();
		cmd.CommandText = $"""
			SELECT external_id, project_slug, jsonl_path, title, first_user_prompt, message_count, created_ticks, last_used_ticks
			FROM sessions
			WHERE external_id IN ({string.Join(",", paramNames)});
			""";
		for (var i = 0; i < externalIds.Count; i++)
			cmd.Parameters.AddWithValue(paramNames[i], externalIds[i]);

		var results = new List<ClaudeSessionSummaryModel>();
		using var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			results.Add(new ClaudeSessionSummaryModel
			{
				SessionId       = reader.GetString(0),
				ProjectSlug     = reader.GetString(1),
				JsonlPath       = reader.GetString(2),
				GeneratedTitle  = reader.IsDBNull(3) ? null : reader.GetString(3),
				FirstUserPrompt = reader.IsDBNull(4) ? null : reader.GetString(4),
				MessageCount    = reader.GetInt32(5),
				Created         = new DateTimeOffset(reader.GetInt64(6), TimeSpan.Zero),
				LastUsed        = new DateTimeOffset(reader.GetInt64(7), TimeSpan.Zero),
			});
		}
		return results;
	}

	private static string? GetStoredTitle(SqliteConnection conn, string externalId)
	{
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT title FROM sessions WHERE external_id = @externalId AND title IS NOT NULL;";
		cmd.Parameters.AddWithValue("@externalId", externalId);
		var result = cmd.ExecuteScalar();
		return result as string;
	}

	private void PersistTitles(List<ClaudeSessionSummaryModel> summaries, Dictionary<string, string> generated)
	{
		using var conn = OpenConnection();
		using var tx = conn.BeginTransaction();

		var summariesById = summaries.ToDictionary(s => s.SessionId);
		var nowTicks = DateTimeOffset.UtcNow.UtcTicks;

		foreach (var (sessionId, title) in generated)
		{
			if (!summariesById.TryGetValue(sessionId, out var summary))
				continue;

			var projectSlug = summary.ProjectSlug
				?? Path.GetFileName(Path.GetDirectoryName(summary.JsonlPath)) ?? string.Empty;
			long mtimeTicks;
			try { mtimeTicks = File.GetLastWriteTimeUtc(summary.JsonlPath).Ticks; }
			catch (IOException) { mtimeTicks = 0; }

			using var cmd = conn.CreateCommand();
			cmd.Transaction = tx;
			cmd.CommandText = """
				INSERT INTO sessions (external_id, project_slug, jsonl_path, mtime_ticks, title, title_generated_at_ticks, first_user_prompt, message_count, created_ticks, last_used_ticks)
				VALUES (@externalId, @projectSlug, @jsonlPath, @mtimeTicks, @title, @titleGeneratedAtTicks, @firstUserPrompt, @messageCount, @createdTicks, @lastUsedTicks)
				ON CONFLICT(external_id) DO UPDATE SET
					title = excluded.title,
					title_generated_at_ticks = excluded.title_generated_at_ticks;
				""";
			cmd.Parameters.AddWithValue("@externalId", sessionId);
			cmd.Parameters.AddWithValue("@projectSlug", projectSlug);
			cmd.Parameters.AddWithValue("@jsonlPath", summary.JsonlPath);
			cmd.Parameters.AddWithValue("@mtimeTicks", mtimeTicks);
			cmd.Parameters.AddWithValue("@title", title);
			cmd.Parameters.AddWithValue("@titleGeneratedAtTicks", nowTicks);
			cmd.Parameters.AddWithValue("@firstUserPrompt", (object?)summary.FirstUserPrompt ?? DBNull.Value);
			cmd.Parameters.AddWithValue("@messageCount", summary.MessageCount);
			cmd.Parameters.AddWithValue("@createdTicks", summary.Created.UtcTicks);
			cmd.Parameters.AddWithValue("@lastUsedTicks", summary.LastUsed.UtcTicks);
			cmd.ExecuteNonQuery();
		}

		tx.Commit();
	}
}
