using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>One item of a session's Attention Ledger (contract section 3.17).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionItemDto
{
	public AttentionItemDto()
	{
	}

	public AttentionItemDto(AttentionItemModel model)
	{
		Id             = model.Id;
		Number         = model.Number;
		Kind           = model.Kind;
		Title          = model.Title;
		Body           = string.IsNullOrEmpty(model.Body) ? null : model.Body;
		Options        = [.. model.Options];
		Recommendation = model.Recommendation;
		Status         = model.Status;
		Answer         = model.Answer;
		ResolutionNote = model.ResolutionNote;
		CreatedUtc     = model.CreatedUtc;
		UpdatedUtc     = model.UpdatedUtc;
		ClosedUtc      = model.ClosedUtc;
	}

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
