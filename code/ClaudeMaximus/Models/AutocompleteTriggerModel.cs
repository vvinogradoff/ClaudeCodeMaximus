namespace ClaudeMaximus.Models;

/// <remarks>Created by Claude</remarks>
public sealed class AutocompleteTriggerModel
{
	public required AutocompleteMode Mode { get; init; }
	public required string Query { get; init; }
	public required int TriggerStartIndex { get; init; }
	public required int TriggerLength { get; init; }
}
