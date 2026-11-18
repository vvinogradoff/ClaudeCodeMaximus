namespace TessynDesktop.WPF.Services;

/// <summary>The valid patterns of a pattern text and the tokens that are not patterns.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record PreviewPatternParseResult(IReadOnlyList<PreviewPattern> Patterns, IReadOnlyList<string> Invalid);
