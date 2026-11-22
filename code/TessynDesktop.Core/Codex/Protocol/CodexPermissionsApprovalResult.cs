using System.Text.Json;

namespace TessynDesktop.Codex.Protocol;

/// <summary>Tessyn's answer to a permissions request: the requested profile, granted for the turn (FR.33.8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexPermissionsApprovalResult
{
	public JsonElement Permissions { get; set; }
	public string Scope { get; set; } = null!;
}
