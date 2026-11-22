namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of <c>thread/name/set</c> (FR.33.7).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadSetNameParams
{
	public string ThreadId { get; set; } = null!;
	public string Name { get; set; } = null!;
}
