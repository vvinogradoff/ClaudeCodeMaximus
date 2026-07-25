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
/// </summary>
/// <remarks>Created by Claude</remarks>
/// <param name="IsAutoCommit">
/// Tri-state Auto-Commit (FR.11.3): <c>true</c> = Commit, <c>false</c> = Do-not-commit,
/// <c>null</c> = No preference (no commit-related line is emitted).
/// </param>
public sealed record InstructionOptionsModel(
	bool? IsAutoCommit,
	bool IsNewBranch,
	bool IsAutoDocument,
	bool IsAgentToolsEnabled,
	bool IsNewSession);
