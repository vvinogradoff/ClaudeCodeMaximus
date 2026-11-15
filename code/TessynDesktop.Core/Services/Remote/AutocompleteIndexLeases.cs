using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Holds the code indexes that <c>autocomplete.suggest</c> had to ask for (ISSUE.016). The first request for a directory whose index is not
/// ready starts the build in the background and is answered at once with "not ready"; the reference is kept while requests keep coming and
/// given back after <see cref="Constants.Composer.AutocompleteIndexIdle"/>, so a client that stops typing does not keep an index of a huge tree alive.
/// An index another holder (a desktop session) already has ready is used without a lease of ours.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AutocompleteIndexLeases : IDisposable
{
	private static readonly ILogger _log = Log.ForContext<AutocompleteIndexLeases>();

	private readonly ICodeIndexService _index;
	private readonly TimeProvider _time;
	private readonly ITimer _sweeper;
	private readonly object _gate = new();
	private readonly Dictionary<string, Lease> _leases = new(StringComparer.OrdinalIgnoreCase);

	private sealed class Lease
	{
		public DateTimeOffset LastUsed;
		public bool Acquired;
	}

	public AutocompleteIndexLeases(ICodeIndexService index, TimeProvider? time = null)
	{
		_index   = index;
		_time    = time ?? TimeProvider.System;
		_sweeper = _time.CreateTimer(_ => ReleaseIdle(), null, Constants.Composer.AutocompleteLeaseSweepInterval, Constants.Composer.AutocompleteLeaseSweepInterval);
	}

	/// <summary>
	/// Notes a request for <paramref name="workingDirectory"/> and returns whether its index is ready. Never waits for a build: a missing index
	/// is started in the background and the caller answers "not ready".
	/// </summary>
	public bool Touch(string workingDirectory)
	{
		if (string.IsNullOrWhiteSpace(workingDirectory))
			return false;

		var start = false;
		lock (_gate)
		{
			if (_leases.TryGetValue(workingDirectory, out var lease))
				lease.LastUsed = _time.GetUtcNow();
			else if (!_index.IsIndexReady(workingDirectory))
			{
				_leases[workingDirectory] = new Lease { LastUsed = _time.GetUtcNow() };
				start = true;
			}
		}

		if (start)
			_ = Task.Run(() => AcquireAsync(workingDirectory));

		return _index.IsIndexReady(workingDirectory);
	}

	private async Task AcquireAsync(string workingDirectory)
	{
		var acquired = false;
		try
		{
			acquired = await _index.GetOrCreateIndexAsync(workingDirectory);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Autocomplete could not build the code index of {Directory}", workingDirectory);
		}

		lock (_gate)
		{
			if (acquired && _leases.TryGetValue(workingDirectory, out var lease))
				lease.Acquired = true;
			else if (acquired)
				_index.ReleaseIndex(workingDirectory);
			else
				_leases.Remove(workingDirectory);
		}
	}

	/// <summary>Gives back the leases nobody asked for within the idle time (also called by the timer).</summary>
	public void ReleaseIdle()
	{
		var now = _time.GetUtcNow();
		lock (_gate)
		{
			foreach (var (directory, lease) in new List<KeyValuePair<string, Lease>>(_leases))
			{
				if (!lease.Acquired || now - lease.LastUsed < Constants.Composer.AutocompleteIndexIdle)
					continue;

				_leases.Remove(directory);
				_index.ReleaseIndex(directory);
				_log.Information("Autocomplete released the code index of {Directory} after it was idle", directory);
			}
		}
	}

	public void Dispose()
	{
		_sweeper.Dispose();
		lock (_gate)
		{
			foreach (var (directory, lease) in _leases)
			{
				if (lease.Acquired)
					_index.ReleaseIndex(directory);
			}

			_leases.Clear();
		}
	}
}
