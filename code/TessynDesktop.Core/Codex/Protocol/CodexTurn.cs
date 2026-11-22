using System.Collections.Generic;

namespace TessynDesktop.Codex.Protocol;

/// <summary>A turn: <c>status</c> is <c>completed</c>, <c>interrupted</c>, <c>failed</c> or <c>inProgress</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTurn
{
	public string Id { get; set; } = null!;
	public string Status { get; set; } = null!;
	public CodexTurnError? Error { get; set; }
	public long? DurationMs { get; set; }
	public List<CodexThreadItem>? Items { get; set; }
}
