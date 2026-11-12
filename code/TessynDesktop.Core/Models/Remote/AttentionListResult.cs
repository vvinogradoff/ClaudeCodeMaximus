using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of attention.list: the badge inputs and the ledger in <c>AttentionOrdering</c> order.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionListResult(string NodeId, AttentionSummaryDto Summary, IReadOnlyList<AttentionItemDto> Items);
