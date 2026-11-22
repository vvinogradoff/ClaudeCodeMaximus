using System.Text.Json.Nodes;

namespace TessynDesktop.Codex.Protocol;

/// <summary>Params of <c>thread/start</c> (FR.33.7). <c>config</c> carries the per-thread overrides of FR.33.9 and FR.33.16.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadStartParams
{
	public string? Cwd { get; set; }
	public string? Model { get; set; }
	public string? ApprovalPolicy { get; set; }
	public string? Sandbox { get; set; }
	public string? ServiceName { get; set; }
	public string? ThreadSource { get; set; }
	public JsonObject? Config { get; set; }
	public string? DeveloperInstructions { get; set; }
}
