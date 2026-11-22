namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of <c>thread/compact/start</c> (FR.33.12).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadCompactStartParams
{
	public string ThreadId { get; set; } = null!;
}
