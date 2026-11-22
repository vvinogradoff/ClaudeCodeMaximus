namespace TessynDesktop.Codex.Protocol;

/// <summary><c>thread/tokenUsage/updated</c>: one per model call.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTokenUsageUpdatedNotification
{
	public string ThreadId { get; set; } = null!;
	public string TurnId { get; set; } = null!;
	public CodexThreadTokenUsage TokenUsage { get; set; } = null!;
}
