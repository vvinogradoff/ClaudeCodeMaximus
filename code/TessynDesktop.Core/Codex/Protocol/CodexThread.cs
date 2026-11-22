using System.Text.Json;

namespace TessynDesktop.Codex.Protocol;

/// <summary>The thread of <c>thread/start</c>, <c>thread/resume</c> and <c>thread/started</c> (FR.33.7). <c>path</c> is unstable and not used.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThread
{
	public string Id { get; set; } = null!;
	public string? Name { get; set; }
	public string? Preview { get; set; }
	public string? Cwd { get; set; }
	public string? Model { get; set; }
	public string? Path { get; set; }
	public string? ThreadSource { get; set; }
	public JsonElement? Source { get; set; }
	public JsonElement? Status { get; set; }
}
