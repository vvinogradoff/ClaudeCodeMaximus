using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TessynDesktop.Codex.Protocol;

/// <summary>A turn item (open union on <c>type</c>): the members of the variants Tessyn maps (FR.33.11) flattened, and every other member in <see cref="Extra"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadItem
{
	public string Type { get; set; } = null!;
	public string Id { get; set; } = null!;
	public string? Text { get; set; }
	public string? Phase { get; set; }
	public string? Command { get; set; }
	public int? ExitCode { get; set; }
	public string? Status { get; set; }
	public string? AggregatedOutput { get; set; }
	public List<CodexFileUpdateChange>? Changes { get; set; }
	public string? Server { get; set; }
	public string? Tool { get; set; }
	public CodexMcpToolCallError? Error { get; set; }
	public string? Query { get; set; }
	public string? Path { get; set; }

	/// <summary>Members of this item that Tessyn does not model.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
