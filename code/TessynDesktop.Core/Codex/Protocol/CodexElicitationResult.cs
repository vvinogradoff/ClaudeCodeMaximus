namespace TessynDesktop.Codex.Protocol;

/// <summary>Tessyn's answer to an MCP elicitation: <c>{ action: "decline" }</c> (FR.33.8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexElicitationResult
{
	public string Action { get; set; } = null!;
}
