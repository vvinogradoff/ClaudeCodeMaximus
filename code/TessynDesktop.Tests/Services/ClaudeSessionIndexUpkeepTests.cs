using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TessynDesktop.Models;
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
	private readonly FakeTreeSessionIds _tree = new();

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
		new(new ClaudeSessionImportService(), _assist, dbPath ?? _dbPath, [_projectsDir], quiet ?? TimeSpan.Zero, _tree);

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

		// The planner pushes the rowid bounds into the FTS5 scan ("><" = rowid >= and <=); the old delete has none.
		string Plan(string sql)
		{
			using var c = Open();
			using var cmd = c.CreateCommand();
			cmd.CommandText = "EXPLAIN QUERY PLAN " + sql;
			cmd.Parameters.AddWithValue("@externalId", "sess-1");
			cmd.Parameters.AddWithValue("@first", 1);
			cmd.Parameters.AddWithValue("@last", 25);
			using var r = cmd.ExecuteReader();
			var details = new List<string>();
			while (r.Read()) details.Add(r.GetString(3));
			return string.Join(" | ", details);
		}
		var rangedPlan = Plan(Constants.SqlQueries.DeleteMessagesByRange);
		var scanPlan = Plan(Constants.SqlQueries.DeleteMessagesByScan);
		Assert.Contains("><", rangedPlan);
		Assert.DoesNotContain("><", scanPlan);

		TimeSpan Time(string sql, params (string, object)[] extra)
		{
			using var c = Open();
			using var tx = c.BeginTransaction();
			using var cmd = c.CreateCommand();
			cmd.Transaction = tx;
			cmd.CommandText = sql;
			cmd.Parameters.AddWithValue("@externalId", "sess-399");
			foreach (var (n, v) in extra) cmd.Parameters.AddWithValue(n, v);
			var sw = Stopwatch.StartNew();
			var changed = cmd.ExecuteNonQuery();
			sw.Stop();
			Assert.Equal(25, changed);
			tx.Rollback();
			return sw.Elapsed;
		}

		// Soft secondary check: wall-clock only has to agree with the plan, and only when the scan was measurable.
		long first399 = Scalar("SELECT first_rowid FROM session_rows WHERE external_id='sess-399';");
		long last399 = Scalar("SELECT last_rowid FROM session_rows WHERE external_id='sess-399';");
		var scan = Enumerable.Range(0, 3).Select(_ => Time(Constants.SqlQueries.DeleteMessagesByScan)).Min();
		var ranged = Enumerable.Range(0, 3).Select(_ => Time(Constants.SqlQueries.DeleteMessagesByRange, ("@first", first399), ("@last", last399))).Min();
		if (scan > TimeSpan.FromMilliseconds(20))
			Assert.True(ranged < scan, $"ranged delete {ranged.TotalMilliseconds:F1} ms should be below the scan {scan.TotalMilliseconds:F1} ms");
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
		var service = new ClaudeSessionIndexService(new ClaudeSessionImportService(), _assist, _dbPath, [_projectsDir, second], TimeSpan.Zero, _tree);
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

	// ── Purge scope: the tree owns its sessions ─────────────────────────────────

	[Fact]
	public async Task OrphanPurge_KeepsRowsAndTitleOfATreeSession_WhoseJsonlIsGone()
	{
		var treePath = WriteSession("-p-a", "tree-1", "tree session transcript about gizmos");
		var strayPath = WriteSession("-p-a", "stray-1", "stray session transcript about gizmos");
		WriteSession("-p-a", "present-1", "still on disk");
		var service = NewService();
		await service.RunUpkeepAsync();
		Exec("UPDATE sessions SET title = 'Kept title' WHERE external_id = 'tree-1';");
		_tree.Ids.Add("tree-1");

		File.Delete(treePath);
		File.Delete(strayPath);
		var report = await service.RunUpkeepAsync();

		Assert.Equal(1, report.Purged);
		Assert.Equal(1, report.KeptForTree);
		Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messages WHERE external_id = 'tree-1';"));
		Assert.Equal(1, Scalar("SELECT COUNT(*) FROM sessions WHERE external_id = 'tree-1' AND title = 'Kept title';"));
		Assert.Equal(0, Scalar("SELECT COUNT(*) FROM messages WHERE external_id = 'stray-1';"));
		Assert.Equal(0, Scalar("SELECT COUNT(*) FROM sessions WHERE external_id = 'stray-1';"));
		Assert.Single(await service.SearchAsync("tree session transcript"));
	}

	[Fact]
	public async Task OrphanPurge_IsSkipped_WhenNoTreeSourceIsConfigured()
	{
		var gone = WriteSession("-p-a", "g-1", "needs a tree source before purging");
		WriteSession("-p-a", "g-2", "stays");
		var service = new ClaudeSessionIndexService(new ClaudeSessionImportService(), _assist, _dbPath, [_projectsDir], TimeSpan.Zero);
		await service.RunUpkeepAsync();
		File.Delete(gone);

		var report = await service.RunUpkeepAsync();

		Assert.Equal(0, report.Purged);
		Assert.Equal(1, Scalar("SELECT COUNT(*) FROM sessions WHERE external_id = 'g-1';"));
	}

	[Fact]
	public void TreeSessionIdSource_CollectsCurrentExternalAndPriorIdsOfEveryNode_AtAnyDepth()
	{
		var settings = new AppSettingsService(Path.Combine(_tempRoot, "settings"));
		var group = new GroupNodeModel { Name = "g" };
		group.Sessions.Add(new SessionNodeModel { Name = "n2", FileName = "n2.txt", ClaudeSessionId = "grouped-current" });
		var inner = new GroupNodeModel { Name = "inner" };
		inner.Sessions.Add(new SessionNodeModel { Name = "n3", FileName = "n3.txt", ExternalId = "deep-external" });
		group.Groups.Add(inner);
		var dir = new DirectoryNodeModel { Path = "C:/x" };
		dir.Sessions.Add(new SessionNodeModel
		{
			Name = "n1", FileName = "n1.txt", ClaudeSessionId = "current-id", PriorClaudeSessionIds = ["prior-a", "prior-b"],
		});
		dir.Groups.Add(group);
		settings.Settings.Tree.Add(dir);

		var ids = new TreeSessionIdSource(settings).GetTreeSessionIds();

		Assert.Equal(new[] { "current-id", "deep-external", "grouped-current", "prior-a", "prior-b" }, ids.OrderBy(i => i).ToArray());
	}

	[Fact]
	public async Task OrphanPurge_KeepsTheOldIdsRows_OfANodeWhoseSessionIdChanged()
	{
		var oldPath = WriteSession("-p-a", "old-id", "transcript before the clear about doohickeys");
		WriteSession("-p-a", "new-id", "transcript after the clear");
		var settings = new AppSettingsService(Path.Combine(_tempRoot, "settings2"));
		var dir = new DirectoryNodeModel { Path = "C:/x" };
		dir.Sessions.Add(new SessionNodeModel { Name = "n", FileName = "n.txt", ClaudeSessionId = "new-id", PriorClaudeSessionIds = ["old-id"] });
		settings.Settings.Tree.Add(dir);
		var service = new ClaudeSessionIndexService(new ClaudeSessionImportService(), _assist, _dbPath, [_projectsDir], TimeSpan.Zero, new TreeSessionIdSource(settings));
		await service.RunUpkeepAsync();

		File.Delete(oldPath);
		var report = await service.RunUpkeepAsync();

		Assert.Equal(0, report.Purged);
		Assert.Equal(1, report.KeptForTree);
		Assert.Single(await service.SearchAsync("doohickeys"));
	}

	// ── Review fixes (314) ──────────────────────────────────────────────────────────

	[Fact]
	public async Task X1_TitleWrites_RunningWithReindexAndPurge_RaiseNoSqliteException()
	{
		for (var i = 0; i < 120; i++)
			WriteSession("-p-x1", "keep-" + i, "keeper message " + i, "second line " + i);
		for (var i = 0; i < 40; i++)
			WriteSession("-p-x1", "doomed-" + i, "doomed message " + i);
		var service = NewService();
		await service.RunUpkeepAsync();
		foreach (var f in Directory.GetFiles(Path.Combine(_projectsDir, "-p-x1"), "doomed-*")) File.Delete(f);
		foreach (var f in Directory.GetFiles(Path.Combine(_projectsDir, "-p-x1"), "keep-*")) File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(-3));

		var titlePath = Path.Combine(_projectsDir, "-p-t");
		for (var i = 0; i < 30; i++) _assist.TitlesToReturn["title-" + i] = "Title " + i;
		var titles = Task.Run(async () =>
		{
			for (var i = 0; i < 30; i++)
			{
				var summary = new ClaudeSessionSummaryModel
				{
					SessionId = "title-" + i, JsonlPath = Path.Combine(titlePath, "title-" + i + ".jsonl"),
					MessageCount = 1, ProjectSlug = "-p-t",
				};
				await service.EnsureTitlesAsync([summary]);
			}
		});

		var report = await service.RunUpkeepAsync(); // reindex of 120 sessions + purge of 60, while titles commit
		await titles;

		Assert.Equal(0, report.Failed);
		Assert.True(report.Purged >= 40);
		Assert.Equal(120, Scalar("SELECT COUNT(*) FROM sessions WHERE external_id LIKE 'keep-%';"));
	}

	[Fact]
	public async Task X2_MapWrittenStaleByAnOlderBuild_IsRebuilt_AndLeavesNoDuplicateHits()
	{
		var pathA = WriteSession("-p-a", "A", "alpha one", "alpha two");
		WriteSession("-p-a", "B", "beta only");
		var service = NewService();
		await service.RunUpkeepAsync();

		// An older build rewrites A's rows (more of them, new rowids) and knows nothing about session_rows.
		Exec("DELETE FROM messages WHERE external_id = 'A';");
		for (var i = 0; i < 3; i++)
			Exec("INSERT INTO messages (content, role, external_id, project_slug, message_index, timestamp_ticks) VALUES ('staleold " + i + "', 'USER', 'A', '-p-a', " + i + ", 0);");

		WriteSession("-p-a", "A", "alpha rewritten");
		File.SetLastWriteTimeUtc(pathA, DateTime.UtcNow.AddMinutes(-5));
		var report = await service.RunUpkeepAsync();

		Assert.True(report.MapRebuilt);
		Assert.Equal(1, Scalar("SELECT COUNT(*) FROM messages WHERE external_id = 'A';"));
		Assert.Empty(await service.SearchAsync("staleold"));
		Assert.Single(await service.SearchAsync("rewritten"));

		var again = await service.RunUpkeepAsync();
		Assert.False(again.MapRebuilt); // consistent now: no rebuild on every start
	}

	[Fact]
	public async Task X3_ConfiguredHomeThatIsMissingButStillHasRows_BlocksThePurge()
	{
		var second = Path.Combine(_tempRoot, "profiles", "p9", "projects");
		Directory.CreateDirectory(Path.Combine(second, "-p-x"));
		File.WriteAllLines(Path.Combine(second, "-p-x", "in-b.jsonl"), File.ReadAllLines(WriteSession("-p-a", "in-a", "in home a")));
		WriteSession("-p-a", "gone-a", "will vanish from a");
		WriteSession("-p-a", "stay-1", "stay one");
		WriteSession("-p-a", "stay-2", "stay two");
		var service = new ClaudeSessionIndexService(new ClaudeSessionImportService(), _assist, _dbPath, [_projectsDir, second], TimeSpan.Zero, _tree);
		await service.RunUpkeepAsync();

		Directory.Delete(second, recursive: true); // the unmounted or moved home
		File.Delete(Path.Combine(_projectsDir, "-p-a", "gone-a.jsonl"));
		var report = await service.RunUpkeepAsync();

		Assert.Equal(0, report.Purged);
		Assert.True(report.PurgeSkippedUnsafeScan);
		Assert.Equal(1, Scalar("SELECT COUNT(*) FROM sessions WHERE external_id = 'in-b';"));
		Assert.Equal(1, Scalar("SELECT COUNT(*) FROM sessions WHERE external_id = 'gone-a';"));
	}

	[Fact]
	public async Task X3_OrphanWhoseStoredHomeWasNotListedInThisScan_IsSkipped()
	{
		WriteSession("-p-a", "listed-gone", "gone from a listed home");
		WriteSession("-p-a", "elsewhere", "recorded under some other home");
		for (var i = 0; i < 4; i++) WriteSession("-p-a", "ballast-" + i, "ballast " + i);
		var service = NewService();
		await service.RunUpkeepAsync();

		Exec("UPDATE sessions SET jsonl_path = 'Z:/not-configured/projects/-p-a/elsewhere.jsonl' WHERE external_id = 'elsewhere';");
		File.Delete(Path.Combine(_projectsDir, "-p-a", "listed-gone.jsonl"));
		File.Delete(Path.Combine(_projectsDir, "-p-a", "elsewhere.jsonl"));
		var report = await service.RunUpkeepAsync();

		Assert.Equal(1, report.Purged);
		Assert.Equal(1, report.PurgeSkippedUnlisted);
		Assert.Equal(1, Scalar("SELECT COUNT(*) FROM sessions WHERE external_id = 'elsewhere';"));
		Assert.Equal(0, Scalar("SELECT COUNT(*) FROM sessions WHERE external_id = 'listed-gone';"));
	}

	[Fact]
	public async Task X3_APurgeOfMoreThanHalfTheIndex_IsSkippedWithAWarning()
	{
		var paths = Enumerable.Range(0, 4).Select(i => WriteSession("-p-a", "s-" + i, "session " + i)).ToList();
		var service = NewService();
		await service.RunUpkeepAsync();

		File.Delete(paths[0]);
		File.Delete(paths[1]);
		File.Delete(paths[2]); // 3 of 4 gone
		var report = await service.RunUpkeepAsync();

		Assert.Equal(0, report.Purged);
		Assert.True(report.PurgeSkippedOverCap);
		Assert.Equal(4, Scalar("SELECT COUNT(*) FROM sessions;"));
	}

	[Fact]
	public async Task X4_X5_AFailingSessionWrite_IsCounted_TheRunCarriesOn_AndIndexSessionAsyncDoesNotThrow()
	{
		var cwd = "C:/work/fail";
		var slug = Constants.ClaudeSessions.BuildProjectSlug(cwd);
		var victim = WriteSession(slug, "victim", "victim original");
		var gone = WriteSession("-p-b", "to-purge", "purge me");
		for (var i = 0; i < 6; i++) WriteSession("-p-b", "ok-" + i, "fine " + i);
		var service = NewService();
		await service.RunUpkeepAsync();

		// Every write of a map row now fails; searches keep working off the committed data.
		Exec("CREATE TRIGGER fail_map BEFORE INSERT ON session_rows BEGIN SELECT RAISE(ABORT, 'boom'); END;");
		File.WriteAllLines(victim, ["""{"type":"user","timestamp":"2026-01-01T00:00:00Z","message":{"role":"user","content":"victim changed"}}"""]);
		File.SetLastWriteTimeUtc(victim, DateTime.UtcNow.AddMinutes(-5));
		File.Delete(gone);

		Assert.False(await service.IndexSessionAsync("victim", cwd)); // reported, not thrown
		var report = await service.RunUpkeepAsync();

		Assert.True(report.Failed >= 1);
		Assert.Equal(1, report.Purged); // the pass carried on to the purge
		Assert.Single(await service.SearchAsync("original")); // the failed write rolled back: old rows still searchable
	}

	[Fact]
	public async Task X2_ACorruptOrMissingMap_LeavesSearchWorking_AndIsRebuiltByTheNextUpkeep()
	{
		WriteSession("-p-a", "one", "findable content one");
		WriteSession("-p-a", "two", "findable content two");
		var service = NewService();
		await service.RunUpkeepAsync();

		Exec("DROP TABLE session_rows;"); // a failed migration or a hand-damaged index
		var restarted = NewService();

		Assert.Equal(2, (await restarted.SearchAsync("findable")).Count);
		var report = await restarted.RunUpkeepAsync();
		Assert.True(report.MapRebuilt);
		Assert.Equal(2, Scalar("SELECT COUNT(*) FROM session_rows;"));
		Assert.Equal(0, report.Indexed);
	}

	[Fact]
	public async Task X7_AJsonlWithoutIndexableMessages_IsRecordedAndNotParsedAgain()
	{
		var dir = Path.Combine(_projectsDir, "-p-z");
		Directory.CreateDirectory(dir);
		File.WriteAllLines(Path.Combine(dir, "empty-1.jsonl"), ["""{"type":"summary","summary":"nothing said"}"""]);
		var service = NewService();

		var first = await service.RunUpkeepAsync();
		var second = await service.RunUpkeepAsync();

		Assert.Equal(1, first.Indexed);
		Assert.Equal(0, second.Indexed); // unchanged by mtime: not re-parsed
		Assert.Equal(0, Scalar("SELECT row_count FROM session_rows WHERE external_id = 'empty-1';"));
		Assert.Equal(1, Scalar("SELECT COUNT(*) FROM sessions WHERE external_id = 'empty-1';"));
	}

	[Fact]
	public async Task X6_AFutureDatedFile_IsNotLive_AndIsIndexedAtOnce()
	{
		var path = WriteSession("-p-a", "future-1", "from the future");
		File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(2));
		var service = NewService(TimeSpan.FromMinutes(1));

		var report = await service.RunUpkeepAsync();

		Assert.Equal(0, report.Deferred);
		Assert.Equal(1, report.Indexed);
		Assert.Single(await service.SearchAsync("from the future"));
	}

	[Fact]
	public async Task X6_DeferredPasses_RetryTheLiveFile_AreBounded_AndNeverOverlap()
	{
		var path = WriteSession("-p-a", "live-1", "still being written");
		var service = NewService(TimeSpan.FromMilliseconds(150));

		using var touching = new System.Threading.CancellationTokenSource();
		var toucher = Task.Run(async () =>
		{
			while (!touching.IsCancellationRequested)
			{
				File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
				await Task.Delay(20);
			}
		});

		// Two upkeeps at once both end with a deferred file: still one chain of at most DeferredPassLimit passes.
		var runs = await Task.WhenAll(service.RunUpkeepAsync(), service.RunUpkeepAsync());
		Assert.All(runs, r => Assert.Equal(1, r.Deferred));
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (service.DeferredPassesRun < Constants.SessionIndex.DeferredPassLimit && DateTime.UtcNow < deadline)
			await Task.Delay(50);
		await Task.Delay(800);
		touching.Cancel();
		await toucher;

		Assert.Equal(Constants.SessionIndex.DeferredPassLimit, service.DeferredPassesRun);
		Assert.Empty(await service.SearchAsync("still being written")); // never quiet, so never indexed

		// Quiet again: the next explicit upkeep indexes it.
		File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
		Assert.Equal(1, (await service.RunUpkeepAsync()).Indexed);
	}

	[Fact]
	public async Task X6_DeferredPass_IndexesAFileOnceItIsQuiet()
	{
		WriteSession("-p-a", "settling-1", "settles shortly");
		var service = NewService(TimeSpan.FromMilliseconds(150));

		var first = await service.RunUpkeepAsync();
		Assert.Equal(1, first.Deferred);

		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (DateTime.UtcNow < deadline && (await service.SearchAsync("settles shortly")).Count == 0)
			await Task.Delay(50);
		Assert.Single(await service.SearchAsync("settles shortly"));
	}

	[Fact]
	public async Task RowidsAndTheMap_StayInAgreement_AcrossARealVacuum()
	{
		var service = await ServiceWithFreePagesAsync();
		service.VacuumMinFreeBytes = 1;
		service.VacuumMinFreeRatio = 0.05;

		List<(string, long, long, long)> Truth()
		{
			var list = new List<(string, long, long, long)>();
			using var c = Open();
			using var cmd = c.CreateCommand();
			cmd.CommandText = "SELECT external_id, MIN(rowid), MAX(rowid), COUNT(*) FROM messages GROUP BY external_id ORDER BY external_id;";
			using var r = cmd.ExecuteReader();
			while (r.Read()) list.Add((r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3)));
			return list;
		}
		List<(string, long, long, long)> Map()
		{
			var list = new List<(string, long, long, long)>();
			using var c = Open();
			using var cmd = c.CreateCommand();
			cmd.CommandText = "SELECT external_id, first_rowid, last_rowid, row_count FROM session_rows WHERE row_count > 0 ORDER BY external_id;";
			using var r = cmd.ExecuteReader();
			while (r.Read()) list.Add((r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3)));
			return list;
		}
		Assert.Equal(Truth(), Map());
		var before = Truth();

		var report = await service.RunUpkeepAsync();

		Assert.True(report.Vacuumed);
		Assert.False(report.MapRebuilt); // the audit saw a consistent map after the VACUUM
		Assert.Equal(before, Truth());   // VACUUM did not renumber any FTS rowid
		Assert.Equal(Truth(), Map());
	}

	// ── Optimize, vacuum, checkpoint ──────────────────────────────────────────────

	private async Task<ClaudeSessionIndexService> ServiceWithFreePagesAsync()
	{
		var filler = string.Join(' ', Enumerable.Repeat("vacuum filler words that take up pages", 400));
		for (var i = 0; i < 60; i++)
			WriteSession("-p-v", "v-" + i, filler + " " + i, filler + " again " + i);
		for (var i = 0; i < 80; i++) // ballast keeps the purge under the 50% cap
			WriteSession("-p-ballast", "ballast-" + i, "small " + i);
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

internal sealed class FakeTreeSessionIds : ITreeSessionIdSource
{
	public HashSet<string> Ids { get; } = new();
	public IReadOnlySet<string> GetTreeSessionIds() => Ids.ToHashSet();
}

internal static class UpkeepTestExtensions
{
	public static void Let<T>(this T value, Action<T> action) => action(value);
}
