namespace TessynDesktop.Codex.Protocol;

/// <summary><c>turn/started</c> and <c>turn/completed</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTurnNotification
{
	public string ThreadId { get; set; } = null!;
	public CodexTurn Turn { get; set; } = null!;
}
