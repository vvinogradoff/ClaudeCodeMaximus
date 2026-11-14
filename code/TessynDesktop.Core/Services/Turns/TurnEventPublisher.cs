using System;
using System.Collections.Concurrent;
using System.Threading;
using Serilog;
using TessynDesktop.Models.Remote;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <summary>
/// Publishes the progress of turns on the remote event bus as <c>turn.started</c>, <c>turn.delta</c>, <c>turn.tool</c>,
/// <c>turn.status</c> and <c>turn.usage</c> (P1.I). <c>turn.started</c> goes out at once; the rest is batched for
/// <see cref="Constants.Turns.Timings.BatchWindow"/> per turn (not coalesced: every tool, status and usage notification survives, consecutive
/// deltas merge) and kept in a bounded buffer that drops the oldest deltas first. The end of a turn flushes its buffer, so nothing of the turn
/// is published after its <c>turn.completed</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnEventPublisher : ITurnProgressSink, IDisposable
{
	private static readonly ILogger _log = Log.ForContext<TurnEventPublisher>();

	private readonly IRemoteEventBus _bus;
	private readonly TimeSpan _window;
	private readonly int _maxItems;
	private readonly int _maxDeltaChars;
	private readonly ConcurrentDictionary<string, TurnChannel> _turns = new(StringComparer.Ordinal);
	private bool _disposed;

	public TurnEventPublisher(IRemoteEventBus bus, TimeSpan? batchWindow = null, int? maxBufferedItems = null, int? maxBufferedDeltaChars = null)
	{
		_bus           = bus;
		_window        = batchWindow ?? Constants.Turns.Timings.BatchWindow;
		_maxItems      = maxBufferedItems ?? Constants.Turns.Limits.BufferMaxItems;
		_maxDeltaChars = maxBufferedDeltaChars ?? Constants.Turns.Limits.BufferMaxDeltaChars;
	}

	/// <inheritdoc />
	public void Publish(TurnProgress progress)
	{
		if (_disposed)
			return;

		try
		{
			switch (progress)
			{
				case TurnStartedProgress started:
					Channel(started);
					_bus.Publish(RemoteProtocol.TurnStream.StartedEvent,
						new TurnStartedPayload(started.NodeId, started.TurnId, started.ClientRequestId));
					break;

				case TurnDeltaProgress delta:
					Channel(delta).AddDelta(delta.Kind, delta.Text);
					break;

				case TurnToolProgress tool:
					Channel(tool).AddOther(RemoteProtocol.TurnStream.ToolEvent,
						new TurnToolPayload(tool.NodeId, tool.TurnId, tool.ToolUseId, tool.Name, tool.Status, tool.Summary, tool.IsError));
					break;

				case TurnStatusProgress status:
					Channel(status).AddOther(RemoteProtocol.TurnStream.StatusEvent,
						new TurnStatusPayload(status.NodeId, status.TurnId, status.State, status.Detail));
					break;

				case TurnUsageProgress usage:
					Channel(usage).AddOther(RemoteProtocol.TurnStream.UsageEvent,
						new TurnUsagePayload(usage.NodeId, usage.TurnId, usage.InputTokens, usage.OutputTokens, usage.CostUsd));
					break;

				case TurnFinishedProgress finished:
					if (_turns.TryRemove(finished.TurnId, out var channel))
						channel.Close();

					// A turn that never ran gets no TurnCompleted from the file service: say so here, after its last notification.
					if (!finished.Ran)
						_bus.Publish(RemoteProtocol.Events.TurnCompleted,
							new TurnCompletedPayload(finished.NodeId, finished.Cancelled, finished.Error, finished.TurnId));
					break;
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "TurnEventPublisher: could not publish progress of turn {TurnId}", progress.TurnId);
		}
	}

	public void Dispose()
	{
		_disposed = true;
		foreach (var channel in _turns.Values)
			channel.Close();
		_turns.Clear();
	}

	private TurnChannel Channel(TurnProgress progress) =>
		_turns.GetOrAdd(progress.TurnId, _ => new TurnChannel(_bus, progress.NodeId, progress.TurnId, _window, _maxItems, _maxDeltaChars));

	/// <summary>One turn's buffer, flush timer and the lock that keeps its notifications in order.</summary>
	private sealed class TurnChannel
	{
		private readonly IRemoteEventBus _bus;
		private readonly TimeSpan _window;
		private readonly TurnEventBuffer _buffer;
		private readonly object _gate = new();
		private readonly object _publishGate = new();
		private readonly Timer _timer;
		private bool _armed;
		private bool _closed;

		public TurnChannel(IRemoteEventBus bus, string nodeId, string turnId, TimeSpan window, int maxItems, int maxDeltaChars)
		{
			_bus    = bus;
			_window = window;
			_buffer = new TurnEventBuffer(nodeId, turnId, maxItems, maxDeltaChars);
			_timer  = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
		}

		public void AddDelta(TurnDeltaKind kind, string text)
		{
			lock (_gate)
			{
				if (_closed)
					return;

				_buffer.AddDelta(kind, text);
				Arm();
			}
		}

		public void AddOther(string method, object payload)
		{
			lock (_gate)
			{
				if (_closed)
					return;

				_buffer.AddOther(method, payload);
				Arm();
			}
		}

		/// <summary>Publishes what is buffered now, in order; concurrent flushes queue up so batches never interleave.</summary>
		public void Flush()
		{
			lock (_publishGate)
			{
				List<(string Method, object Payload)> batch;
				lock (_gate)
				{
					_armed = false;
					batch  = _buffer.Drain();
				}

				foreach (var (method, payload) in batch)
					_bus.Publish(method, payload);
			}
		}

		/// <summary>Flushes a last time and stops the timer.</summary>
		public void Close()
		{
			lock (_gate) _closed = true;
			Flush();
			_timer.Dispose();
		}

		private void Arm()
		{
			if (_armed)
				return;

			_armed = true;
			_timer.Change(_window, Timeout.InfiniteTimeSpan);
		}
	}
}
