using System.Collections.Generic;

namespace ClaudeMaximus.Models;

/// <summary>
/// Terminal tree node representing one Claude Code session.
/// Name is user-assigned and stored in appsettings.json.
/// FileName is the bare file name (e.g. 2026-03-12-1430-xkqbzf.txt) relative to SessionFilesRoot.
/// WorkingDirectory is the filesystem path used when launching the claude process.
/// ClaudeSessionId is captured from the first result event and used for --resume on subsequent launches.
/// NodeId is a stable GUID that never changes (unlike ClaudeSessionId) and is used by agent tools (FR.14.2).
/// AgentToken is a random secret injected into the per-node --mcp-config to identify the calling node (FR.14.3).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionNodeModel
{
	public required string Name { get; set; }
	public required string FileName { get; init; }
	public string WorkingDirectory { get; set; } = string.Empty;
	public string? ClaudeSessionId { get; set; }

	/// <summary>Stable GUID for this node. Backfilled from empty on first load (FR.14.2).</summary>
	public string NodeId { get; set; } = string.Empty;

	/// <summary>Per-node secret passed as X-CMX-Token to the in-process MCP server (FR.14.3).</summary>
	public string AgentToken { get; set; } = string.Empty;

	/// <summary>Session IDs from previous Claude sessions that were cleared (FR.11.7).
	/// Preserved so the JSONL view can show the full history across session resets.</summary>
	public List<string> PriorClaudeSessionIds { get; set; } = [];

	/// <summary>Persisted vertical scroll offset for the session output area.</summary>
	public double ScrollOffset { get; set; }

	/// <summary>Per-session auto-commit toggle (FR.11.3). Persisted across app restarts.</summary>
	public bool IsAutoCommit { get; set; }

	/// <summary>Per-session auto-document toggle (FR.11.5). Persisted across app restarts.</summary>
	public bool IsAutoDocument { get; set; }
}
