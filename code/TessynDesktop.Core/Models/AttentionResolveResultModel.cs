namespace TessynDesktop.Models;

/// <summary>Result of a session-side close (FR.23.5). <see cref="Item"/> is null only for <see cref="AttentionResolveOutcome.Unknown"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionResolveResultModel
{
	public AttentionResolveOutcome Outcome { get; init; }
	public AttentionItemModel? Item { get; init; }
}
