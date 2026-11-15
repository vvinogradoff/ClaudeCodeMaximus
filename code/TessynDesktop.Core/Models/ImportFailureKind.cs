namespace TessynDesktop.Models;

/// <summary>Why <see cref="Services.IImportService.ImportSession"/> added nothing to the tree.</summary>
/// <remarks>Created by Claude</remarks>
public enum ImportFailureKind
{
	/// <summary>The session was imported.</summary>
	None,

	/// <summary>Neither a directory nor a group was given.</summary>
	NoTarget,

	/// <summary>The summary names no JSONL file: a session cannot be imported without one.</summary>
	NoSource,

	/// <summary>The JSONL file holds no message.</summary>
	Empty,

	/// <summary>Reading, writing or adding the session failed; the cause is in the log.</summary>
	Error,
}
