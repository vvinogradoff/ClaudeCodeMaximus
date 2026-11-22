using System;
using System.Threading;
using System.Threading.Tasks;

namespace TessynDesktop.Tests.Codex.Fakes;

internal sealed partial class ManualTimerTimeProvider
{
	/// <summary>A timer of <see cref="ManualTimerTimeProvider"/>; it fires only from <see cref="Advance"/>.</summary>
	private sealed class ManualTimer : ITimer
	{
		private readonly ManualTimerTimeProvider _owner;
		private readonly TimerCallback           _callback;
		private readonly object?                 _state;
		private TimeSpan                         _period = Timeout.InfiniteTimeSpan;

		public ManualTimer(ManualTimerTimeProvider owner, TimerCallback callback, object? state)
		{
			_owner    = owner;
			_callback = callback;
			_state    = state;
		}

		/// <summary>When it fires next; null when disarmed. Read and written under the owner's lock.</summary>
		public DateTimeOffset? DueAt { get; private set; }

		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			lock (_owner._lock)
			{
				_period = period;
				DueAt   = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._now + dueTime;
			}
			return true;
		}

		/// <summary>Called under the owner's lock when it falls due: re-arms a periodic timer, disarms a one-shot one.</summary>
		public void Fired() =>
			DueAt = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : DueAt + _period;

		public void Invoke() => _callback(_state);

		public void Dispose()
		{
			lock (_owner._lock)
				DueAt = null;
			_owner.Remove(this);
		}

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
