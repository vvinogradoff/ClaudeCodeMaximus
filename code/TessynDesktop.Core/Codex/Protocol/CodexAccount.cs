namespace TessynDesktop.Codex.Protocol;

/// <summary>The signed-in account (open union on <c>type</c>; Tessyn uses <c>chatgpt</c>).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexAccount
{
	public string Type { get; set; } = null!;
	public string? Email { get; set; }
	public string? PlanType { get; set; }
	public string? WorkspaceName { get; set; }
}
