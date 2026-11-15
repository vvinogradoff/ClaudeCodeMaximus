using System;
using System.Collections.Generic;
using System.Threading;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

internal sealed record SamplePayload(string Name, string? Nothing, int Count);

/// <remarks>Created by Claude</remarks>
public sealed class RemoteEventBusTests : IDisposable
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(80);

	private readonly RemoteEventBus _bus = new();
	private readonly List<RemoteNotification> _received = new();
	private readonly SemaphoreSlim _signal = new(0);

	public RemoteEventBusTests()
	{
		_bus.Published += n =>
		{
			lock (_received)
				_received.Add(n);
			_signal.Release();
		};
	}

	public void Dispose() => _bus.Dispose();

	private List<RemoteNotification> Snapshot()
	{
		lock (_received)
			return new List<RemoteNotification>(_received);
	}

	[Fact]
	public void Publish_RaisesImmediatelyWithPayloadSerializedWithSharedOptions()
	{
		_bus.Publish("session.appended", new SamplePayload("abc", null, 3));

		var notification = Assert.Single(Snapshot());
		Assert.Equal("session.appended", notification.Method);
		Assert.Equal("abc", notification.Params["name"]!.GetValue<string>());
		Assert.Equal(3, notification.Params["count"]!.GetValue<int>());
		Assert.False(notification.Params.AsObject().ContainsKey("nothing"));
	}

	[Fact]
	public void ACoalescedPayloadBuilder_MayPublishAndSchedule_BecauseItRunsOutsideTheBusLock()
	{
		// J4 review K1: a builder that publishes (immediately and coalesced) must not deadlock or lose either notification.
		_bus.PublishCoalesced("outer", "k", () =>
		{
			_bus.Publish("inner.now", new SamplePayload("a", null, 1));
			_bus.PublishCoalesced("inner.later", "k", () => new SamplePayload("b", null, 2), Window);
			_bus.FlushPending("inner.later", "other");
			return new SamplePayload("c", null, 3);
		}, Window);

		var deadline = DateTime.UtcNow + Wait;
		while (DateTime.UtcNow < deadline && Snapshot().Count < 3)
			Thread.Sleep(20);

		Assert.Equal(["inner.later", "inner.now", "outer"], Snapshot().Select(n => n.Method).Order(StringComparer.Ordinal));
	}

	[Fact]
	public void Publish_WithoutSubscribers_DoesNotThrow()
	{
		using var lonely = new RemoteEventBus();

		lonely.Publish("x", new SamplePayload("a", null, 1));
	}

	[Fact]
	public void Publish_ASubscriberThatThrows_DoesNotStopOthersOrReachThePublisher()
	{
		var other = 0;
		_bus.Published += _ => throw new InvalidOperationException("boom");
		_bus.Published += _ => other++;

		_bus.Publish("x", new SamplePayload("a", null, 1));

		Assert.Equal(1, other);
		Assert.Single(Snapshot());
	}

	[Fact]
	public void Publish_ANonSerializablePayload_DoesNotThrow()
	{
		_bus.Publish("x", new CyclicPayload());

		Assert.Empty(Snapshot());
	}

	[Fact]
	public void PublishCoalesced_ManyCallsInOneWindow_PublishOnceAndRunTheFactoryOnceWithLatestState()
	{
		var state = 0;
		var factoryCalls = 0;
		Func<object?> factory = () => { factoryCalls++; return new SamplePayload("n", null, state); };

		for (var i = 1; i <= 20; i++)
		{
			state = i;
			_bus.PublishCoalesced("session.changed", "node-1", factory, Window);
		}

		Assert.True(_signal.Wait(Wait));
		Thread.Sleep(Window * 3);
		var notification = Assert.Single(Snapshot());
		Assert.Equal(20, notification.Params["count"]!.GetValue<int>());
		Assert.Equal(1, factoryCalls);
	}

	[Fact]
	public void PublishCoalesced_DoesNotPublishBeforeTheWindowEnds()
	{
		_bus.PublishCoalesced("session.changed", "k", () => new SamplePayload("n", null, 1), TimeSpan.FromSeconds(30));

		Assert.False(_signal.Wait(TimeSpan.FromMilliseconds(200)));
		Assert.Empty(Snapshot());
	}

	[Fact]
	public void PublishCoalesced_DifferentKeysAndMethods_PublishSeparately()
	{
		_bus.PublishCoalesced("session.changed", "a", () => new SamplePayload("a", null, 1), Window);
		_bus.PublishCoalesced("session.changed", "b", () => new SamplePayload("b", null, 2), Window);
		_bus.PublishCoalesced("session.appended", "a", () => new SamplePayload("c", null, 3), Window);

		Assert.True(_signal.Wait(Wait));
		Assert.True(_signal.Wait(Wait));
		Assert.True(_signal.Wait(Wait));
		Assert.Equal(3, Snapshot().Count);
	}

	[Fact]
	public void FlushPending_PublishesNowOnTheCallingThread_AndTheWindowDoesNotPublishAgain()
	{
		var factoryCalls = 0;
		_bus.PublishCoalesced("m", "k", () => { factoryCalls++; return new SamplePayload("n", null, 1); }, Window);

		_bus.FlushPending("m", "k");

		Assert.Single(Snapshot());
		Thread.Sleep(Window * 3);
		Assert.Single(Snapshot());
		Assert.Equal(1, factoryCalls);
	}

	[Fact]
	public void FlushPending_WithNothingPending_DoesNothing_AndOnlyMatchesItsOwnMethodAndKey()
	{
		_bus.FlushPending("m", "k");
		_bus.PublishCoalesced("m", "other", () => new SamplePayload("n", null, 1), TimeSpan.FromSeconds(30));
		_bus.PublishCoalesced("x", "k", () => new SamplePayload("n", null, 1), TimeSpan.FromSeconds(30));

		_bus.FlushPending("m", "k");

		Assert.Empty(Snapshot());
	}

	[Fact]
	public void FlushPending_AFactoryReturningNull_PublishesNothing_AndALaterCallOpensANewWindow()
	{
		_bus.PublishCoalesced("m", "k", () => null, TimeSpan.FromSeconds(30));
		_bus.FlushPending("m", "k");
		Assert.Empty(Snapshot());

		_bus.PublishCoalesced("m", "k", () => new SamplePayload("again", null, 2), Window);
		Assert.True(_signal.Wait(Wait));
		Assert.Single(Snapshot());
	}

	[Fact]
	public void PublishCoalesced_AfterTheWindowEnded_StartsANewWindow()
	{
		_bus.PublishCoalesced("m", "k", () => new SamplePayload("first", null, 1), Window);
		Assert.True(_signal.Wait(Wait));

		_bus.PublishCoalesced("m", "k", () => new SamplePayload("second", null, 2), Window);
		Assert.True(_signal.Wait(Wait));

		var all = Snapshot();
		Assert.Equal(2, all.Count);
		Assert.Equal("second", all[1].Params["name"]!.GetValue<string>());
	}

	[Fact]
	public void PublishCoalesced_ACallArrivingWhileTheFactoryRuns_OpensTheNextWindow()
	{
		var started = new ManualResetEventSlim();
		var release = new ManualResetEventSlim();
		_bus.PublishCoalesced("m", "k", () =>
		{
			started.Set();
			release.Wait(Wait);
			return new SamplePayload("first", null, 1);
		}, TimeSpan.Zero);
		Assert.True(started.Wait(Wait));

		_bus.PublishCoalesced("m", "k", () => new SamplePayload("second", null, 2), Window);
		release.Set();

		Assert.True(_signal.Wait(Wait));
		Assert.True(_signal.Wait(Wait));
		Assert.Equal(2, Snapshot().Count);
	}

	[Fact]
	public void PublishCoalesced_AFactoryReturningNull_PublishesNothing_AndLaterCallsStillWork()
	{
		_bus.PublishCoalesced("m", "k", () => null, TimeSpan.Zero);
		Assert.False(_signal.Wait(TimeSpan.FromMilliseconds(300)));

		_bus.PublishCoalesced("m", "k", () => new SamplePayload("ok", null, 1), TimeSpan.Zero);
		Assert.True(_signal.Wait(Wait));
	}

	[Fact]
	public void PublishCoalesced_AFactoryThatThrows_DoesNotCrashAndLaterCallsStillWork()
	{
		_bus.PublishCoalesced("m", "k", () => throw new InvalidOperationException("boom"), TimeSpan.Zero);
		Assert.False(_signal.Wait(TimeSpan.FromMilliseconds(300)));

		_bus.PublishCoalesced("m", "k", () => new SamplePayload("ok", null, 1), TimeSpan.Zero);
		Assert.True(_signal.Wait(Wait));
	}

	[Fact]
	public void PublishCoalesced_ReturnsWithoutWaitingForTheFactory()
	{
		var release = new ManualResetEventSlim();

		_bus.PublishCoalesced("m", "k", () => { release.Wait(Wait); return null; }, TimeSpan.Zero);
		_bus.PublishCoalesced("m", "other", () => null, Window);

		release.Set();
	}

	[Fact]
	public void Dispose_CancelsPendingPublications_AndLaterCallsDoNothing()
	{
		_bus.PublishCoalesced("m", "k", () => new SamplePayload("x", null, 1), Window);

		_bus.Dispose();
		_bus.PublishCoalesced("m", "k2", () => new SamplePayload("y", null, 2), TimeSpan.Zero);

		Assert.False(_signal.Wait(TimeSpan.FromMilliseconds(400)));
	}

	private sealed class CyclicPayload
	{
		public CyclicPayload Self => this;
	}
}
