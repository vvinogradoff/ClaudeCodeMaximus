using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Serilog;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IRemoteEventBus" />
/// <remarks>Created by Claude</remarks>
public sealed class RemoteEventBus : IRemoteEventBus, IDisposable
{
	private static readonly ILogger _log = Log.ForContext<RemoteEventBus>();

	private readonly object _gate = new();
	private readonly Dictionary<string, PendingPublish> _pending = new(StringComparer.Ordinal);
	private bool _disposed;

	/// <inheritdoc />
	public event Action<RemoteNotification>? Published;

	/// <inheritdoc />
	public void Publish(string method, object payload)
	{
		try
		{
			var node = JsonSerializer.SerializeToNode(payload, payload.GetType(), TdcpJsonOptions.Default);
			if (node == null)
				return;

			Raise(new RemoteNotification(method, node));
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "RemoteEventBus: could not publish {Method}", method);
		}
	}

	/// <inheritdoc />
	public void PublishCoalesced(string method, string key, Func<object?> payloadFactory, TimeSpan window)
	{
		try
		{
			var pendingKey = method + "\n" + key;
			lock (_gate)
			{
				if (_disposed || _pending.ContainsKey(pendingKey))
					return;

				var pending = new PendingPublish(pendingKey, method, payloadFactory);
				pending.Timer = new Timer(OnTimer, pending, Timeout.Infinite, Timeout.Infinite);
				_pending[pendingKey] = pending;
				pending.Timer.Change(window, Timeout.InfiniteTimeSpan);
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "RemoteEventBus: could not schedule {Method}", method);
		}
	}

	/// <summary>Cancels every pending publication; later calls do nothing.</summary>
	public void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
			foreach (var pending in _pending.Values)
				pending.Timer?.Dispose();
			_pending.Clear();
		}
	}

	private void OnTimer(object? state)
	{
		var pending = (PendingPublish)state!;
		lock (_gate)
		{
			// Removed before the factory runs: a call arriving while it runs opens the next window.
			if (!_pending.Remove(pending.Key))
				return;
		}

		pending.Timer?.Dispose();

		try
		{
			var payload = pending.PayloadFactory();
			if (payload != null)
				Publish(pending.Method, payload);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "RemoteEventBus: payload factory for {Method} failed", pending.Method);
		}
	}

	private void Raise(RemoteNotification notification)
	{
		var handlers = Published;
		if (handlers == null)
			return;

		foreach (var handler in handlers.GetInvocationList())
		{
			try
			{
				((Action<RemoteNotification>)handler)(notification);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "RemoteEventBus: a subscriber of {Method} threw", notification.Method);
			}
		}
	}

	private sealed class PendingPublish(string key, string method, Func<object?> payloadFactory)
	{
		public string Key { get; } = key;
		public string Method { get; } = method;
		public Func<object?> PayloadFactory { get; } = payloadFactory;
		public Timer? Timer { get; set; }
	}
}
