namespace TessynDesktop.Codex.Protocol;

/// <summary>One effort a model supports, with Codex's description (FR.33.3).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexReasoningEffortOption
{
	public string ReasoningEffort { get; set; } = null!;
	public string? Description { get; set; }
}
