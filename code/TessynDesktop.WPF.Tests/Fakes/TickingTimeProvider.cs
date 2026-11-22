namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>A <see cref="TimeProvider"/> with a settable clock whose repeating timers fire only when the test calls <see cref="Advance"/> (one tick per due period).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TickingTimeProvider(DateTimeOffset start) : TimeProvider
{
	private readonly List<TickingTimer> _timers = [];
	private DateTimeOffset _now = start;

	public override DateTimeOffset GetUtcNow() => _now;

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		var timer = new TickingTimer(callback, state, _now + dueTime, period);
		_timers.Add(timer);
		return timer;
	}

	public int ActiveTimers => _timers.Count(t => !t.Disposed);

	/// <summary>Moves the clock forward in steps of the timers' periods and fires each timer once per period that passed.</summary>
	public void Advance(TimeSpan by)
	{
		var target = _now + by;
		while (true)
		{
			var next = _timers.Where(t => !t.Disposed && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
			if (next == null)
				break;

			_now = next.Due > _now ? next.Due : _now;
			next.Fire();
		}

		_now = target;
	}

	private sealed class TickingTimer(TimerCallback callback, object? state, DateTimeOffset due, TimeSpan period) : ITimer
	{
		public DateTimeOffset Due { get; private set; } = due;

		public bool Disposed { get; private set; }

		public void Fire()
		{
			Due = period == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : Due + period;
			callback(state);
		}

		public bool Change(TimeSpan dueTime, TimeSpan newPeriod) => true;

		public void Dispose() => Disposed = true;

		public ValueTask DisposeAsync()
		{
			Disposed = true;
			return ValueTask.CompletedTask;
		}
	}
}
