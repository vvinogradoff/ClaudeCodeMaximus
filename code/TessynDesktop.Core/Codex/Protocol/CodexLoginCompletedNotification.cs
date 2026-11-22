namespace TessynDesktop.Codex.Protocol;

/// <summary><c>account/login/completed</c>: the outcome of a sign-in (FR.33.5).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexLoginCompletedNotification
{
	public string? LoginId { get; set; }
	public bool Success { get; set; }
	public string? Error { get; set; }
}
