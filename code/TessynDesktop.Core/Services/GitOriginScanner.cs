using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class GitOriginScanner : IGitOriginScanner
{
	private readonly IGitOriginService _origins;

	public GitOriginScanner(IGitOriginService origins) => _origins = origins;

	public IReadOnlyList<DirectoryGitOriginModel> Scan(IReadOnlyList<string> directoryPaths)
	{
		var results = new List<DirectoryGitOriginModel>(directoryPaths.Count);
		foreach (var path in directoryPaths)
			results.Add(new DirectoryGitOriginModel(path, TryGetOrigin(path)));
		return results;
	}

	private string? TryGetOrigin(string path)
	{
		try
		{
			return _origins.GetOriginUrl(path);
		}
		catch
		{
			// Unresolvable path (invalid characters, IO error): no origin, same as a non-repo directory.
			return null;
		}
	}
}
