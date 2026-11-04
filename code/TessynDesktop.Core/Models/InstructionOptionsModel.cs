using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>
/// Instruction toggle snapshot used by <c>InstructionBlockBuilder</c> to construct
/// the hidden instruction block appended to every claude stdin message (FR.11.2).
/// <see cref="IsAgentToolsEnabled"/> is not a toolbar toggle — it mirrors
/// <c>AppSettingsModel.AgentToolsEnabled</c> so the builder can emit the native-scheduling
/// redirect from FR.14.11 only when the host MCP scheduling tools are actually available.
/// <see cref="IsNewSession"/> gates the tool-usage bans (native scheduling, Workflow/Agent,
/// /deep-research) so they are only sent once, at the start of a fresh claude process, instead
/// of on every turn — repeating them each turn reads as a prompt injection to the model and is
/// sometimes ignored outright. Per-turn toggles (auto-commit, branch, document) are unaffected
/// and still re-assert every turn since the user can change them between turns.
/// <see cref="IsStreamingMode"/> additionally gates the native-scheduling redirect (FR.19.9):
/// it describes the mode of the spawn this instruction block is being sent to, not any sticky
/// per-session setting. A headless turn passes <c>false</c> when it spawns its own one-shot
/// --resume process (FR.19.11, the common case), or <c>true</c> when it is instead routed into an
/// already-alive Streaming Session Mode process (FR.19.13) — the value always reflects where the
/// turn is actually going, not the target session's persisted streaming toggle in isolation.
/// </summary>
/// <remarks>Created by Claude</remarks>
/// <param name="IsAutoCommit">
/// Tri-state Auto-Commit (FR.11.3): <c>true</c> = Commit, <c>false</c> = Do-not-commit,
/// <c>null</c> = No preference (no commit-related line is emitted).
/// </param>
/// <param name="IsWorkerSession">
/// True for a Worker Session (<c>OrchestrationDepth &gt; 0</c>, FR.21.19) — suppresses
/// <see cref="Constants.Instructions.TelegramNotifyGuidance"/> entirely, since a Worker Session's
/// own <c>notify_user</c> call is dropped outright by the Telegram Bridge and telling it the tool
/// exists only invites a wasted call. Does not affect any other instruction line.
/// </param>
/// <param name="IsScheduledTurn">
/// True when the scheduler started this turn (<c>TurnSource.Scheduled</c>, FR.21.24) — adds
/// <see cref="Constants.Instructions.ScheduledTurnNotifyReminder"/> on every such turn, since
/// it describes this turn rather than session policy.
/// </param>
/// <param name="PinnedAttentionItems">
/// The session's Attention Items (FR.23.11); the builder lists the open ones in the Pinned list, so
/// the session does not restate them. Null or no open item emits nothing. Only filled for a
/// top-level session with agent tools enabled.
/// </param>
/// <param name="UserClosedAttentionItems">
/// Items the user closed in the Focus view since the previous turn (FR.23.11), taken once through
/// <c>IAttentionService.TakeUnreportedUserClosures</c> so each closure is reported a single time.
/// </param>
public sealed record InstructionOptionsModel(
	bool? IsAutoCommit,
	bool IsNewBranch,
	bool IsAutoDocument,
	bool IsAgentToolsEnabled,
	bool IsNewSession,
	bool IsStreamingMode,
	bool IsTelegramEnabled = false,
	bool IsWorkerSession = false,
	bool IsScheduledTurn = false,
	IReadOnlyList<AttentionItemModel>? PinnedAttentionItems = null,
	IReadOnlyList<AttentionItemModel>? UserClosedAttentionItems = null);
