using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>
/// One already-validated, already-trimmed item to pin (FR.23.4). Built by the MCP tool layer, which owns
/// the input limits; <c>IAttentionService.Post</c> only dedupes, numbers and stores it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionPostRequestModel
{
	public AttentionKind Kind { get; init; }
	public string Title { get; init; } = string.Empty;
	public string Body { get; init; } = string.Empty;
	public IReadOnlyList<string> Options { get; init; } = [];
	public string? Recommendation { get; init; }
	public string? Key { get; init; }
	public AttentionSource Source { get; init; } = AttentionSource.Tool;

	/// <summary>Already resolved references. A re-post that carries some replaces the item's; one without keeps them.</summary>
	public IReadOnlyList<AttentionRefModel> Refs { get; init; } = [];
}
