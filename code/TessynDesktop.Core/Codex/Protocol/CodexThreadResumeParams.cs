using System.Text.Json.Nodes;

namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of <c>thread/resume</c> (FR.33.7): once per host process, with <c>excludeTurns: true</c> and the thread's config.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadResumeParams
{
	public string ThreadId { get; set; } = null!;
	public bool ExcludeTurns { get; set; }
	public string? Model { get; set; }
	public string? Cwd { get; set; }
	public JsonObject? Config { get; set; }
}
