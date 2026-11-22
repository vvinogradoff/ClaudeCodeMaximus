namespace TessynDesktop.Codex.Protocol;

/// <summary><c>clientInfo</c> of <c>initialize</c> (FR.33.6).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexClientInfo
{
	public string Name { get; set; } = null!;
	public string? Title { get; set; }
	public string Version { get; set; } = null!;
}
