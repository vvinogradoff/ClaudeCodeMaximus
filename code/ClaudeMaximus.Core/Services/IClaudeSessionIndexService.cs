using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <summary>
/// Owns a persistent, machine-wide SQLite FTS5 index over every Claude CLI session JSONL under
/// <c>~/.claude/projects/*/</c>, plus a persisted title cache — the in-process replacement for the
/// NodeJS tessyn daemon's cross-project search and title-generation features (Stage 6).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IClaudeSessionIndexService
{
	/// <summary>
	/// Scans all Claude CLI project directories and (re)indexes any session whose JSONL file is
	/// new or has changed since the last index (tracked by file mtime). Returns the number of
	/// sessions (re)indexed.
	/// </summary>
	Task<int> ReindexAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Full-text searches indexed message content across all projects, ranked by relevance
	/// (FTS5 bm25). Optionally scoped to a single project slug and/or role.
	/// </summary>
	Task<IReadOnlyList<SessionSearchResultModel>> SearchAsync(
		string query,
		string? projectSlug = null,
		string? role = null,
		int? limit = null,
		int? offset = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns a title for each summary — from the persisted cache when one already exists,
	/// otherwise generates it (via <see cref="IClaudeAssistService"/>) and persists it. Never
	/// regenerates an already-cached title. Keyed by <see cref="ClaudeSessionSummaryModel.SessionId"/>.
	/// <paramref name="onBatchComplete"/> is invoked after each newly-generated batch, for
	/// progressive UI updates on large title-generation runs.
	/// </summary>
	Task<IReadOnlyDictionary<string, string>> EnsureTitlesAsync(
		IReadOnlyList<ClaudeSessionSummaryModel> summaries,
		Action<IReadOnlyDictionary<string, string>>? onBatchComplete = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Looks up indexed session summary metadata (title, first prompt, message count, timestamps)
	/// for a set of external IDs — e.g. to build display items from cross-project search hits
	/// without re-parsing their JSONL files.
	/// </summary>
	Task<IReadOnlyList<ClaudeSessionSummaryModel>> GetSessionSummariesAsync(
		IReadOnlyList<string> externalIds,
		CancellationToken cancellationToken = default);
}
