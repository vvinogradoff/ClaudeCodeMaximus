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

	public ClaudeSessionIndexService(
		IClaudeSessionImportService importService,
		IClaudeAssistService assistService,
		string? dbPath = null,
		IReadOnlyList<string>? claudeProjectsDirs = null)
	{
		_importService = importService;
		_assistService = assistService;
		_dbPath = dbPath ?? GetDefaultDbPath();
		_claudeProjectsDirs = claudeProjectsDirs ?? GetDefaultClaudeProjectsDirs();

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

	private void InitializeSchema()
	{
		using var conn = new SqliteConnection(ConnectionString);
		conn.Open();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = """
			PRAGMA journal_mode=WAL;

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
			""";
		cmd.ExecuteNonQuery();
	}

	// ── Reindex ───────────────────────────────────────────────────────────────────

	public Task<int> ReindexAsync(CancellationToken cancellationToken = default) =>
		Task.Run(() => ReindexCore(cancellationToken), cancellationToken);

	private int ReindexCore(CancellationToken cancellationToken)
	{
		using var conn = new SqliteConnection(ConnectionString);
		conn.Open();

		var indexed = 0;
		foreach (var claudeProjectsDir in _claudeProjectsDirs)
		{
			if (!Directory.Exists(claudeProjectsDir))
			{
				_log.Debug("ReindexAsync: no {Dir} found, skipping", claudeProjectsDir);
				continue;
			}

			foreach (var slugDir in Directory.GetDirectories(claudeProjectsDir))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var slug = Path.GetFileName(slugDir);

				foreach (var jsonlPath in Directory.GetFiles(slugDir, "*" + Constants.ClaudeSessions.SessionFileExtension))
				{
					cancellationToken.ThrowIfCancellationRequested();

					var externalId = Path.GetFileNameWithoutExtension(jsonlPath);
					if (string.IsNullOrEmpty(externalId))
						continue;

					long mtimeTicks;
					try { mtimeTicks = File.GetLastWriteTimeUtc(jsonlPath).Ticks; }
					catch (IOException) { continue; } // file removed mid-scan

					if (GetStoredMtime(conn, externalId) == mtimeTicks)
						continue; // unchanged since last index

					if (IndexSession(conn, externalId, slug, jsonlPath, mtimeTicks))
						indexed++;
				}
			}
		}

		_log.Information("ReindexAsync: (re)indexed {Count} sessions", indexed);
		return indexed;
	}

	private bool IndexSession(SqliteConnection conn, string externalId, string projectSlug, string jsonlPath, long mtimeTicks)
	{
		IReadOnlyList<SessionEntryModel> entries;
		ClaudeSessionSummaryModel? summary;
		try
		{
			entries = _importService.ParseJsonlSession(jsonlPath);
			summary = _importService.ExtractSummary(externalId, jsonlPath);
		}
		catch (Exception ex)
		{
			_log.Debug("IndexSession: failed to parse {Path}: {Error}", jsonlPath, ex.Message);
			return false;
		}

		if (summary == null || summary.MessageCount == 0)
			return false;

		using var tx = conn.BeginTransaction();

		using (var del = conn.CreateCommand())
		{
			del.Transaction = tx;
			del.CommandText = "DELETE FROM messages WHERE external_id = @externalId;";
			del.Parameters.AddWithValue("@externalId", externalId);
			del.ExecuteNonQuery();
		}

		var messageIndex = 0;
		foreach (var entry in entries)
		{
			if (string.IsNullOrWhiteSpace(entry.Content))
				continue;

			using var insert = conn.CreateCommand();
			insert.Transaction = tx;
			insert.CommandText = """
				INSERT INTO messages (content, role, external_id, project_slug, message_index, timestamp_ticks)
				VALUES (@content, @role, @externalId, @projectSlug, @messageIndex, @timestampTicks);
				""";
			insert.Parameters.AddWithValue("@content", entry.Content);
			insert.Parameters.AddWithValue("@role", entry.Role);
			insert.Parameters.AddWithValue("@externalId", externalId);
			insert.Parameters.AddWithValue("@projectSlug", projectSlug);
			insert.Parameters.AddWithValue("@messageIndex", messageIndex);
			insert.Parameters.AddWithValue("@timestampTicks", entry.Timestamp.UtcTicks);
			insert.ExecuteNonQuery();
			messageIndex++;
		}

		using (var upsert = conn.CreateCommand())
		{
			upsert.Transaction = tx;
			upsert.CommandText = """
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
			upsert.Parameters.AddWithValue("@externalId", externalId);
			upsert.Parameters.AddWithValue("@projectSlug", projectSlug);
			upsert.Parameters.AddWithValue("@jsonlPath", jsonlPath);
			upsert.Parameters.AddWithValue("@mtimeTicks", mtimeTicks);
			upsert.Parameters.AddWithValue("@firstUserPrompt", (object?)summary.FirstUserPrompt ?? DBNull.Value);
			upsert.Parameters.AddWithValue("@messageCount", summary.MessageCount);
			upsert.Parameters.AddWithValue("@createdTicks", summary.Created.UtcTicks);
			upsert.Parameters.AddWithValue("@lastUsedTicks", summary.LastUsed.UtcTicks);
			upsert.ExecuteNonQuery();
		}

		tx.Commit();
		return true;
	}

	private static long? GetStoredMtime(SqliteConnection conn, string externalId)
	{
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT mtime_ticks FROM sessions WHERE external_id = @externalId;";
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

	private IReadOnlyList<SessionSearchResultModel> SearchCore(
		string query, string? projectSlug, string? role, int? limit, int? offset)
	{
		if (string.IsNullOrWhiteSpace(query))
			return [];

		using var conn = new SqliteConnection(ConnectionString);
		conn.Open();

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
		cmd.Parameters.AddWithValue("@query", EscapeFtsQuery(query));
		cmd.Parameters.AddWithValue("@projectSlug", (object?)projectSlug ?? DBNull.Value);
		cmd.Parameters.AddWithValue("@role", (object?)role ?? DBNull.Value);
		cmd.Parameters.AddWithValue("@limit", limit ?? 50);
		cmd.Parameters.AddWithValue("@offset", offset ?? 0);

		var results = new List<SessionSearchResultModel>();
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
		return results;
	}

	/// <summary>Escapes double quotes so unbalanced quotes in free-typed queries don't break FTS5 syntax.</summary>
	private static string EscapeFtsQuery(string query) => query.Replace("\"", "\"\"");

	// ── Titles ────────────────────────────────────────────────────────────────────

	public async Task<IReadOnlyDictionary<string, string>> EnsureTitlesAsync(
		IReadOnlyList<ClaudeSessionSummaryModel> summaries,
		Action<IReadOnlyDictionary<string, string>>? onBatchComplete = null,
		CancellationToken cancellationToken = default)
	{
		var result = new Dictionary<string, string>();
		var missing = new List<ClaudeSessionSummaryModel>();

		using (var conn = new SqliteConnection(ConnectionString))
		{
			conn.Open();
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

		using var conn = new SqliteConnection(ConnectionString);
		conn.Open();

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
		using var conn = new SqliteConnection(ConnectionString);
		conn.Open();
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
