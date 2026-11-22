using System;
using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>
/// Terminal tree node representing one Claude Code session.
/// Name is user-assigned and stored in appsettings.json.
/// FileName is the bare file name (e.g. 2026-03-12-1430-xkqbzf.txt) relative to SessionFilesRoot.
/// WorkingDirectory is the filesystem path used when launching the claude process.
/// ClaudeSessionId is captured from the first result event and used for --resume on subsequent launches.
/// NodeId is a stable GUID that never changes (unlike ClaudeSessionId) and is used by agent tools (FR.14.2).
/// AgentToken is a random secret injected into the per-node --mcp-config to identify the calling node (FR.14.3).
/// ExternalId is the stable Claude CLI session identifier (UUID from the JSONL file name), used to key
/// the cross-project search index (IClaudeSessionIndexService) and daemon-owned session lookups.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionNodeModel
{
	public required string Name { get; set; }
	public required string FileName { get; init; }
	public string WorkingDirectory { get; set; } = string.Empty;
	public string? ClaudeSessionId { get; set; }

	/// <summary>
	/// The session engine the session runs on (FR.33.1, <see cref="Constants.SessionEngines"/>). Null and <c>claude</c> both mean the Claude engine,
	/// so settings written before engines existed load unchanged (null values are not written).
	/// </summary>
	public string? EngineId { get; set; }

	/// <summary>
	/// The Codex thread of a Codex session (FR.33.7): its conversation on the Codex engine, like <see cref="ClaudeSessionId"/> on the Claude engine.
	/// Null for a Claude session, and for a Codex session whose thread was never started or was detached.
	/// </summary>
	public string? CodexThreadId { get; set; }

	/// <summary>
	/// Codex threads the session ran on before (FR.33.7, FR.33.12, FR.33.4): a reset, an engine change or a thread that was gone. Null until the
	/// first one, so the settings of sessions that never ran on Codex stay as they are (null values are not written).
	/// </summary>
	public List<string>? PriorCodexThreadIds { get; set; }

	/// <summary>Stable GUID for this node. Backfilled from empty on first load (FR.14.2).</summary>
	public string NodeId { get; set; } = string.Empty;

	/// <summary>Per-node secret passed as X-CMX-Token to the in-process MCP server (FR.14.3).</summary>
	public string AgentToken { get; set; } = string.Empty;

	/// <summary>Session IDs from previous Claude sessions that were cleared (FR.11.7).
	/// Preserved so the JSONL view can show the full history across session resets.</summary>
	public List<string> PriorClaudeSessionIds { get; set; } = [];

	/// <summary>
	/// Stable Claude CLI session identifier (UUID from the JSONL file name). Populated from
	/// ClaudeSessionId by <see cref="Services.SessionIdentityMigration"/>. Null until migrated.
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

	/// <summary>
	/// Per-session auto-commit tri-state toggle (FR.11.3): <c>true</c> = Commit, <c>false</c> =
	/// Do-not-commit, <c>null</c> = No preference (no commit-related instruction is emitted).
	/// Persisted across app restarts. New sessions default to Do-not-commit.
	/// </summary>
	public bool? IsAutoCommit { get; set; } = null;

	/// <summary>Per-session auto-document toggle (FR.11.5). Persisted across app restarts.</summary>
	public bool IsAutoDocument { get; set; }

	/// <summary>
	/// Model ID used for all turns (user, scheduled, orchestrated) on this session (FR.16.3).
	/// Null or empty means "inherit from directory/app setting"; <c>"default"</c> means no <c>--model</c> flag and no fallback (FR.12.35).
	/// Set by a sticky pick of an interactive send, by <c>create_session</c>, by <c>spawn_session</c>'s <c>model</c> and by the
	/// <c>set_session_model</c> MCP tool (FR.15.10). Read through <see cref="Services.ISessionModelSettings"/>.
	/// </summary>
	public string? ModelId { get; set; }

	/// <summary>
	/// The session's effort (FR.12.35): the <c>--effort</c> value of every turn without a per-turn pick (<c>max</c>, <c>high</c>, <c>medium</c>,
	/// <c>low</c>). Null (or <c>"default"</c>) means no flag. Set by a sticky pick of an interactive send and by <c>create_session</c>.
	/// </summary>
	public string? Effort { get; set; }

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
	/// Profile display name pinned for headless (scheduled/orchestrated) turns on this session.
	/// Orchestrated children inherit their supervisor's resolved profile here at spawn so they
	/// authenticate with the same account as the supervisor rather than falling back to Default —
	/// whose OAuth token may be revoked, causing every child turn to 401
	/// (see docs/issues/ISSUE-002-orchestrated-child-profile-inheritance.md).
	/// Null = resolve from this session's own file history, then Default.
	/// </summary>
	public string? ProfileName { get; set; }

	/// <summary>
	/// ISSUE.013: the stable pin of the profile this session runs on: <c>"default"</c> or <see cref="ClaudeProfileModel.ProfileId"/>.
	/// Null = not pinned by id (an old display-name pin in <see cref="ProfileName"/> is migrated on load). A pin that no longer
	/// resolves is an error, never a silent fallback to Default.
	/// </summary>
	public string? ProfileId { get; set; }

	/// <summary>
	/// ISSUE.013: a profile switch that waits for this session's running turn to end (a running session is never interrupted).
	/// Applied (files moved, pin written) when the next turn starts; null when none is queued.
	/// </summary>
	public string? PendingProfileId { get; set; }

	/// <summary>
	/// Returns the best available session identity key: ExternalId if available, otherwise FileName.
	/// Used for cache keying and session restore during the migration period.
	/// </summary>
	public string SessionKey => ExternalId ?? FileName;

	/// <summary>
	/// When the user hid this session from the top-bar recent-sessions dropdown. The session stays
	/// hidden as long as this is at or after the session's last activity (prompt/response/draft edit);
	/// any newer activity makes it reappear without needing to clear this field.
	/// </summary>
	public DateTimeOffset? RecentIgnoredAt { get; set; }

	/// <summary>
	/// Whether the user has marked this session as a favorite (FR.9.7). Favorited sessions always
	/// remain in the top-bar recent-sessions dropdown regardless of recency or <see cref="RecentIgnoredAt"/>.
	/// </summary>
	public bool IsFavorite { get; set; }

	/// <summary>
	/// Streaming Session Mode toggle (FR.19.6): when <c>true</c>, interactive sends on this session
	/// spawn a single persistent <c>claude</c> process (<c>--input-format stream-json</c>) that stays
	/// alive across turns instead of the default One-shot Mode (one process per turn). Sticky and
	/// persisted like <see cref="IsAutoDocument"/> — unlike the in-memory-only chrome toggle, it
	/// survives app restarts, though no process is auto-respawned on restart. Headless (scheduled,
	/// orchestrated) turns always ignore this and run one-shot (FR.19.11).
	/// </summary>
	public bool IsStreamingMode { get; set; }

	/// <summary>
	/// Session Completion Sound toggle (FR.22.2): when <c>true</c> (the default), an audible cue
	/// plays when this session's "Claude is thinking…" state clears on natural turn completion
	/// (not on user-initiated Stop). Sticky and persisted like <see cref="IsStreamingMode"/>.
	/// </summary>
	public bool PlayCompletionSound { get; set; } = true;
}
