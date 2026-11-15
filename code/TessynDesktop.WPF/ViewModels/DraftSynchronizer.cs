using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>What <c>draft.get</c> returned, plus what this client had agreed on with the daemon before it (null when it knew nothing).</summary>
/// <param name="Text">The daemon's draft text.</param>
/// <param name="Revision">Its revision.</param>
/// <param name="PreviousBase">The text this client last read from or wrote to the daemon for that session.</param>
/// <remarks>Created by Claude</remarks>
public sealed record DraftLoad(string Text, string Revision, string? PreviousBase);

/// <summary>A conflicting <c>draft.set</c> was merged with the daemon's newer text; <see cref="Merged"/> is what the daemon stores now.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DraftMerged(string NodeId, string Sent, string Merged);

/// <summary>
/// Saves the unsent text of sessions through <c>draft.set</c> (debounced per session, one write in flight per session) and keeps the revision the daemon
/// last confirmed, so a write that raced another writer (-32000 "Draft changed") is merged instead of lost. Independent of the UI thread: the
/// view model hands it text snapshots, and <see cref="FlushAllAsync"/> can run at exit without it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DraftSynchronizer : IDisposable
{
	private const int MaxConflictRounds = 3;

	private readonly ITessynClient _client;
	private readonly ILogger _logger;
	private readonly Dictionary<string, Entry> _entries = new();
	private readonly object _lock = new();
	private bool _disposed;

	public DraftSynchronizer(ITessynClient client, ILogger logger, TimeSpan delay)
	{
		_client = client;
		_logger = logger;
		Delay = delay;
	}

	/// <summary>How long a session's text must stay unchanged before it is written; zero writes at once (tests).</summary>
	public TimeSpan Delay { get; set; }

	/// <summary>Raised (on a pool thread) when a conflict was merged and the merged text differs from what was sent.</summary>
	public event EventHandler<DraftMerged>? Merged;

	/// <summary>Remembers the newest text of a session and writes it after <see cref="Delay"/> of quiet.</summary>
	public void Schedule(string nodeId, string text)
	{
		var entry = EntryOf(nodeId);
		lock (_lock)
		{
			if (_disposed)
				return;

			entry.Pending = text;
		}

		ArmTimer(nodeId, entry, Delay);
	}

	/// <summary>First pause before a failed write is tried again; it doubles per failure up to <see cref="MaxRetryBackoff"/>.</summary>
	public TimeSpan RetryBackoff { get; set; } = Constants.ComposerLimits.DraftRetryBackoff;

	/// <summary>Longest pause between two tries of a failing write.</summary>
	public TimeSpan MaxRetryBackoff { get; set; } = Constants.ComposerLimits.DraftMaxRetryBackoff;

	/// <summary>Forgets the text waiting to be written: a newer text from the daemon has replaced it (M1), so writing it would undo that.</summary>
	public void Discard(string nodeId)
	{
		lock (_lock)
		{
			if (!_entries.TryGetValue(nodeId, out var entry))
				return;

			entry.Pending = null;
			entry.Timer?.Cancel();
		}
	}

	/// <summary>Replaces the entry's timer with one that flushes after <paramref name="delay"/> (at once when zero).</summary>
	private void ArmTimer(string nodeId, Entry entry, TimeSpan delay)
	{
		CancellationToken token;
		lock (_lock)
		{
			if (_disposed)
				return;

			entry.Timer?.Cancel();
			entry.Timer?.Dispose();
			entry.Timer = new CancellationTokenSource();
			token = entry.Timer.Token;
		}

		if (delay <= TimeSpan.Zero)
		{
			_ = FlushAsync(nodeId);
			return;
		}

		_ = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(delay, token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			await FlushAsync(nodeId).ConfigureAwait(false);
		});
	}

	/// <summary>True when <paramref name="revision"/> is the one this client last read or wrote (its own <c>draft.changed</c> echo).</summary>
	public bool IsKnownRevision(string nodeId, string revision)
	{
		lock (_lock)
			return _entries.TryGetValue(nodeId, out var entry) && entry.Known && entry.Revision == revision;
	}

	/// <summary>True while a session's text is waiting for its write.</summary>
	public bool HasPending(string nodeId)
	{
		lock (_lock)
			return _entries.TryGetValue(nodeId, out var entry) && entry.Pending != null;
	}

	/// <summary>Forgets what is known about a session's draft (another writer changed it and nothing here is waiting to be written).</summary>
	public void Forget(string nodeId)
	{
		lock (_lock)
		{
			if (_entries.TryGetValue(nodeId, out var entry) && entry.Pending == null)
				entry.Known = false;
		}
	}

	/// <summary>Reads the daemon's draft and records its revision and text as the agreed state.</summary>
	public async Task<DraftLoad> LoadAsync(string nodeId, CancellationToken cancellationToken = default)
	{
		// Serialised with the writes of the session (L2): an answer that was issued before a write must not return after it and put the older revision back.
		var entry = EntryOf(nodeId);
		await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			return await LoadCoreAsync(nodeId, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			entry.Gate.Release();
		}
	}

	/// <summary>The read itself, for callers that already hold the session's gate.</summary>
	private async Task<DraftLoad> LoadCoreAsync(string nodeId, CancellationToken cancellationToken = default)
	{
		var result = await _client.DraftGetAsync(nodeId, cancellationToken).ConfigureAwait(false);
		var entry = EntryOf(nodeId);
		lock (_lock)
		{
			var previous = entry.Known ? entry.BaseText : null;
			entry.Known = true;
			entry.Revision = result.Revision;
			entry.BaseText = result.Text;
			return new DraftLoad(result.Text, result.Revision, previous);
		}
	}

	/// <summary>Writes a session's pending text now (nothing when none is pending). Never throws: a failure is logged and the text stays pending.</summary>
	public async Task FlushAsync(string nodeId)
	{
		var entry = EntryOf(nodeId);
		await entry.Gate.WaitAsync().ConfigureAwait(false);
		try
		{
			string? text;
			lock (_lock)
			{
				text = entry.Pending;
				entry.Pending = null;
				entry.Timer?.Cancel();
			}

			if (text == null)
				return;

			try
			{
				await WriteAsync(nodeId, entry, text).ConfigureAwait(false);
				lock (_lock)
					entry.Failures = 0;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Saving the draft of {NodeId} failed", nodeId);
				TimeSpan? retryIn = null;
				lock (_lock)
				{
					// A newer text keeps its place (and its own timer); only the text that failed is tried again, after a growing pause (M4).
					if (entry.Pending == null)
					{
						entry.Pending = text;
						retryIn = TimeSpan.FromTicks(Math.Min(MaxRetryBackoff.Ticks, RetryBackoff.Ticks << Math.Min(entry.Failures, 20)));
					}

					entry.Failures++;
				}

				if (retryIn is { } pause)
					ArmTimer(nodeId, entry, pause);
			}
		}
		finally
		{
			entry.Gate.Release();
		}
	}

	/// <summary>Writes every session's pending text (at exit and on a lost connection's return).</summary>
	public async Task FlushAllAsync()
	{
		List<string> pending;
		lock (_lock)
			pending = _entries.Where(e => e.Value.Pending != null).Select(e => e.Key).ToList();

		foreach (var nodeId in pending)
			await FlushAsync(nodeId).ConfigureAwait(false);
	}

	private async Task WriteAsync(string nodeId, Entry entry, string text)
	{
		for (var round = 0; round < MaxConflictRounds; round++)
		{
			string? baseRevision;
			lock (_lock)
			{
				if (entry.Known && text.Length == 0 && entry.Revision == RemoteProtocol.Composer.EmptyDraftRevision)
					return;   // nothing stored, nothing to delete

				baseRevision = entry.Known ? entry.Revision : null;
			}

			if (baseRevision == null)
			{
				// Never read: the daemon may hold a draft this text knows nothing about. Read it first so it is merged, not overwritten.
				var load = await LoadCoreAsync(nodeId).ConfigureAwait(false);
				var merged = DraftMerge.Merge(text, load.Text, null);
				if (merged != text)
				{
					Merged?.Invoke(this, new DraftMerged(nodeId, text, merged));
					text = merged;
				}

				continue;
			}

			try
			{
				var result = await _client.DraftSetAsync(nodeId, text, baseRevision).ConfigureAwait(false);
				lock (_lock)
				{
					entry.Revision = result.Revision;
					entry.BaseText = text;
					entry.Known = true;
				}

				return;
			}
			catch (TessynRpcException ex) when (ex.Code == RemoteProtocol.ErrorCodes.DaemonError)
			{
				var load = await LoadCoreAsync(nodeId).ConfigureAwait(false);
				var merged = DraftMerge.Merge(text, load.Text, load.PreviousBase);
				if (merged != text)
				{
					Merged?.Invoke(this, new DraftMerged(nodeId, text, merged));
					text = merged;
				}
			}
		}

		throw new InvalidOperationException("The draft kept changing under the write.");
	}

	private Entry EntryOf(string nodeId)
	{
		lock (_lock)
		{
			if (!_entries.TryGetValue(nodeId, out var entry))
			{
				entry = new Entry();
				_entries[nodeId] = entry;
			}

			return entry;
		}
	}

	public void Dispose()
	{
		lock (_lock)
		{
			_disposed = true;
			foreach (var entry in _entries.Values)
			{
				entry.Timer?.Cancel();
				entry.Timer?.Dispose();
				entry.Timer = null;
			}
		}
	}

	private sealed class Entry
	{
		public SemaphoreSlim Gate { get; } = new(1, 1);

		public bool Known { get; set; }

		public string Revision { get; set; } = string.Empty;

		public string? BaseText { get; set; }

		public string? Pending { get; set; }

		public int Failures { get; set; }

		public CancellationTokenSource? Timer { get; set; }
	}
}
