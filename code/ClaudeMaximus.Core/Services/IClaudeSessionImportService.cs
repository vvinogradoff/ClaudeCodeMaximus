using System.Collections.Generic;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public interface IClaudeSessionImportService
{
	/// <summary>
	/// Scans ~/.claude/projects/&lt;slug&gt;/ for JSONL session files and returns
	/// summary metadata for each discovered session (FR.13.2, FR.13.3).
	/// Pre-populates GeneratedTitle from cache for previously titled sessions.
	/// </summary>
	IReadOnlyList<ClaudeSessionSummaryModel> DiscoverSessions(string workingDirectory);

	/// <summary>
	/// Parses a Claude Code JSONL session file into ClaudeMaximus session entries (FR.13.7).
	/// Extracts USER, ASSISTANT, and tool-use SYSTEM entries. Skips malformed lines.
	/// </summary>
	IReadOnlyList<SessionEntryModel> ParseJsonlSession(string jsonlPath);

	/// <summary>
	/// Parses a Claude Code JSONL session file in raw mode — preserves instruction blocks
	/// and system-injected content that would normally be stripped.
	/// </summary>
	IReadOnlyList<SessionEntryModel> ParseJsonlSessionRaw(string jsonlPath);

	/// <summary>
	/// Caches a generated title for a session ID. Survives across dialog open/close cycles.
	/// </summary>
	void CacheTitle(string sessionId, string title);

	/// <summary>
	/// Returns a cached title for a session ID, or null if not cached.
	/// </summary>
	string? GetCachedTitle(string sessionId);

	/// <summary>
	/// Extracts summary metadata (first prompt, prompt samples, message count, timestamps) from
	/// a single JSONL session file, for search indexing or title generation. Returns null if the
	/// file is unreadable.
	/// </summary>
	ClaudeSessionSummaryModel? ExtractSummary(string sessionId, string jsonlPath);
}
