using System.Collections.Concurrent;
using Serilog;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IDirectoryGitOrigins" />
/// <remarks>Created by Claude</remarks>
public sealed class DirectoryGitOrigins : IDirectoryGitOrigins
{
	private static readonly ILogger _log = Log.ForContext<DirectoryGitOrigins>();

	private readonly IGitOriginScanner _scanner;
	private readonly ConcurrentDictionary<string, (string? Origin, DateTimeOffset At)> _origins = new(StringComparer.OrdinalIgnoreCase);
	private readonly TimeProvider _time;
	private int _refreshInFlight;

	public DirectoryGitOrigins(IGitOriginService origins, TimeProvider? time = null)
	{
		_time    = time ?? TimeProvider.System;
		_scanner = new GitOriginScanner(origins);
	}

	/// <inheritdoc />
	public string? GetOrigin(string directoryPath)
	{
		// A found origin stays until a refresh. "No origin" is read again after a while: a repository cloned into the folder since would otherwise
		// count as "not a repository" (the move rule lets such a session go anywhere) until someone pressed refresh.
		if (_origins.TryGetValue(directoryPath, out var cached)
		    && (cached.Origin != null || _time.GetUtcNow() - cached.At < TimeSpan.FromMinutes(Constants.TreeMutation.NoOriginCacheMinutes)))
			return cached.Origin;

		var origin = _scanner.Scan([directoryPath])[0].OriginUrl;
		_origins[directoryPath] = (origin, _time.GetUtcNow());
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
					_origins[paths[i]] = (results[i].OriginUrl, _time.GetUtcNow());
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
