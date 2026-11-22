using System.Collections.Generic;

namespace TessynDesktop.Codex.Protocol;

/// <summary>Result of <c>thread/start</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexThreadStartResult
{
	public CodexThread Thread { get; set; } = null!;
	public string? Model { get; set; }
	public string? Cwd { get; set; }
	public List<string>? InstructionSources { get; set; }
	public string? ReasoningEffort { get; set; }
}
