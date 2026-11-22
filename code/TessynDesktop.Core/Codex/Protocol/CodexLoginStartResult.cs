namespace TessynDesktop.Codex.Protocol;

/// <summary>Result of <c>account/login/start</c> (open union on <c>type</c>).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexLoginStartResult
{
	public string Type { get; set; } = null!;
	public string? LoginId { get; set; }
	public string? AuthUrl { get; set; }
	public string? VerificationUrl { get; set; }
	public string? UserCode { get; set; }
}
