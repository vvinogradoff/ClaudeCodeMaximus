using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>One item of a session's Attention Ledger (contract section 3.17).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionItemDto
{
	public string Id { get; init; } = string.Empty;
	public int Number { get; init; }
	public AttentionKind Kind { get; init; }
	public string Title { get; init; } = string.Empty;
	public string? Body { get; init; }
	public IReadOnlyList<string> Options { get; init; } = [];
	public string? Recommendation { get; init; }

	/// <summary>"open", "answered", "resolved" or "dismissed".</summary>
	public AttentionStatus Status { get; init; }

	public string? Answer { get; init; }
	public string? ResolutionNote { get; init; }
	public DateTimeOffset CreatedUtc { get; init; }
	public DateTimeOffset UpdatedUtc { get; init; }
	public DateTimeOffset? ClosedUtc { get; init; }
}
