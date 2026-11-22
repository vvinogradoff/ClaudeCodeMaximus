namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of <c>initialize</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexInitializeParams
{
	public CodexClientInfo ClientInfo { get; set; } = null!;
	public CodexInitializeCapabilities? Capabilities { get; set; }
}
