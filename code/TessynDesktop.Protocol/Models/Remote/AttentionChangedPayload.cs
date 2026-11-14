namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>attention.changed</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionChangedPayload(string NodeId, AttentionSummaryDto Summary);
