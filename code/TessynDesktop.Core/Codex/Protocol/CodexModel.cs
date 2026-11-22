using System.Collections.Generic;

namespace TessynDesktop.Codex.Protocol;

/// <summary>One model of <c>model/list</c> (FR.33.2). A missing <c>inputModalities</c> means text and image.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexModel
{
	public string Id { get; set; } = null!;
	public string? Model { get; set; }
	public string? DisplayName { get; set; }
	public string? Description { get; set; }
	public bool Hidden { get; set; }
	public bool IsDefault { get; set; }
	public List<CodexReasoningEffortOption>? SupportedReasoningEfforts { get; set; }
	public string? DefaultReasoningEffort { get; set; }
	public List<string>? InputModalities { get; set; }
	public string? Upgrade { get; set; }
}
