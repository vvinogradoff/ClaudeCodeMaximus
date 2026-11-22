namespace TessynDesktop.Codex.Protocol;

/// <summary><c>error</c>: with <c>willRetry: true</c> Codex retries and the turn goes on (FR.33.11).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexErrorNotification
{
	public CodexTurnError Error { get; set; } = null!;
	public bool WillRetry { get; set; }
	public string ThreadId { get; set; } = null!;
	public string TurnId { get; set; } = null!;
}
