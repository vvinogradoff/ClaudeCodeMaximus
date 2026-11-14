using System;
using System.Collections.Generic;
using Serilog;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IEventLog" />
/// <remarks>Created by Claude</remarks>
public sealed class EventLog : IEventLog, IDisposable
{
	private static readonly ILogger _log = Log.ForContext<EventLog>();

	private readonly IRemoteEventBus _bus;
	private readonly TimeProvider    _time;
	private readonly int             _capacity;
	private readonly TimeSpan        _maxAge;
	private readonly Action<RemoteNotification> _handler;

	// Guards the ring, the head and the order of Appended: stamping and raising happen under one lock so subscribers see seq order.
	private readonly object _gate = new();
	private readonly LinkedList<(RemoteNotification Notification, DateTimeOffset At)> _ring = new();
	private long _head;

	public EventLog(IRemoteEventBus bus, TimeProvider? time = null, int capacity = Constants.Remote.EventLog.DefaultCapacity, TimeSpan? maxAge = null)
	{
		_bus      = bus;
		_time     = time ?? TimeProvider.System;
		_capacity = Math.Max(1, capacity);
		_maxAge   = maxAge ?? Constants.Remote.EventLog.DefaultMaxAge;
		_handler  = OnPublished;
		_bus.Published += _handler;
	}

	/// <inheritdoc />
	public long HeadSeq
	{
		get { lock (_gate) return _head; }
	}

	/// <inheritdoc />
	public event Action<RemoteNotification>? Appended;

	/// <inheritdoc />
	public EventReplay ReadSince(long lastSeq)
	{
		lock (_gate)
		{
			Trim(_time.GetUtcNow());

			if (lastSeq > _head)
				return new EventReplay([], Constants.Remote.EventLog.ResyncReasons.UnknownSeq, _head);

			if (lastSeq == _head)
				return new EventReplay([], null, _head);

			// The first event the client needs is lastSeq + 1; the ring must still start at or before it.
			var oldest = _ring.First?.Value.Notification.Seq ?? _head + 1;
			if (oldest > lastSeq + 1)
				return new EventReplay([], Constants.Remote.EventLog.ResyncReasons.Gap, _head);

			var events = new List<RemoteNotification>();
			foreach (var (notification, _) in _ring)
			{
				if (notification.Seq > lastSeq)
					events.Add(notification);
			}

			return new EventReplay(events, null, _head);
		}
	}

	/// <summary>Detaches from the bus.</summary>
	public void Dispose() => _bus.Published -= _handler;

	private void OnPublished(RemoteNotification notification)
	{
		try
		{
			lock (_gate)
			{
				var now = _time.GetUtcNow();
				var stamped = notification with { Seq = ++_head };
				_ring.AddLast((stamped, now));
				Trim(now);
				RaiseAppended(stamped);
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "EventLog: could not log {Method}", notification.Method);
		}
	}

	private void RaiseAppended(RemoteNotification stamped)
	{
		var handlers = Appended;
		if (handlers == null)
			return;

		foreach (var handler in handlers.GetInvocationList())
		{
			try
			{
				((Action<RemoteNotification>)handler)(stamped);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "EventLog: a subscriber of {Method} threw", stamped.Method);
			}
		}
	}

	private void Trim(DateTimeOffset now)
	{
		while (_ring.Count > _capacity)
			_ring.RemoveFirst();

		while (_ring.First is { } first && now - first.Value.At > _maxAge)
			_ring.RemoveFirst();
	}
}
