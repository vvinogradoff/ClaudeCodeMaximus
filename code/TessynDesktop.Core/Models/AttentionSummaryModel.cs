namespace TessynDesktop.Models;

/// <summary>The Focus badge's inputs (FR.23.13): how many open items count, and the most severe kind among them.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionSummaryModel
{
	public int OpenCount { get; init; }              // open items, Decided excluded
	public AttentionKind? TopKind { get; init; }     // most severe kind among those; null when OpenCount == 0
}
