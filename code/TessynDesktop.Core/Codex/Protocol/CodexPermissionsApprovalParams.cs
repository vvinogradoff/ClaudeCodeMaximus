using System.Text.Json;

namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of the <c>item/permissions/requestApproval</c> server request (FR.33.8). The requested profile is kept raw.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexPermissionsApprovalParams
{
	public string ThreadId { get; set; } = null!;
	public string TurnId { get; set; } = null!;
	public string ItemId { get; set; } = null!;
	public string? Cwd { get; set; }
	public string? Reason { get; set; }
	public JsonElement Permissions { get; set; }
}
