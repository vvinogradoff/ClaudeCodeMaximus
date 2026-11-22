namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of the <c>mcpServer/elicitation/request</c> server request (FR.33.8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexElicitationParams
{
	public string ServerName { get; set; } = null!;
	public string? ThreadId { get; set; }
	public string? TurnId { get; set; }
	public string? Mode { get; set; }
	public string? Message { get; set; }
}
