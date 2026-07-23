namespace ClaudeMaximus.Models;

/// <remarks>Created by Claude</remarks>
public sealed class IndexedFileModel
{
	public required string FileName { get; init; }
	public required string RelativePath { get; init; }
	public required string AbsolutePath { get; init; }
}
