namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>A <see cref="TimeProvider"/> whose timers fire only when the test calls <see cref="Advance"/>; one-shot timers (period infinite) are enough here.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ManualTimeProvider : TimeProvider
{
	private readonly List<ManualTimer> _timers = [];
	private TimeSpan _now = TimeSpan.Zero;

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		var timer = new ManualTimer(this, callback, state);
		timer.Change(dueTime, period);
		_timers.Add(timer);
		return timer;
	}

	/// <summary>Moves the clock forward and fires every armed timer that comes due.</summary>
	public void Advance(TimeSpan by)
	{
		_now += by;
		foreach (var timer in _timers.ToList())
			timer.FireIfDue(_now);
	}

	private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
	{
		private TimeSpan? _due;
		private bool _disposed;

		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(ITimer));

			_due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
			return true;
		}

		public void FireIfDue(TimeSpan now)
		{
			if (_disposed || _due == null || _due > now)
				return;

			_due = null;
			callback(state);
		}

		public void Dispose() => _disposed = true;

		public ValueTask DisposeAsync()
		{
			_disposed = true;
			return ValueTask.CompletedTask;
		}
	}
}
