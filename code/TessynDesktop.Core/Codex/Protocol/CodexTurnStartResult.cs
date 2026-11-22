namespace TessynDesktop.Codex.Protocol;

/// <summary>Result of <c>turn/start</c>: the new turn, in progress.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTurnStartResult
{
	public CodexTurn Turn { get; set; } = null!;
}
