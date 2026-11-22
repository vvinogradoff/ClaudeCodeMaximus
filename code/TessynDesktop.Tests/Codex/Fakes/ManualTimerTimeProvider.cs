using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TessynDesktop.Tests.Codex.Fakes;

/// <summary>
/// A clock the test advances by hand, with timers: <see cref="Advance"/> fires every timer that falls due, so the host's idle shutdown,
/// request timeouts and <c>CancellationTokenSource(TimeSpan, TimeProvider)</c> run deterministically.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed partial class ManualTimerTimeProvider : TimeProvider
{
	private readonly object            _lock   = new();
	private readonly List<ManualTimer> _timers = [];
	private DateTimeOffset             _now    = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

	public override DateTimeOffset GetUtcNow()
	{
		lock (_lock)
			return _now;
	}

	public override long TimestampFrequency => TimeSpan.TicksPerSecond;

	public override long GetTimestamp() => GetUtcNow().UtcTicks;

	/// <summary>Timers that are armed (due at some point).</summary>
	public int ArmedTimerCount
	{
		get { lock (_lock) return _timers.Count(t => t.DueAt is not null); }
	}

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		var timer = new ManualTimer(this, callback, state);
		lock (_lock)
			_timers.Add(timer);
		timer.Change(dueTime, period);
		return timer;
	}

	public void Advance(TimeSpan by)
	{
		DateTimeOffset target;
		lock (_lock)
			target = _now + by;

		while (true)
		{
			ManualTimer? next;
			lock (_lock)
			{
				next = _timers.Where(t => t.DueAt is { } due && due <= target).OrderBy(t => t.DueAt).FirstOrDefault();
				if (next is null)
				{
					_now = target;
					return;
				}
				_now = next.DueAt!.Value;
				next.Fired();
			}
			next.Invoke();
		}
	}

	private void Remove(ManualTimer timer)
	{
		lock (_lock)
			_timers.Remove(timer);
	}
}
