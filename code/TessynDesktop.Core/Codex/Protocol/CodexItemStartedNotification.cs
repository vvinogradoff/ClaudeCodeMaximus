namespace TessynDesktop.Codex.Protocol;

/// <summary><c>item/started</c>: carries the full item.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexItemStartedNotification
{
	public CodexThreadItem Item { get; set; } = null!;
	public string ThreadId { get; set; } = null!;
	public string TurnId { get; set; } = null!;
	public long StartedAtMs { get; set; }
}
