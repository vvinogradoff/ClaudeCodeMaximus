namespace ClaudeMaximus.Models.Agent;

/// <summary>
/// Compact session descriptor returned by the <c>list_sessions</c> MCP tool (FR.15.1).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionSummaryModel(
	string NodeId,
	string Name,
	string DirectoryLabel,
	string WorkingDirectory,
	bool IsRunning,
	bool IsResumable,
	string? LastPrompt);
