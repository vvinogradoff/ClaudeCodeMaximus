namespace TessynDesktop.Models.Remote;

/// <summary>Attention badge inputs of a session: the Focus badge (Decided excluded) and the row badge (Decided included).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionSummaryDto
{
	public AttentionSummaryDto()
	{
	}

	public AttentionSummaryDto(AttentionSummaryModel model)
	{
		OpenCount      = model.OpenCount;
		TopKind        = model.TopKind;
		AllOpenCount   = model.AllOpenCount;
		AllOpenTopKind = model.AllOpenTopKind;
	}

	/// <summary>Open items, Decided excluded (FR.23.13).</summary>
	public int OpenCount { get; init; }

	/// <summary>Most severe kind among those; null when <see cref="OpenCount"/> is 0.</summary>
	public AttentionKind? TopKind { get; init; }

	/// <summary>Every open item, Decided included.</summary>
	public int AllOpenCount { get; init; }

	/// <summary>Most severe kind among those; null when <see cref="AllOpenCount"/> is 0.</summary>
	public AttentionKind? AllOpenTopKind { get; init; }
}
