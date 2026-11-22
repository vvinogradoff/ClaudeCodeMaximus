namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of the <c>item/commandExecution/requestApproval</c> server request (FR.33.8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexCommandApprovalParams
{
	public string ThreadId { get; set; } = null!;
	public string TurnId { get; set; } = null!;
	public string ItemId { get; set; } = null!;
	public string? Command { get; set; }
	public string? Cwd { get; set; }
	public string? Reason { get; set; }
}
