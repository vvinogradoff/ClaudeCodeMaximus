namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of <c>account/login/start</c>: <c>{ type: "chatgpt" }</c> or <c>{ type: "chatgptDeviceCode" }</c> (FR.33.5).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexLoginStartParams
{
	public string Type { get; set; } = null!;
}
