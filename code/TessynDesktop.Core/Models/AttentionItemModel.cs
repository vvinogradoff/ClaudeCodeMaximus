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

	/// <summary>The id of the elevated run this item is the card of (FR.23.31); null for every other item. Ledger files without the field read as none.</summary>
	public string? ElevatedRunId { get; set; }

	/// <summary>
	/// True while the elevated run behind this item is <c>launching</c> or <c>running</c> (FR.23.41). Derived by the ledger service from the run's live status
	/// when it hands the item out; never stored. Read it through <c>AttentionOrdering.IsInProgress</c>.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public bool IsRunInProgress { get; set; }
	/// <summary>The questionnaire of an ask or critical item (FR.23.33); empty for any other. Ledger files without the field read as none.</summary>
	public List<AttentionQuestionModel> Questions { get; set; } = [];
}
