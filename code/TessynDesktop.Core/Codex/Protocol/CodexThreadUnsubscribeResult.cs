namespace TessynDesktop.Codex.Protocol;

/// <summary>Result of <c>thread/unsubscribe</c>: <c>unsubscribed</c>, <c>notSubscribed</c> or <c>notLoaded</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadUnsubscribeResult
{
	public string? Status { get; set; }
}
