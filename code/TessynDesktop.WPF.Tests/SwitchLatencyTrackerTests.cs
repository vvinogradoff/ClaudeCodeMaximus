using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Tests;

/// <summary>Click-to-first-paint measurement.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SwitchLatencyTrackerTests
{
	private sealed class ManualTime : TimeProvider
	{
		public long Ticks { get; set; }

		public override long TimestampFrequency => 1000;

		public override long GetTimestamp() => Ticks;
	}

	[Fact]
	public void BeginThenPainted_RecordsTheElapsedTimeAndWarmFlag()
	{
		var time = new ManualTime();
		var tracker = new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance, time);

		tracker.Begin("a");
		tracker.MarkWarm("a", true);
		time.Ticks = 42;
		tracker.Painted("a", 3.5);

		Assert.NotNull(tracker.Last);
		Assert.Equal(42, tracker.Last!.ClickToPaintMilliseconds, 3);
		Assert.Equal(3.5, tracker.Last.PageMilliseconds);
		Assert.True(tracker.Last.Warm);
	}

	[Fact]
	public void Painted_ForASupersededSwitch_IsIgnored()
	{
		var tracker = new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance, new ManualTime());

		tracker.Begin("a");
		tracker.Begin("b");
		tracker.Painted("a", 1);

		Assert.Null(tracker.Last);
	}
}
