using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IClientPresenceRegistry" />
/// <remarks>Created by Claude</remarks>
public sealed class ClientPresenceRegistry : IClientPresenceRegistry
{
	private sealed class Entry
	{
		public HashSet<string> Presents = [];
		public int OpenStreams;
		public DateTimeOffset LastTouched;
	}

	private readonly TimeProvider _time;
	private readonly object _gate = new();
	private readonly Dictionary<string, Entry> _clients = new(StringComparer.Ordinal);

	public ClientPresenceRegistry(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

	/// <inheritdoc />
	public event Action? Changed;

	/// <inheritdoc />
	public void Register(string clientId, IReadOnlyCollection<string> presents)
	{
		lock (_gate)
		{
			// A registration with no stream that nobody refreshed is forgotten, and the table is bounded (V7).
			DropIdle();
			if (presents.Count > 0 && !_clients.ContainsKey(clientId) && _clients.Count >= Constants.Composer.MaxPresenceClients)
				throw new InvalidOperationException($"At most {Constants.Composer.MaxPresenceClients} clients can be registered.");

			var entry = Get(clientId);
			entry.Presents = [.. presents];
			entry.LastTouched = _time.GetUtcNow();
			Prune(clientId, entry);
		}

		Changed?.Invoke();
	}

	/// <inheritdoc />
	public IDisposable OpenStream(string? clientId)
	{
		if (string.IsNullOrWhiteSpace(clientId))
			return NullHandle.Instance;

		lock (_gate)
		{
			var entry = Get(clientId);
			entry.OpenStreams++;
			entry.LastTouched = _time.GetUtcNow();
		}

		Changed?.Invoke();
		return new StreamHandle(this, clientId);
	}

	/// <inheritdoc />
	public bool IsPresented(string channel)
	{
		lock (_gate)
			return _clients.Values.Any(e => e.OpenStreams > 0 && e.Presents.Contains(channel));
	}

	private void DropIdle()
	{
		var now = _time.GetUtcNow();
		foreach (var (id, entry) in _clients.ToList())
		{
			if (entry.OpenStreams == 0 && now - entry.LastTouched > Constants.Composer.PresenceIdleTtl)
				_clients.Remove(id);
		}
	}

	private Entry Get(string clientId)
	{
		if (!_clients.TryGetValue(clientId, out var entry))
			_clients[clientId] = entry = new Entry();
		return entry;
	}

	// A client with nothing registered and no stream is forgotten, so ids of finished clients do not pile up.
	private void Prune(string clientId, Entry entry)
	{
		if (entry.Presents.Count == 0 && entry.OpenStreams == 0)
			_clients.Remove(clientId);
	}

	private void CloseStream(string clientId)
	{
		lock (_gate)
		{
			if (_clients.TryGetValue(clientId, out var entry))
			{
				entry.OpenStreams = Math.Max(0, entry.OpenStreams - 1);
				entry.LastTouched = _time.GetUtcNow();
				Prune(clientId, entry);
			}
		}

		Changed?.Invoke();
	}

	private sealed class StreamHandle(ClientPresenceRegistry owner, string clientId) : IDisposable
	{
		private int _disposed;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
				owner.CloseStream(clientId);
		}
	}

	private sealed class NullHandle : IDisposable
	{
		public static readonly NullHandle Instance = new();
		public void Dispose() { }
	}
}
