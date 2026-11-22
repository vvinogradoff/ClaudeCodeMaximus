namespace TessynDesktop.Codex.Protocol;

/// <summary><c>mcpServer/startupStatus/updated</c> (FR.33.9): <c>starting</c>, <c>ready</c>, <c>failed</c> or <c>cancelled</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexMcpServerStartupStatusNotification
{
	public string Name { get; set; } = null!;
	public string Status { get; set; } = null!;
	public string? Error { get; set; }
	public string? FailureReason { get; set; }
	public string? ThreadId { get; set; }
}
