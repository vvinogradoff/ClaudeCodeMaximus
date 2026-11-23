namespace TessynDesktop.Models;

/// <summary>One answer typed into an item's reply box in the Focus view (FR.23.15).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionAnswerModel
{
	public string ItemId { get; init; } = string.Empty;
	public string Title { get; init; } = string.Empty;
	public string Answer { get; init; } = string.Empty;

	/// <summary>True when <see cref="Answer"/> has no note and starts with the question lines (FR.23.36): the answers prompt then starts them on a new line.</summary>
	public bool StartsWithQuestions { get; init; }
}
