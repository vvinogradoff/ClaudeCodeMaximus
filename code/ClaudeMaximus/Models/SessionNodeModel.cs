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
/// ExternalId is the stable daemon-side identifier (UUID from JSONL). Used for all Tessyn daemon references.
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

	/// <summary>
	/// Stable session identifier for the Tessyn daemon (UUID from JSONL filename or session_id).
	/// Populated from ClaudeSessionId during migration, or from run.system event for new sessions.
	/// Null for sessions not yet mapped to the daemon.
	/// </summary>
	public string? ExternalId { get; set; }

	/// <summary>
	/// The original project path where this session's JSONL lives. Used for cross-project
	/// imported sessions where WorkingDirectory differs from the session's origin.
	/// When set, run.send uses this path instead of WorkingDirectory for --resume to work.
	/// Null means WorkingDirectory is the original project (same-project session).
	/// </summary>
	public string? OriginalProjectPath { get; set; }

	/// <summary>Persisted vertical scroll offset for the session output area.</summary>
	public double ScrollOffset { get; set; }

	/// <summary>Per-session auto-commit toggle (FR.11.3). Persisted across app restarts.</summary>
	public bool IsAutoCommit { get; set; }

	/// <summary>Per-session auto-document toggle (FR.11.5). Persisted across app restarts.</summary>
	public bool IsAutoDocument { get; set; }

	/// <summary>
	/// Model ID used for all turns (user, scheduled, orchestrated) on this session (FR.16.3).
	/// Null or empty means "inherit from directory/app setting".
	/// Set interactively via the UI or by the <c>set_session_model</c> MCP tool (FR.15.10).
	/// </summary>
	public string? ModelId { get; set; }

	/// <summary>
	/// Nesting depth in the supervisor→worker chain (FR.15.5, FR.16).
	/// 0 = top-level user session. Set by orchestration tools at spawn time.
	/// </summary>
	public int OrchestrationDepth { get; set; }

	/// <summary>
	/// NodeId of the session that spawned this one. Null for user-created sessions.
	/// Used for depth-limit enforcement (FR.15.5).
	/// </summary>
	public string? SupervisorNodeId { get; set; }

	/// <summary>
	/// Returns the best available session identity key: ExternalId if available, otherwise FileName.
	/// Used for cache keying and session restore during the migration period.
	/// </summary>
	public string SessionKey => ExternalId ?? FileName;
}
