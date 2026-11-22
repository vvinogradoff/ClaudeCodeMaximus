using System.Text.Json.Serialization;

namespace TessynDesktop.Codex.Protocol;

/// <summary><c>account/updated</c>. Only <c>authMode: null</c> is a sign-out; <c>reason: "workspaceRoutingReady"</c> comes at every turn start and is not an auth change (FR.33.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexAccountUpdatedNotification
{
	public string? AuthMode { get; set; }
	public string? PlanType { get; set; }
	public string? Reason { get; set; }

	/// <summary>True for a sign-out (<c>authMode: null</c>).</summary>
	[JsonIgnore]
	public bool IsSignedOut => AuthMode is null;
}
