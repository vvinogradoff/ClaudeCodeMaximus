using System;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Codex.Events;

namespace TessynDesktop.Services;

public sealed partial class CodexSessionEngine
{
	/// <summary>
	/// Listens to one turn on one thread of a host for as long as it is not disposed: maps each notification with a <see cref="CodexTurnMapper"/>
	/// and hands the events on, in stream order, on the host's read loop. <see cref="Finished"/> completes with <c>turn/completed</c> or with a
	/// crash of the host. It is attached before the request that starts the turn, so no early notification is missed; before the turn id is
	/// known the mapper takes it from the thread's first <c>turn/started</c> (a compaction turn).
	/// </summary>
	/// <remarks>Created by Claude</remarks>
	private sealed class TurnListener : IDisposable
	{
		private readonly CodexAppServerHost _host;
		private readonly CodexTurnMapper _mapper;
		private readonly Action<CodexTurnEvent> _dispatch;
		private readonly TaskCompletionSource<TurnFinish> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TurnListener(CodexAppServerHost host, string threadId, Action<CodexTurnEvent> dispatch)
		{
			_host     = host;
			_mapper   = new CodexTurnMapper(threadId);
			_dispatch = dispatch;
			_host.NotificationReceived += OnNotification;
			_host.Crashed              += OnCrashed;
			_host.Stopped              += OnStopped;
		}

		public Task<TurnFinish> Finished => _done.Task;

		public string? TurnId
		{
			get { lock (_mapper) return _mapper.TurnId; }
		}

		public void SetTurnId(string turnId)
		{
			lock (_mapper)
				_mapper.SetTurnId(turnId);
		}

		public void Dispose()
		{
			_host.NotificationReceived -= OnNotification;
			_host.Crashed              -= OnCrashed;
			_host.Stopped              -= OnStopped;
		}

		private void OnNotification(CodexNotification notification)
		{
			System.Collections.Generic.IReadOnlyList<CodexTurnEvent> events;
			lock (_mapper)
				events = _mapper.Map(notification);

			foreach (var turnEvent in events)
			{
				try
				{
					_dispatch(turnEvent);
				}
				catch (Exception ex)
				{
					_log.Error(ex, "Handling Codex event {Event} failed", turnEvent.GetType().Name);
				}

				if (turnEvent is CodexTurnCompletedEvent completed)
					_done.TrySetResult(new TurnFinish(completed, null, null));
			}
		}

		private void OnCrashed(CodexHostCrashInfo crash) => _done.TrySetResult(new TurnFinish(null, crash, null));

		/// <summary>The host is stopped on purpose (a profile delete, Terminate all): no turn/completed will come, so the turn ends now.</summary>
		private void OnStopped(CodexHostStopReason reason) => _done.TrySetResult(new TurnFinish(null, null, reason));
	}

	/// <summary>How a listened turn ended: its <c>turn/completed</c>, the crash of its host, or a deliberate stop of its host.</summary>
	private sealed record TurnFinish(CodexTurnCompletedEvent? Completion, CodexHostCrashInfo? Crash, CodexHostStopReason? Stopped);
}
