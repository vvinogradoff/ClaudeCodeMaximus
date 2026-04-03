using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

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
	/// Parses a Claude Code JSONL session file into TessynDesktop session entries (FR.13.7).
	/// Extracts USER, ASSISTANT, and tool-use SYSTEM entries. Skips malformed lines.
	/// </summary>
	IReadOnlyList<SessionEntryModel> ParseJsonlSession(string jsonlPath);

	/// <summary>
	/// Caches a generated title for a session ID. Survives across dialog open/close cycles.
	/// </summary>
	void CacheTitle(string sessionId, string title);

	/// <summary>
	/// Returns a cached title for a session ID, or null if not cached.
	/// </summary>
	string? GetCachedTitle(string sessionId);
}
