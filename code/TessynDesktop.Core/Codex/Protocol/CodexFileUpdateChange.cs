using System.Text.Json;

namespace TessynDesktop.Codex.Protocol;

/// <summary>One file of a <c>fileChange</c> item. <c>kind</c> is an open union, kept raw.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexFileUpdateChange
{
	public string Path { get; set; } = null!;
	public JsonElement? Kind { get; set; }
	public string? Diff { get; set; }
}
