using System.IO;

namespace TessynDesktop.Models;

/// <remarks>Created by Claude</remarks>
public sealed class IndexedFileModel
{
	public required string FileName { get; init; }
	public required string RelativePath { get; init; }

	/// <summary>The indexed working directory. One string shared by every entry of an index, so it costs a reference, not a copy per file.</summary>
	public required string Root { get; init; }

	/// <summary>Computed on demand: holding a third string per file cost hundreds of MB on large trees.</summary>
	public string AbsolutePath => Path.Combine(Root, RelativePath);
}
