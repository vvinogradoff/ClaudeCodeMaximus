namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of the <c>item/fileChange/requestApproval</c> server request (FR.33.8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexFileChangeApprovalParams
{
	public string ThreadId { get; set; } = null!;
	public string TurnId { get; set; } = null!;
	public string ItemId { get; set; } = null!;
	public string? Reason { get; set; }
	public string? GrantRoot { get; set; }
}
