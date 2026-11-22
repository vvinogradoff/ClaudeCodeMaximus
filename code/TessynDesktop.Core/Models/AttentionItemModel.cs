using System;
using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>One item in a session's Attention Ledger (FR.23.2).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionItemModel
{
	public string Id { get; set; } = string.Empty;              // "A12"
	public int Number { get; set; }                             // 12
	public AttentionKind Kind { get; set; }
	public string Title { get; set; } = string.Empty;
	public string Body { get; set; } = string.Empty;
	public List<string> Options { get; set; } = [];
	public string? Recommendation { get; set; }
	public string? Key { get; set; }
	public AttentionStatus Status { get; set; } = AttentionStatus.Open;
	public AttentionSource Source { get; set; }
	public string? Answer { get; set; }
	public string? ResolutionNote { get; set; }
	public bool IsUserCloseUnreported { get; set; }
	public DateTimeOffset CreatedUtc { get; set; }
	public DateTimeOffset UpdatedUtc { get; set; }
	public DateTimeOffset? ClosedUtc { get; set; }

	/// <summary>References to entries, files and URLs, resolved when the item was posted. Ledger files without the field read as none.</summary>
	public List<AttentionRefModel> Refs { get; set; } = [];

	/// <summary>The questionnaire of an ask or critical item (FR.23.33); empty for any other. Ledger files without the field read as none.</summary>
	public List<AttentionQuestionModel> Questions { get; set; } = [];
}
