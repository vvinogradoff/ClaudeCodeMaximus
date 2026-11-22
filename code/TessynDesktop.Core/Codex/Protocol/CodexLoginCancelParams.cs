namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of <c>account/login/cancel</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexLoginCancelParams
{
	public string LoginId { get; set; } = null!;
}
