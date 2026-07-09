namespace ClaudeMaximus.Models.Agent;

/// <summary>Arguments for the <c>spawn_session</c> MCP tool (FR.15.1).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SpawnSessionArgs(
	/// <summary>Display name for the new session node.</summary>
	string Name,

	/// <summary>Filesystem working directory for the session. Mutually exclusive with <see cref="ParentNodeId"/>.</summary>
	string? WorkingDir = null,

	/// <summary>NodeId of the parent session — the new session is placed under the same directory/group. Mutually exclusive with <see cref="WorkingDir"/>.</summary>
	string? ParentNodeId = null,

	/// <summary>Initial prompt to run on first turn. Defaults to empty (session created without a first turn).</summary>
	string Prompt = "",

	/// <summary>Group name to place the session under (created if it doesn't exist). Null = no group.</summary>
	string? Group = null);
