namespace TessynDesktop.Codex.Protocol;

/// <summary><c>thread/name/updated</c>; the name is in <c>threadName</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadNameUpdatedNotification
{
	public string ThreadId { get; set; } = null!;
	public string? ThreadName { get; set; }
}
