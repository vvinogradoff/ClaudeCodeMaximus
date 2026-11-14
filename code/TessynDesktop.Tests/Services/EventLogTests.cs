using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class EventLogTests
{
	private sealed class ManualTime : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private static void PublishN(RemoteEventBus bus, int count)
	{
		for (var i = 0; i < count; i++)
			bus.Publish("session.changed", new SamplePayload("n" + i, null, i));
	}

	[Fact]
	public void Stamps_a_monotonic_seq_starting_at_one()
	{
		var bus = new RemoteEventBus();
		using var log = new EventLog(bus);
		var seen = new List<RemoteNotification>();
		log.Appended += seen.Add;

		PublishN(bus, 3);

		Assert.Equal([1L, 2L, 3L], seen.Select(n => n.Seq).ToArray());
		Assert.Equal(3, log.HeadSeq);
	}

	[Fact]
	public void ReadSince_returns_only_newer_events()
	{
		var bus = new RemoteEventBus();
		using var log = new EventLog(bus);
		PublishN(bus, 5);

		var replay = log.ReadSince(3);

		Assert.Null(replay.ResyncReason);
		Assert.Equal([4L, 5L], replay.Events.Select(e => e.Seq).ToArray());
		Assert.Equal(5, replay.HeadSeq);
	}

	[Fact]
	public void ReadSince_head_is_covered_and_empty()
	{
		var bus = new RemoteEventBus();
		using var log = new EventLog(bus);
		PublishN(bus, 2);

		var replay = log.ReadSince(2);

		Assert.Null(replay.ResyncReason);
		Assert.Empty(replay.Events);
	}

	[Fact]
	public void ReadSince_zero_replays_everything_still_in_the_ring()
	{
		var bus = new RemoteEventBus();
		using var log = new EventLog(bus);
		PublishN(bus, 2);

		Assert.Equal(2, log.ReadSince(0).Events.Count);
	}

	[Fact]
	public void Ring_is_bounded_by_capacity_and_a_gap_asks_for_resync()
	{
		var bus = new RemoteEventBus();
		using var log = new EventLog(bus, capacity: 3);
		PublishN(bus, 10);

		var gap = log.ReadSince(2);
		Assert.Equal(Constants.Remote.EventLog.ResyncReasons.Gap, gap.ResyncReason);
		Assert.Empty(gap.Events);

		// Events 8..10 are all still in the ring, so a client at 7 misses nothing.
		var covered = log.ReadSince(7);
		Assert.Null(covered.ResyncReason);
		Assert.Equal([8L, 9L, 10L], covered.Events.Select(e => e.Seq).ToArray());
	}

	[Fact]
	public void Events_older_than_the_max_age_leave_the_ring()
	{
		var time = new ManualTime();
		var bus = new RemoteEventBus();
		using var log = new EventLog(bus, time, maxAge: TimeSpan.FromMinutes(10));
		PublishN(bus, 2);

		time.Now += TimeSpan.FromMinutes(11);
		PublishN(bus, 1);

		Assert.Equal(Constants.Remote.EventLog.ResyncReasons.Gap, log.ReadSince(0).ResyncReason);
		Assert.Equal([3L], log.ReadSince(2).Events.Select(e => e.Seq).ToArray());
	}

	[Fact]
	public void A_seq_ahead_of_the_head_is_unknown()
	{
		var bus = new RemoteEventBus();
		using var log = new EventLog(bus);
		PublishN(bus, 2);

		Assert.Equal(Constants.Remote.EventLog.ResyncReasons.UnknownSeq, log.ReadSince(99).ResyncReason);
	}

	[Fact]
	public void A_throwing_subscriber_does_not_stop_logging()
	{
		var bus = new RemoteEventBus();
		using var log = new EventLog(bus);
		log.Appended += _ => throw new InvalidOperationException("boom");

		PublishN(bus, 2);

		Assert.Equal(2, log.HeadSeq);
	}

	[Fact]
	public void Dispose_detaches_from_the_bus()
	{
		var bus = new RemoteEventBus();
		var log = new EventLog(bus);
		log.Dispose();

		PublishN(bus, 1);

		Assert.Equal(0, log.HeadSeq);
	}

	[Fact]
	public void Stamped_payload_is_preserved()
	{
		var bus = new RemoteEventBus();
		using var log = new EventLog(bus);
		RemoteNotification? seen = null;
		log.Appended += n => seen = n;

		bus.Publish("session.changed", new SamplePayload("x", null, 7));

		Assert.Equal("session.changed", seen!.Method);
		Assert.Equal(7, seen.Params["count"]!.GetValue<int>());
	}
}
