namespace ClaudeMaximus.Models.Agent;

/// <summary>Arguments for the <c>schedule_wake</c> MCP tool (FR.14.4).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ScheduleWakeArgs(
	/// <summary>When to fire. Exactly one of InSeconds, At, or Cron must be set.</summary>
	ScheduleWhenArgs When,

	/// <summary>Prompt to send when the schedule fires. Defaults to empty (just wakes the session).</summary>
	string Prompt = "",

	/// <summary>Human-readable label shown in list_schedules and the session SYSTEM line.</summary>
	string Note = "",

	/// <summary>Target node to wake. Null = self-wake (caller's own node, FR.14.5).</summary>
	string? Target = null);
