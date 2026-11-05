using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Owns a persistent, machine-wide SQLite FTS5 index over every Claude CLI session JSONL under
/// <c>~/.claude/projects/*/</c> and under each Claude account profile's own config directory
/// (<c>%AppData%\TessynDesktop\profiles\&lt;profileId&gt;\projects\*/</c>), plus a persisted title
/// cache — the in-process replacement for the NodeJS tessyn daemon's cross-project search and
/// title-generation features (Stage 6).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IClaudeSessionIndexService
{
	/// <summary>
	/// Raised after <see cref="IndexSessionAsync"/> re-indexed a session (<see cref="SessionIndexedEventArgs.ExternalId"/> set)
	/// and after <see cref="ReindexAsync"/> (re)indexed at least one session (ExternalId null). Raised outside the write gate; may fire on any thread.
	/// </summary>
	event EventHandler<SessionIndexedEventArgs>? SessionIndexed;

	/// <summary>
	/// Scans all Claude CLI project directories and (re)indexes any session whose JSONL file is
	/// new or has changed since the last index (tracked by file mtime). Returns the number of
	/// sessions (re)indexed.
	/// </summary>
	Task<int> ReindexAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Full-text searches indexed message content across all projects, ranked by relevance
	/// (FTS5 bm25, exact-phrase matches preferred over incidental term co-occurrence). Optionally
	/// scoped to a single project slug and/or role. Capped by <paramref name="limit"/> (default 50)
	/// — intended for a ranked top-N results display, not a full membership test (see
	/// <see cref="SearchMatchingSessionIdsAsync"/> for that).
	/// </summary>
	Task<IReadOnlyList<SessionSearchResultModel>> SearchAsync(
		string query,
		string? projectSlug = null,
		string? role = null,
		int? limit = null,
		int? offset = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns the distinct set of external IDs with at least one matching message — unranked and
	/// uncapped, unlike <see cref="SearchAsync"/>. Use this to filter/hide already-known tree nodes
	/// by content match, where a hard result-count limit would silently drop genuine matches.
	/// Optionally scoped to a single project slug (e.g. to limit the session tree's search box to
	/// the currently selected session's project).
	/// </summary>
	Task<IReadOnlySet<string>> SearchMatchingSessionIdsAsync(
		string query, string? projectSlug = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Per-session match counts: <see cref="SessionSearchScoreModel.StrictMatchCount"/> (messages containing the
	/// query as an exact phrase) and <see cref="SessionSearchScoreModel.LooseMatchCount"/> (messages containing
	/// all query words anywhere). One entry per session with a loose match; unranked and uncapped.
	/// </summary>
	Task<IReadOnlyList<SessionSearchScoreModel>> SearchSessionScoresAsync(
		string query, string? projectSlug = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Re-indexes one session from its JSONL file when it changed since last indexed. Returns false when
	/// the file is not found or unchanged.
	/// </summary>
	Task<bool> IndexSessionAsync(string externalId, string workingDirectory, CancellationToken cancellationToken = default);

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
