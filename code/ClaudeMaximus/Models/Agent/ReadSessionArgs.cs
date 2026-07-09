namespace ClaudeMaximus.Models.Agent;

/// <summary>Arguments for the <c>read_session</c> MCP tool (FR.15.1).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ReadSessionArgs(
	string NodeId,
	/// <summary>Maximum number of entries to return from the end of the session file. 0 = all.</summary>
	int LastN = 20);
