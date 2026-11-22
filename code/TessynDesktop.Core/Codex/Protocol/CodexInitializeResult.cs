namespace TessynDesktop.Codex.Protocol;

/// <summary>Result of <c>initialize</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexInitializeResult
{
	public string? UserAgent { get; set; }
	public string? CodexHome { get; set; }
	public string? PlatformFamily { get; set; }
	public string? PlatformOs { get; set; }
}
