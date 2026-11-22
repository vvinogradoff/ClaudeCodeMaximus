using System.Collections.Generic;

namespace TessynDesktop.Codex.Protocol;

/// <summary><c>turn/plan/updated</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTurnPlanUpdatedNotification
{
	public string ThreadId { get; set; } = null!;
	public string TurnId { get; set; } = null!;
	public string? Explanation { get; set; }
	public List<CodexTurnPlanStep> Plan { get; set; } = [];
}
