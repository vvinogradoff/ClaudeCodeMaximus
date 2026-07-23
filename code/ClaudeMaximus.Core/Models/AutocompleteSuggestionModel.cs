namespace ClaudeMaximus.Models;

/// <remarks>Created by Claude</remarks>
public sealed class AutocompleteSuggestionModel
{
	public required string DisplayText { get; init; }
	public required string InsertText { get; init; }
	public required string SecondaryText { get; init; }
	public CodeSymbolKind? Kind { get; init; }
	public int MatchStart { get; init; }
	public int MatchLength { get; init; }
	public bool IsFileSuggestion { get; init; }
}
