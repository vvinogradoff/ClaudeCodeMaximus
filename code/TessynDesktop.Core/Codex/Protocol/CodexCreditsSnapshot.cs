namespace TessynDesktop.Codex.Protocol;

/// <summary>The credits part of a rate-limit snapshot.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexCreditsSnapshot
{
	public bool HasCredits { get; set; }
	public bool Unlimited { get; set; }
	public string? Balance { get; set; }
}
