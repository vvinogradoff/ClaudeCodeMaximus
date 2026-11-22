using System.Collections.Generic;

namespace TessynDesktop.Codex.Protocol;

/// <summary>Result of <c>model/list</c>: one page; <c>nextCursor</c> is null on the last.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexModelListResult
{
	public List<CodexModel> Data { get; set; } = [];
	public string? NextCursor { get; set; }
}
