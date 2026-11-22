namespace TessynDesktop.Codex.Protocol;

/// <summary>One step of a turn plan: <c>pending</c>, <c>inProgress</c> or <c>completed</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTurnPlanStep
{
	public string Step { get; set; } = null!;
	public string Status { get; set; } = null!;
}
