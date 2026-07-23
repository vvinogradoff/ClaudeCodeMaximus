namespace ClaudeMaximus.Models;

/// <remarks>Created by Claude</remarks>
public sealed class CodeSymbolModel
{
	public required string Name { get; init; }
	public required string FullyQualifiedName { get; init; }
	public required string Namespace { get; init; }
	public required CodeSymbolKind Kind { get; init; }
	public required string FilePath { get; init; }
	public required int Line { get; init; }
}
