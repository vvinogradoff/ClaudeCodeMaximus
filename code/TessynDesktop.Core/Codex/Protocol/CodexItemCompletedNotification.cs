namespace TessynDesktop.Codex.Protocol;

/// <summary><c>item/completed</c>: the authoritative item.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexItemCompletedNotification
{
	public CodexThreadItem Item { get; set; } = null!;
	public string ThreadId { get; set; } = null!;
	public string TurnId { get; set; } = null!;
	public long CompletedAtMs { get; set; }
}
