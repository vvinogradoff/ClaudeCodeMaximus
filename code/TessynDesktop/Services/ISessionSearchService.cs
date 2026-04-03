using System.Collections.Generic;

namespace TessynDesktop.Services;

/// <summary>
/// Performs full-text search across session files.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionSearchService
{
	/// <summary>
	/// Returns the set of session file names whose content contains the query string.
	/// Search is case-insensitive.
	/// </summary>
	IReadOnlySet<string> FindMatchingSessionFiles(string query);
}
