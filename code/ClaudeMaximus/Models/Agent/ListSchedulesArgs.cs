namespace ClaudeMaximus.Models.Agent;

/// <summary>Arguments for the <c>list_schedules</c> MCP tool.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ListSchedulesArgs(
	/// <summary>When true returns all app schedules; false returns only the caller's schedules.</summary>
	bool All = false);
