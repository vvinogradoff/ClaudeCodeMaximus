namespace TessynDesktop.Models;

/// <summary>
/// Badge inputs of one session: the Focus badge's count and most severe kind (FR.23.13), and the same over
/// every open item, Decided included, for the badge on the session's tree and Recent-dropdown rows.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionSummaryModel
{
	public int OpenCount { get; init; }                 // open items, Decided excluded
	public AttentionKind? TopKind { get; init; }        // most severe kind among those; null when OpenCount == 0
	public int AllOpenCount { get; init; }              // every open item, Decided included
	public AttentionKind? AllOpenTopKind { get; init; } // most severe kind among those; null when AllOpenCount == 0
}
