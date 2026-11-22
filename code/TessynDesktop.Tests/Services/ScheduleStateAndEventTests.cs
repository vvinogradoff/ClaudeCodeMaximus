using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Package J4: how a schedule reaches the clients without a method call: <c>SessionStateDto.HasSchedules</c> on the state of a session and the
/// <c>schedule.changed</c> notification (with the <c>session.changed</c> of a session whose flag moved).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScheduleStateAndEventTests : IDisposable
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);   // generous: the windows are real timers and the machine may be loaded

	private readonly ImportTestEnv _env = new();
	private readonly SchedulerService _scheduler;
	private readonly RemoteEventBus _bus = new();
	private readonly SessionFileService _files;
	private readonly SessionEpochTracker _epochs;
	private readonly IDaemonClient _daemon;
	private readonly FakeDaemonProxy _fake;
	private readonly List<RemoteNotification> _received = [];
	private readonly SessionNodeModel _one;
	private readonly SessionNodeModel _two;

	public ScheduleStateAndEventTests()
	{
		var directory = _env.AddDirectory("proj");
		_one = _env.Registry.AddSession(directory, "One");
		_two = _env.Registry.AddSession(directory, "Two");
		_scheduler = new SchedulerService(_env.Settings, new FakeTurnService(), _env.Registry);
		_files  = new SessionFileService(_env.Settings);
		_epochs = new SessionEpochTracker(_files);
		_daemon = FakeDaemonProxy.Create(out _fake);
		_fake.ByNodeId[_one.NodeId] = _one;
		_fake.ByNodeId[_two.NodeId] = _two;
		_bus.Published += n => { lock (_received) _received.Add(n); };
	}

	public void Dispose()
	{
		_bus.Dispose();
		_epochs.Dispose();
		_env.Dispose();
	}

	private ScheduleModel Add(SessionNodeModel node, bool raise = true)
	{
		var schedule = new ScheduleModel { ScheduleId = Guid.NewGuid().ToString("N")[..8], TargetNodeId = node.NodeId, Prompt = "p", Kind = ScheduleKind.At, FireAtUtc = DateTimeOffset.UtcNow.AddHours(1) };
		_scheduler.AddSchedule(schedule);
		if (raise)
			Raise();
		return schedule;
	}

	private void Raise() => _fake.Raise(nameof(IDaemonClient.ScheduleChanged), EventArgs.Empty);

	private RemoteNotificationPublisher StartPublisher(ISchedulerService? scheduler)
	{
		var publisher = new RemoteNotificationPublisher(_daemon, _files, _bus, new TreeVersionTracker(), _epochs, new FakeStateBuilder(), scheduler: scheduler);
		publisher.Start();
		return publisher;
	}

	private List<RemoteNotification> Received(string method)
	{
		lock (_received)
			return _received.Where(n => n.Method == method).ToList();
	}

	private List<RemoteNotification> WaitFor(string method, int count)
	{
		var watch = Stopwatch.StartNew();
		while (watch.Elapsed < Wait && Received(method).Count < count)
			Thread.Sleep(20);
		return Received(method);
	}

	// ── SessionStateDto.HasSchedules ─────────────────────────────────────────────────

	[Fact]
	public void TheState_SaysWhetherASessionHasSchedules()
	{
		var states = new SessionStateBuilder(_env.Registry, TestEngines.Claude(new FakeStreamingManagerForTurnService()), new FakeAttentionService(), new FakeActivityService(), null, _scheduler);
		Add(_one, raise: false);

		Assert.True(states.Build(_one).HasSchedules);
		Assert.False(states.Build(_two).HasSchedules);
	}

	[Fact]
	public void TheState_FollowsTheRemovalOfTheLastSchedule()
	{
		var states = new SessionStateBuilder(_env.Registry, TestEngines.Claude(new FakeStreamingManagerForTurnService()), new FakeAttentionService(), new FakeActivityService(), null, _scheduler);
		var schedule = Add(_one, raise: false);

		_scheduler.RemoveSchedule(schedule.ScheduleId);

		Assert.False(states.Build(_one).HasSchedules);
	}

	[Fact]
	public void WithoutAScheduler_TheFlagIsFalse()
	{
		var states = new SessionStateBuilder(_env.Registry, TestEngines.Claude(new FakeStreamingManagerForTurnService()), new FakeAttentionService(), new FakeActivityService());

		Assert.False(states.Build(_one).HasSchedules);
	}

	// ── schedule.changed ─────────────────────────────────────────────────────────────

	[Fact]
	public void AddingTheFirstSchedule_PublishesScheduleChanged_NamingTheSession_AndSessionChangedForIt()
	{
		using var publisher = new Stopper(StartPublisher(_scheduler));

		Add(_one);

		var changed = WaitFor("schedule.changed", 1);
		var payload = Assert.Single(changed).Params.Deserialize<ScheduleChangedPayload>(TdcpJsonOptions.Default)!;
		Assert.Equal(_one.NodeId, payload.NodeId);
		var session = Assert.Single(WaitFor("session.changed", 1));
		Assert.Equal(_one.NodeId, session.Params["state"]!["id"]!.GetValue<string>());
	}

	[Fact]
	public void ASchedulesPureFiring_PublishesNothing()
	{
		using var publisher = new Stopper(StartPublisher(_scheduler));
		var first = Add(_one);
		WaitFor("schedule.changed", 1);
		WaitFor("session.changed", 1);

		first.FireCount++;   // the schedule fired: no schedule came or went
		for (var i = 0; i < 5; i++)
			Raise();

		Thread.Sleep(700);
		Assert.Single(Received("schedule.changed"));
		Assert.Single(Received("session.changed"));
	}

	[Fact]
	public void ABurstOfAdds_IsOneNotification()
	{
		using var publisher = new Stopper(StartPublisher(_scheduler));

		Add(_one);
		Add(_one);
		Add(_one);

		Assert.Single(WaitFor("schedule.changed", 1));
		Thread.Sleep(700);
		Assert.Single(Received("schedule.changed"));
		Assert.Equal(_one.NodeId, Received("schedule.changed")[0].Params["nodeId"]!.GetValue<string>());
	}

	[Fact]
	public void ASecondScheduleOfASession_IsAChange_ButMovesNoFlag()
	{
		using var publisher = new Stopper(StartPublisher(_scheduler));
		Add(_one);
		WaitFor("session.changed", 1);

		Add(_one);

		var changed = WaitFor("schedule.changed", 2);
		Assert.Equal(_one.NodeId, changed[1].Params["nodeId"]!.GetValue<string>());
		Thread.Sleep(500);
		Assert.Single(Received("session.changed"));
	}

	[Fact]
	public void RemovingTheLastSchedule_MovesTheFlagBack()
	{
		using var publisher = new Stopper(StartPublisher(_scheduler));
		var schedule = Add(_one);
		WaitFor("schedule.changed", 1);
		WaitFor("session.changed", 1);

		_scheduler.RemoveSchedule(schedule.ScheduleId);
		Raise();

		var changed = WaitFor("schedule.changed", 2);
		Assert.Equal(_one.NodeId, changed[1].Params["nodeId"]!.GetValue<string>());
		Assert.Equal(2, WaitFor("session.changed", 2).Count);
	}

	[Fact]
	public void SeveralSessionsAtOnce_ArePublishedWithoutANodeId_AndEachGetsItsSessionChanged()
	{
		using var publisher = new Stopper(StartPublisher(_scheduler));
		Add(_one, raise: false);
		Add(_two, raise: false);

		Raise();

		var changed = Assert.Single(WaitFor("schedule.changed", 1));
		Assert.Null(changed.Params["nodeId"]);
		Assert.Equal(2, WaitFor("session.changed", 2).Count);
	}

	[Fact]
	public void TheSchedulesThatExistAtStart_AreNotAChange()
	{
		Add(_one, raise: false);
		using var publisher = new Stopper(StartPublisher(_scheduler));

		Raise();   // something fired, the set is as it was

		Thread.Sleep(700);
		Assert.Empty(Received("schedule.changed"));
		Assert.Empty(Received("session.changed"));
	}

	[Fact]
	public void WithoutAScheduler_NothingIsSubscribed_AndStopLetsGo()
	{
		var without = StartPublisher(null);
		Assert.Equal(0, _fake.SubscriberCount(nameof(IDaemonClient.ScheduleChanged)));
		without.Stop();

		var with = StartPublisher(_scheduler);
		Assert.Equal(1, _fake.SubscriberCount(nameof(IDaemonClient.ScheduleChanged)));
		with.Stop();
		Assert.Equal(0, _fake.SubscriberCount(nameof(IDaemonClient.ScheduleChanged)));
	}

	/// <summary>Stops the publisher at the end of a test.</summary>
	private sealed class Stopper(RemoteNotificationPublisher publisher) : IDisposable
	{
		public void Dispose() => publisher.Stop();
	}
}
