namespace TessynDesktop.Codex.Protocol;

/// <summary>Result of <c>account/login/cancel</c>: <c>canceled</c> or <c>notFound</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexLoginCancelResult
{
	public string? Status { get; set; }
}
