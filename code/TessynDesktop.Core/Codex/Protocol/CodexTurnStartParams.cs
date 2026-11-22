using System.Collections.Generic;

namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of <c>turn/start</c> (FR.33.8). <c>model</c> and <c>effort</c> are sent on every turn; a null effort means Default.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTurnStartParams
{
	public string ThreadId { get; set; } = null!;
	public List<CodexUserInput> Input { get; set; } = [];
	public string? Model { get; set; }
	public string? Effort { get; set; }
	public string? Summary { get; set; }
	public string? ClientUserMessageId { get; set; }
}
