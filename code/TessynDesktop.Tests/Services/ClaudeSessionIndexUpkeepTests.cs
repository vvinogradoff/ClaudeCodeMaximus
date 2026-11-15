using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Upkeep of the cross-project search index (decision A5): rowid map, migration from the old schema, orphan purge,
/// optimize / vacuum policy and the throttled reindex. Everything runs on temp databases built from synthetic JSONL.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeSessionIndexUpkeepTests : IDisposable
{
	private readonly string _tempRoot;
	private readonly string _projectsDir;
	private readonly string _dbPath;
	private readonly FakeClaudeAssistService _assist = new();

	public ClaudeSessionIndexUpkeepTests()
	{
		_tempRoot = Path.Combine(Path.GetTempPath(), "cmx-index-upkeep-" + Guid.NewGuid().ToString("N"));
		_projectsDir = Path.Combine(_tempRoot, "projects");
		Directory.CreateDirectory(_projectsDir);
		_dbPath = Path.Combine(_tempRoot, "index.db");
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort cleanup */ }
	}

	private ClaudeSessionIndexService NewService(TimeSpan? quiet = null, string? dbPath = null) =>
		new(new ClaudeSessionImportService(), _assist, dbPath ?? _dbPath, [_projectsDir], quiet ?? TimeSpan.Zero);

	private string WriteSession(string slug, string id, params string[] userMessages)
	{
		var dir = Path.Combine(_projectsDir, slug);
		Directory.CreateDirectory(dir);
		var path = Path.Combine(dir, id + ".jsonl");
		var lines = userMessages.Select((m, i) =>
			"{\"type\":\"user\",\"timestamp\":\"2026-01-01T00:" + i.ToString("D2") + ":00Z\",\"message\":{\"role\":\"user\",\"content\":" + JsonSerializer.Serialize(m) + "}}");
		File.WriteAllLines(path, lines);
		return path;
	}

	private string WriteToolResult(string slug, string id, string content)
	{
		var dir = Path.Combine(_projectsDir, slug);
		Directory.CreateDirectory(dir);
		var path = Path.Combine(dir, id + ".jsonl");
		File.WriteAllLines(path, [
			"""{"type":"user","timestamp":"2026-01-01T00:00:00Z","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":""" + JsonSerializer.Serialize(content) + "}]}}"]);
		return path;
	}

	private SqliteConnection Open()
	{
		var c = new SqliteConnection($"Data Source={_dbPath}");
		c.Open();
		return c;
	}

	private long Scalar(string sql)
	{
		using var c = Open();
		using var cmd = c.CreateCommand();
		cmd.CommandText = sql;
		return Convert.ToInt64(cmd.ExecuteScalar());
	}

	private void Exec(string sql)
	{
		using var c = Open();
		using var cmd = c.CreateCommand();
		cmd.CommandText = sql;
		cmd.ExecuteNonQuery();
	}

	/// <summary>Creates an index database in the pre-upkeep layout (no rowid map, user_version 2) holding the given sessions.</summary>
	private void BuildOldSchemaDatabase(IReadOnlyList<(string Slug, string Id, string[] Messages)> sessions)
	{
		using var c = Open();
		using (var cmd = c.CreateCommand())
		{
			cmd.CommandText = """
				PRAGMA journal_mode=WAL;
				CREATE TABLE sessions (
					external_id TEXT PRIMARY KEY, project_slug TEXT NOT NULL, jsonl_path TEXT NOT NULL,
					mtime_ticks INTEGER NOT NULL, title TEXT, title_generated_at_ticks INTEGER,
					first_user_prompt TEXT, message_count INTEGER NOT NULL,
					created_ticks INTEGER NOT NULL, last_used_ticks INTEGER NOT NULL);
				CREATE VIRTUAL TABLE messages USING fts5(content, role UNINDEXED, external_id UNINDEXED,
					project_slug UNINDEXED, message_index UNINDEXED, timestamp_ticks UNINDEXED);
				PRAGMA user_version = 2;
				""";
			cmd.ExecuteNonQuery();
		}

		foreach (var (slug, id, messages) in sessions)
		{
			var path = WriteSession(slug, id, messages);
			var mtime = File.GetLastWriteTimeUtc(path).Ticks;
			using var tx = c.BeginTransaction();
			for (var i = 0; i < messages.Length; i++)
			{
				using var ins = c.CreateCommand();
				ins.Transaction = tx;
				ins.CommandText = "INSERT INTO messages (content, role, external_id, project_slug, message_index, timestamp_ticks) VALUES (@c,'USER',@id,@slug,@i,@t);";
				ins.Parameters.AddWithValue("@c", messages[i]);
				ins.Parameters.AddWithValue("@id", id);
				ins.Parameters.AddWithValue("@slug", slug);
				ins.Parameters.AddWithValue("@i", i);
				ins.Parameters.AddWithValue("@t", new DateTime(2026, 1, 1, 0, i, 0, DateTimeKind.Utc).Ticks);
				ins.ExecuteNonQuery();
			}
			using var s = c.CreateCommand();
			s.Transaction = tx;
			s.CommandText = "INSERT INTO sessions (external_id, project_slug, jsonl_path, mtime_ticks, first_user_prompt, message_count, created_ticks, last_used_ticks) VALUES (@id,@slug,@p,@m,@f,@n,0,0);";
			s.Parameters.AddWithValue("@id", id);
			s.Parameters.AddWithValue("@slug", slug);
			s.Parameters.AddWithValue("@p", path);
			s.Parameters.AddWithValue("@m", mtime);
			s.Parameters.AddWithValue("@f", messages[0]);
			s.Parameters.AddWithValue("@n", messages.Length);
			s.ExecuteNonQuery();
			tx.Commit();
		}
	}

	// ── Migration from the old schema ─────────────────────────────────────────────

	private static readonly (string Slug, string Id, string[] Messages)[] OldSessions =
	[
		("-p-a", "s-alpha", ["deploy the staging config now", "check the deploy log"]),
		("-p-a", "s-beta", ["fix the login bug in the deploy script"]),
		("-p-b", "s-gamma", ["unrelated gardening notes", "tomatoes and deploy of compost"]),
	];

	[Fact]
	public async Task Migration_FromOldSchema_KeepsSearchIdenticalAndDoesNotRebuildTheFtsTable()
	{
		BuildOldSchemaDatabase(OldSessions);
		var service = NewService();

		// Before: the old layout still searches, and construction did not touch the version (no work at startup).
		Assert.Equal(2, Scalar("PRAGMA user_version;"));
		var before = await service.SearchAsync("deploy");
		var beforeIds = await service.SearchMatchingSessionIdsAsync("deploy");
		var beforeScores = await service.SearchSessionScoresAsync("deploy");
		Assert.NotEmpty(before);

		var report = await service.RunUpkeepAsync();

		Assert.True(report.SchemaMigrated);
		Assert.Equal(0, report.Indexed); // metadata only: no session was re-read or re-inserted
		Assert.Equal(3, Scalar("PRAGMA user_version;"));
		Assert.Equal(3, Scalar("SELECT COUNT(*) FROM session_rows;"));
		Assert.Equal(5, Scalar("SELECT SUM(row_count) FROM session_rows;"));

		var after = await service.SearchAsync("deploy");
		Assert.Equal(before.Select(r => (r.ExternalId, r.MessageIndex, r.Content, r.Role, r.Rank)),
			after.Select(r => (r.ExternalId, r.MessageIndex, r.Content, r.Role, r.Rank)));
		Assert.Equal(beforeIds, await service.SearchMatchingSessionIdsAsync("deploy")); // FR.13.15 tree search
		Assert.Equal(
			beforeScores.OrderBy(s => s.ExternalId).Select(s => (s.ExternalId, s.LooseMatchCount, s.StrictMatchCount)),
			(await service.SearchSessionScoresAsync("deploy")).OrderBy(s => s.ExternalId).Select(s => (s.ExternalId, s.LooseMatchCount, s.StrictMatchCount)));
	}

	[Fact]
	public async Task Migration_IsResumable_WhenTheAppClosesBeforeItRuns_AndRunsOnce()
	{
		BuildOldSchemaDatabase(OldSessions);
		_ = NewService(); // "app start", closed before any upkeep ran
		var restarted = NewService();

		Assert.Equal(2, Scalar("PRAGMA user_version;"));
		Assert.NotEmpty(await restarted.SearchAsync("deploy")); // still searchable while pending

		var first = await restarted.RunUpkeepAsync();
		var second = await restarted.RunUpkeepAsync();

		Assert.True(first.SchemaMigrated);
		Assert.False(second.SchemaMigrated);
		Assert.Equal(3, Scalar("PRAGMA user_version;"));
	}

	[Fact]
	public async Task Migration_ThenReindexOfOneSession_ReplacesOnlyThatSessionsRows()
	{
		BuildOldSchemaDatabase(OldSessions);
		var service = NewService();
		await service.RunUpkeepAsync();

		var path = WriteSession("-p-a", "s-alpha", "rewritten alpha content only");
		File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
		var report = await service.RunUpkeepAsync();

		Assert.Equal(1, report.Indexed);
		Assert.Empty(await service.SearchAsync("staging"));
		Assert.Single(await service.SearchAsync("rewritten"));
		Assert.Equal(2, (await service.SearchMatchingSessionIdsAsync("deploy")).Count); // beta and gamma untouched
		Assert.Equal(4, Scalar("SELECT COUNT(*) FROM messages;")); // alpha now 1 row, beta 1, gamma 2
	}

	[Fact]
	public async Task Migration_FallsBackToScanDelete_WhenARecordedRangeDoesNotCoverTheSession()
	{
		BuildOldSchemaDatabase(OldSessions);
		var service = NewService();
		await service.RunUpkeepAsync();

		// Corrupt the map: claim fewer rows than the session has (a non-contiguous session would look like this).
		Exec("UPDATE session_rows SET last_rowid = first_rowid, row_count = 2 WHERE external_id = 's-alpha';");
		var path = WriteSession("-p-a", "s-alpha", "fresh alpha");
		File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
		await service.RunUpkeepAsync();

		Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messages WHERE external_id = 's-alpha';"));
		Assert.Empty(await service.SearchAsync("staging"));
	}

	// ── Delete by session uses the index ──────────────────────────────────────────

	[Fact]
	public async Task DeleteBySession_UsesTheIndex_AndIsMuchFasterThanTheOldFullScan()
	{
		var service = NewService();
		await service.RunUpkeepAsync(); // fresh database, schema current

		// 400 sessions x 25 messages x ~2 KB straight into the tables (skips JSONL: this is about the delete path).
		using (var c = Open())
		{
			using var tx = c.BeginTransaction();
			var body = string.Join(' ', Enumerable.Repeat("lorem ipsum dolor sit amet", 80));
			long rowid = 0;
			for (var s = 0; s < 400; s++)
			{
				var first = rowid + 1;
				for (var m = 0; m < 25; m++)
				{
					using var ins = c.CreateCommand();
					ins.Transaction = tx;
					ins.CommandText = "INSERT INTO messages (content, role, external_id, project_slug, message_index, timestamp_ticks) VALUES (@c,'USER',@id,'-p',@i,0);";
					ins.Parameters.AddWithValue("@c", body + " s" + s + "m" + m);
					ins.Parameters.AddWithValue("@id", "sess-" + s);
					ins.Parameters.AddWithValue("@i", m);
					ins.ExecuteNonQuery();
					rowid++;
				}
				using var map = c.CreateCommand();
				map.Transaction = tx;
				map.CommandText = "INSERT INTO session_rows VALUES (@id,@f,@l,25);";
				map.Parameters.AddWithValue("@id", "sess-" + s);
				map.Parameters.AddWithValue("@f", first);
				map.Parameters.AddWithValue("@l", rowid);
				map.ExecuteNonQuery();
			}
			tx.Commit();
		}

		// The lookup the delete starts with is an index search, not a scan.
		using (var c = Open())
		using (var cmd = c.CreateCommand())
		{
			cmd.CommandText = "EXPLAIN QUERY PLAN " + Constants.SqlQueries.SelectRowRange;
			cmd.Parameters.AddWithValue("@externalId", "sess-1");
			using var r = cmd.ExecuteReader();
			var plan = new List<string>();
			while (r.Read()) plan.Add(r.GetString(3));
			Assert.Contains(plan, p => p.Contains("SEARCH session_rows") && p.Contains("INDEX"));
		}

		TimeSpan Time(string sql, string id, params (string, object)[] extra)
		{
			using var c = Open();
			using var tx = c.BeginTransaction();
			using var cmd = c.CreateCommand();
			cmd.Transaction = tx;
			cmd.CommandText = sql;
			cmd.Parameters.AddWithValue("@externalId", id);
			foreach (var (n, v) in extra) cmd.Parameters.AddWithValue(n, v);
			var sw = Stopwatch.StartNew();
			var changed = cmd.ExecuteNonQuery();
			sw.Stop();
			Assert.Equal(25, changed);
			tx.Rollback();
			return sw.Elapsed;
		}

		// 399 is the last session: the old scan visits every row either way, the mapped delete only its 25.
		long first399 = Scalar("SELECT first_rowid FROM session_rows WHERE external_id='sess-399';");
		long last399 = Scalar("SELECT last_rowid FROM session_rows WHERE external_id='sess-399';");
		var scan = Enumerable.Range(0, 3).Select(_ => Time(Constants.SqlQueries.DeleteMessagesByScan, "sess-399")).Min();
		var ranged = Enumerable.Range(0, 3).Select(_ => Time(Constants.SqlQueries.DeleteMessagesByRange, "sess-399", ("@first", first399), ("@last", last399))).Min();

		Assert.True(ranged < scan / 3, $"ranged delete {ranged.TotalMilliseconds:F1} ms should be far below the scan {scan.TotalMilliseconds:F1} ms");
	}

	// ── Orphan purge ────────────────────────────────────────────────────────────────

	[Fact]
	public async Task OrphanPurge_RemovesSessionsWhoseJsonlIsGone_AndKeepsTheRest()
	{
		WriteSession("-p-a", "keep-1", "keeper content about widgets");
		var gone = WriteSession("-p-a", "gone-1", "vanished content about sprockets");
		WriteSession("-p-b", "keep-2", "another keeper about widgets");
		var service = NewService();
		await service.RunUpkeepAsync();
		Assert.Single(await service.SearchAsync("sprockets"));

		File.Delete(gone);
		var report = await service.RunUpkeepAsync();

		Assert.Equal(1, report.Purged);
		Assert.Empty(await service.SearchAsync("sprockets"));
		Assert.Equal(2, (await service.SearchMatchingSessionIdsAsync("widgets")).Count);
		Assert.Equal(0, Scalar("SELECT COUNT(*) FROM sessions WHERE external_id='gone-1';"));
		Assert.Equal(0, Scalar("SELECT COUNT(*) FROM session_rows WHERE external_id='gone-1';"));
	}

	[Fact]
	public async Task OrphanPurge_SessionPresentInAnyConfiguredHome_IsNotAnOrphan()
	{
		var second = Path.Combine(_tempRoot, "profiles", "p1", "projects");
		Directory.CreateDirectory(Path.Combine(second, "-p-x"));
		File.Copy(WriteSession("-p-a", "in-first", "lives in the default home"), Path.Combine(second, "-p-x", "in-second.jsonl"));
		var service = new ClaudeSessionIndexService(new ClaudeSessionImportService(), _assist, _dbPath, [_projectsDir, second], TimeSpan.Zero);
		await service.RunUpkeepAsync();
		Assert.Equal(2, Scalar("SELECT COUNT(*) FROM sessions;"));

		var report = await service.RunUpkeepAsync();

		Assert.Equal(0, report.Purged);
		Assert.Equal(2, Scalar("SELECT COUNT(*) FROM sessions;"));
	}

	[Fact]
	public async Task OrphanPurge_IsSkipped_WhenTheScanSawNoFilesAtAll()
	{
		var path = WriteSession("-p-a", "s-1", "precious cached content");
		var service = NewService();
		await service.RunUpkeepAsync();
		File.Delete(path); // projects folder now empty: indistinguishable from an unmounted home

		var report = await service.RunUpkeepAsync();

		Assert.Equal(0, report.Purged);
		Assert.True(report.PurgeSkippedUnsafeScan);
		Assert.Single(await service.SearchAsync("precious"));
	}

	// ── Optimize, vacuum, checkpoint ──────────────────────────────────────────────

	private async Task<ClaudeSessionIndexService> ServiceWithFreePagesAsync()
	{
		var filler = string.Join(' ', Enumerable.Repeat("vacuum filler words that take up pages", 400));
		for (var i = 0; i < 60; i++)
			WriteSession("-p-v", "v-" + i, filler + " " + i, filler + " again " + i);
		var service = NewService();
		await service.RunUpkeepAsync();
		foreach (var f in Directory.GetFiles(Path.Combine(_projectsDir, "-p-v")).Where(f => !f.EndsWith("v-58.jsonl")).Take(55))
			File.Delete(f);
		await service.RunUpkeepAsync(); // purge frees pages (default thresholds: too small to vacuum)
		return service;
	}

	[Fact]
	public async Task Vacuum_DoesNotRun_BelowTheThreshold()
	{
		var service = await ServiceWithFreePagesAsync();
		Assert.True(Scalar("PRAGMA freelist_count;") > 0);

		var report = await service.RunUpkeepAsync();

		Assert.False(report.Vacuumed); // default policy: 64 MB and 20 % free
		Assert.True(Scalar("PRAGMA freelist_count;") > 0);
	}

	[Fact]
	public async Task Vacuum_RunsOnceFreePagesExceedTheThreshold_AndShrinksTheFile()
	{
		var service = await ServiceWithFreePagesAsync();
		service.VacuumMinFreeBytes = 1;
		service.VacuumMinFreeRatio = 0.05;
		var sizeBefore = new FileInfo(_dbPath).Length;

		var report = await service.RunUpkeepAsync();

		Assert.True(report.Vacuumed);
		Assert.True(report.Checkpointed);
		Assert.Equal(0, Scalar("PRAGMA freelist_count;"));
		Assert.True(new FileInfo(_dbPath).Length < sizeBefore);
		Assert.Single(await service.SearchAsync("vacuum filler words that take up pages again 58"));
	}

	[Fact]
	public async Task Vacuum_IsDeferred_WhileASearchIsInFlight()
	{
		var service = await ServiceWithFreePagesAsync();
		service.VacuumMinFreeBytes = 1;
		service.VacuumMinFreeRatio = 0.05;

		ClaudeSessionIndexService.SearchScope scope = new(service);
		var blocked = await service.RunUpkeepAsync();
		scope.Dispose();
		var released = await service.RunUpkeepAsync();

		Assert.False(blocked.Vacuumed);
		Assert.True(blocked.VacuumSkipped);
		Assert.True(released.Vacuumed);
	}

	[Fact]
	public async Task Optimize_RunsAfterALargeReindexOnly()
	{
		for (var i = 0; i < 210; i++)
			WriteSession("-p-o", "o-" + i, "optimize candidate " + i);
		WriteSession("-p-o2", "small", "one more");
		var service = NewService();

		var large = await service.RunUpkeepAsync();
		var idle = await service.RunUpkeepAsync();

		Assert.True(large.Optimized);
		Assert.True(large.Checkpointed);
		Assert.False(idle.Optimized);
		Assert.Single(await service.SearchAsync("optimize candidate 7"));
	}

	// ── Throttled reindex and live files ──────────────────────────────────────────

	[Fact]
	public async Task Reindex_SkipsAFileThatIsBeingWritten_AndIndexesItOnceItIsQuiet()
	{
		var live = WriteSession("-p-a", "live-1", "streaming session content");
		WriteSession("-p-a", "done-1", "finished session content").Let(p => File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddMinutes(-10)));
		var service = NewService(TimeSpan.FromMinutes(1));

		var first = await service.RunUpkeepAsync();
		Assert.Equal(1, first.Indexed);
		Assert.Equal(1, first.Deferred);
		Assert.Empty(await service.SearchAsync("streaming"));

		File.SetLastWriteTimeUtc(live, DateTime.UtcNow.AddMinutes(-2));
		var second = await service.RunUpkeepAsync();
		Assert.Equal(1, second.Indexed);
		Assert.Single(await service.SearchAsync("streaming"));
	}

	[Fact]
	public async Task IndexSessionAsync_StillIndexesAFreshFile_BecauseItWasAskedForExplicitly()
	{
		var cwd = @"C:\work\live";
		var slug = Constants.ClaudeSessions.BuildProjectSlug(cwd);
		WriteSession(slug, "turn-1", "just finished turn");
		var service = NewService(TimeSpan.FromMinutes(1));

		Assert.True(await service.IndexSessionAsync("turn-1", cwd));
		Assert.Single(await service.SearchAsync("finished turn"));
	}

	[Fact]
	public async Task Reindex_RunningWhileSearchesAndSingleSessionIndexingHappen_NeverFailsWithBusy()
	{
		var cwd = @"C:\work\busy";
		var slug = Constants.ClaudeSessions.BuildProjectSlug(cwd);
		for (var i = 0; i < 120; i++)
			WriteSession(slug, "b-" + i, "concurrent haystack message number " + i, "second concurrent line " + i);
		var service = NewService();
		await service.RunUpkeepAsync();
		foreach (var f in Directory.GetFiles(Path.Combine(_projectsDir, slug))) // make every session stale
			File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(-3));

		var upkeep = service.RunUpkeepAsync();
		var searches = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
		{
			while (!upkeep.IsCompleted)
			{
				Assert.NotNull(await service.SearchAsync("concurrent haystack"));
				Assert.NotNull(await service.SearchMatchingSessionIdsAsync("concurrent"));
			}
		})).ToList();
		var singles = Task.Run(async () =>
		{
			for (var i = 0; i < 20; i++)
				await service.IndexSessionAsync("b-" + i, cwd);
		});

		var report = await upkeep;
		await Task.WhenAll(searches.Append(singles));

		Assert.True(report.Indexed > 0);
		Assert.Equal(120, Scalar("SELECT COUNT(*) FROM sessions;"));
		Assert.Equal(240, Scalar("SELECT COUNT(*) FROM messages;"));
	}

	// ── No truncation (decision A5) ─────────────────────────────────────────────────

	[Fact]
	public async Task LargeToolOutput_IsIndexedInFull_NothingIsTruncated()
	{
		const int size = 2 * 1024 * 1024;
		var filler = string.Join(' ', Enumerable.Range(0, size / 5).Select(i => "w" + (i % 1000).ToString("D3")));
		var content = filler[..(size - 40)] + " zzzterminalneedle";
		var service = NewService();
		WriteToolResult("-p-big", "big-1", content);

		await service.RunUpkeepAsync();

		var hit = Assert.Single(await service.SearchAsync("zzzterminalneedle"));
		Assert.Equal("TOOL_RESULT", hit.Role);
		Assert.Equal(content.Length, hit.Content.Length);
		Assert.True(hit.Content.Length >= size - 100);
		Assert.EndsWith("zzzterminalneedle", hit.Content);
	}
}

internal static class UpkeepTestExtensions
{
	public static void Let<T>(this T value, Action<T> action) => action(value);
}
