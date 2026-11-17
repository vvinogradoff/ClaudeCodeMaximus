using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionResumabilityCache" />
/// <remarks>Created by Claude</remarks>
public sealed class SessionResumabilityCache : ISessionResumabilityCache
{
	private readonly IRemoteTreeReader _reader;
	private readonly IClaudeSessionStatusService _status;
	private readonly TimeProvider _time;
	private readonly ILogger _log;
	private readonly TimeSpan _interval;
	private readonly object _sweepGate = new();
	private readonly CancellationTokenSource _cts = new();

	// Replaced as a whole after a sweep, so readers never see a half-built map.
	private volatile Dictionary<string, bool>? _flags;
	private Task? _loop;

	public SessionResumabilityCache(IRemoteTreeReader reader, IClaudeSessionStatusService status, TimeProvider? time = null, ILogger? log = null, TimeSpan? interval = null)
	{
		_reader   = reader;
		_status   = status;
		_time     = time ?? TimeProvider.System;
		_log      = log ?? Log.ForContext<SessionResumabilityCache>();
		_interval = interval ?? TimeSpan.FromSeconds(Constants.ClaudeSessions.StatusCheckIntervalSeconds);
	}

	/// <inheritdoc />
	public event Action<IReadOnlyList<string>>? Changed;

	/// <inheritdoc />
	public bool? TryGet(string nodeId) => _flags is { } flags && flags.TryGetValue(nodeId, out var value) ? value : null;

	/// <inheritdoc />
	public IReadOnlyList<string> Refresh()
	{
		List<string> changed;
		lock (_sweepGate)
		{
			var sessions = _reader.Snapshot().AllSessions;
			var requests = new List<SessionResumabilityRequest>(sessions.Count);
			foreach (var session in sessions)
				requests.Add(new SessionResumabilityRequest(session.WorkingDirectory, session.Node.ClaudeSessionId));   // a reference read, as the state builder reads the live node

			var results = _status.AreSessionsResumable(requests);
			var next    = new Dictionary<string, bool>(sessions.Count, StringComparer.Ordinal);
			for (var i = 0; i < sessions.Count; i++)
				next[sessions[i].NodeId] = results[i];

			var previous = _flags;
			changed = [];
			if (previous != null)
			{
				foreach (var (nodeId, flag) in next)
				{
					if (previous.TryGetValue(nodeId, out var before) && before != flag)
						changed.Add(nodeId);
				}
			}

			_flags = next;
		}

		if (changed.Count > 0)
		{
			try
			{
				Changed?.Invoke(changed);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "A resumability change subscriber failed");
			}
		}

		return changed;
	}

	/// <inheritdoc />
	public void Start()
	{
		if (_loop != null)
			return;

		_loop = Task.Factory.StartNew(() => RunAsync(_cts.Token), _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
	}

	private async Task RunAsync(CancellationToken token)
	{
		using var timer = new PeriodicTimer(_interval, _time);
		try
		{
			do
			{
				try
				{
					var started = _time.GetTimestamp();
					var changed = Refresh();
					_log.Information("Resumability sweep: {Changed} changed, {ElapsedMs} ms", changed.Count, _time.GetElapsedTime(started).TotalMilliseconds);
				}
				catch (Exception ex)
				{
					_log.Warning(ex, "Resumability sweep failed");
				}
			}
			while (await timer.WaitForNextTickAsync(token));
		}
		catch (OperationCanceledException)
		{
			// shutting down
		}
	}

	/// <inheritdoc />
	public void Dispose() => _cts.Cancel();
}
