using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class SessionIndexUpdater : ISessionIndexUpdater
{
	private readonly ISessionFileService _fileService;
	private readonly ISessionRegistry _registry;
	private readonly IClaudeSessionIndexService _indexService;
	private readonly TimeSpan _debounce;
	private readonly ILogger _log = Log.ForContext<SessionIndexUpdater>();
	private readonly Dictionary<string, CancellationTokenSource> _pending = new();
	private readonly object _gate = new();
	private bool _started;

	public SessionIndexUpdater(
		ISessionFileService fileService,
		ISessionRegistry registry,
		IClaudeSessionIndexService indexService,
		TimeSpan? debounce = null)
	{
		_fileService = fileService;
		_registry = registry;
		_indexService = indexService;
		_debounce = debounce ?? TimeSpan.FromSeconds(Constants.ClaudeSessions.LiveIndexDebounceSeconds);
	}

	public void Start()
	{
		lock (_gate)
		{
			if (_started)
				return;
			_started = true;
			_fileService.TurnCompleted += OnTurnCompleted;
		}
	}

	public void Stop()
	{
		lock (_gate)
		{
			if (!_started)
				return;
			_started = false;
			_fileService.TurnCompleted -= OnTurnCompleted;
			foreach (var cts in _pending.Values)
				cts.Cancel();
			_pending.Clear();
		}
	}

	private void OnTurnCompleted(object? sender, SessionTurnCompletedEventArgs e)
	{
		var model = _registry.FindModelByFileName(e.FileName);
		if (model == null)
			return;

		var key = model.ExternalId ?? model.ClaudeSessionId;
		if (string.IsNullOrEmpty(key))
			return;

		// The JSONL lives under the original project's slug for cross-project imports.
		var directory = model.OriginalProjectPath ?? model.WorkingDirectory;
		var cts = new CancellationTokenSource();
		lock (_gate)
		{
			if (!_started)
				return;
			if (_pending.TryGetValue(key, out var previous))
				previous.Cancel();
			_pending[key] = cts;
		}

		_ = IndexAfterDelayAsync(key, directory, cts);
	}

	private async Task IndexAfterDelayAsync(string key, string directory, CancellationTokenSource cts)
	{
		try
		{
			await Task.Delay(_debounce, cts.Token).ConfigureAwait(false);
			lock (_gate)
			{
				// A newer event (or Stop) replaced this registration while the delay was elapsing.
				if (cts.IsCancellationRequested || !_pending.TryGetValue(key, out var current) || current != cts)
					return;
				_pending.Remove(key);
			}
			await _indexService.IndexSessionAsync(key, directory, cts.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) { }
		catch (Exception ex)
		{
			_log.Warning(ex, "Live indexing of session {SessionKey} failed", key);
		}
		finally { cts.Dispose(); }
	}
}
