namespace TessynDesktop.Models;

/// <summary>One answer typed into an item's reply box in the Focus view (FR.23.15).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttentionAnswerModel
{
	public string ItemId { get; init; } = string.Empty;
	public string Title { get; init; } = string.Empty;
	public string Answer { get; init; } = string.Empty;
}
