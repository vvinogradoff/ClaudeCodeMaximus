namespace ClaudeMaximus.Models;

/// <summary>
/// Instruction toggle snapshot used by <c>InstructionBlockBuilder</c> to construct
/// the hidden instruction block appended to every claude stdin message (FR.11.2).
/// <see cref="IsAgentToolsEnabled"/> is not a toolbar toggle — it mirrors
/// <c>AppSettingsModel.AgentToolsEnabled</c> so the builder can emit the native-scheduling
/// redirect from FR.14.11 only when the host MCP scheduling tools are actually available.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record InstructionOptionsModel(
	bool IsAutoCommit,
	bool IsNewBranch,
	bool IsAutoDocument,
	bool IsAgentToolsEnabled);
