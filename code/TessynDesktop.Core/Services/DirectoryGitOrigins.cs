using System.Collections.Concurrent;
using Serilog;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IDirectoryGitOrigins" />
/// <remarks>Created by Claude</remarks>
public sealed class DirectoryGitOrigins : IDirectoryGitOrigins
{
	private static readonly ILogger _log = Log.ForContext<DirectoryGitOrigins>();

	private readonly IGitOriginScanner _scanner;
	private readonly ConcurrentDictionary<string, string?> _origins = new(StringComparer.OrdinalIgnoreCase);
	private int _refreshInFlight;

	public DirectoryGitOrigins(IGitOriginService origins)
	{
		_scanner = new GitOriginScanner(origins);
	}

	/// <inheritdoc />
	public string? GetOrigin(string directoryPath)
	{
		if (_origins.TryGetValue(directoryPath, out var cached))
			return cached;

		var origin = _scanner.Scan([directoryPath])[0].OriginUrl;
		_origins[directoryPath] = origin;
		return origin;
	}

	/// <inheritdoc />
	public Task? StartRefresh(IReadOnlyList<string> directoryPaths, Action completed)
	{
		if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0)
			return null;

		var paths = directoryPaths.ToList();
		return Task.Run(() =>
		{
			try
			{
				var results = _scanner.Scan(paths);
				for (var i = 0; i < paths.Count; i++)
					_origins[paths[i]] = results[i].OriginUrl;
				completed();
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "Git origin refresh failed");
			}
			finally
			{
				Volatile.Write(ref _refreshInFlight, 0);
			}
		});
	}
}
