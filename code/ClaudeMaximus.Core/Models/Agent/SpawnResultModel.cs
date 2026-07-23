namespace ClaudeMaximus.Models.Agent;

/// <summary>Result of <c>spawn_session</c> MCP tool (FR.15.1).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SpawnResultModel(
	string NodeId,
	string SessionName,
	string ResultText,
	bool IsError = false,
	string? ErrorMessage = null);
