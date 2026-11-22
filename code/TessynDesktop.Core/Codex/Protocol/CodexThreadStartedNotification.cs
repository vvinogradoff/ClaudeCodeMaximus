namespace TessynDesktop.Codex.Protocol;

/// <summary><c>thread/started</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadStartedNotification
{
	public CodexThread Thread { get; set; } = null!;
}
