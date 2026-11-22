namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of <c>turn/interrupt</c> (FR.33.8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTurnInterruptParams
{
	public string ThreadId { get; set; } = null!;
	public string TurnId { get; set; } = null!;
}
