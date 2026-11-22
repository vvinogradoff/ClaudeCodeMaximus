namespace TessynDesktop.Codex.Protocol;

/// <summary>Tessyn's answer to a command or file approval: <c>{ decision: "accept" }</c> (FR.33.8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexApprovalDecisionResult
{
	public string Decision { get; set; } = null!;
}
