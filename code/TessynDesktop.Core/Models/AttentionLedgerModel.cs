using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>A session's Attention Ledger as persisted in its sidecar JSON file (FR.23.3).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionLedgerModel
{
	public string NodeId { get; set; } = string.Empty;
	public int NextNumber { get; set; } = 1;
	public List<AttentionItemModel> Items { get; set; } = [];
}
