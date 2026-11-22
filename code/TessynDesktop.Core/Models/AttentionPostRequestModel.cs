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

	/// <summary>The elevated run this item is the card of (FR.23.31).</summary>
	public string? ElevatedRunId { get; init; }

	/// <summary>True for the mirror in a supervisor's ledger: the created item raises no <c>ItemsCreated</c> (no second sound, FR.23.31).</summary>
	public bool IsSilent { get; init; }

	/// <summary>The text of the SYSTEM entry in place of the title and body (FR.23.8); null = the usual entry.</summary>
	public string? SystemText { get; init; }
}
