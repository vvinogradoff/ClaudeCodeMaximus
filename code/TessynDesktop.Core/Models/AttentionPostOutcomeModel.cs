namespace TessynDesktop.Models;

/// <summary>The result for one posted item: a snapshot of it and whether it was created or updated in place (FR.23.4).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionPostOutcomeModel
{
	public AttentionItemModel Item { get; init; } = new();

	/// <summary>True when a new item was created; false when an open item matched and was updated.</summary>
	public bool IsCreated { get; init; }
}
