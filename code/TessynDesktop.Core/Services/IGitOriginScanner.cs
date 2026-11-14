using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>Batch resolver of git origin URLs for directory paths. Thread-safe; touches no UI state.</summary>
/// <remarks>Created by Claude</remarks>
public interface IGitOriginScanner
{
	/// <summary>Returns one result per input path, in input order. A path that cannot be resolved yields a null origin.</summary>
	IReadOnlyList<DirectoryGitOriginModel> Scan(IReadOnlyList<string> directoryPaths);
}
