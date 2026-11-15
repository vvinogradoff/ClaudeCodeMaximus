using System;
using System.IO;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Package J4: <see cref="ScheduleApiService"/> behind <c>schedules.list</c>, <c>schedules.add</c> and <c>schedules.remove</c>, over the real scheduler.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScheduleApiServiceTests : IDisposable
{
	private readonly ImportTestEnv _env = new();
	private readonly ManualTimeProvider _clock = new();
	private readonly SchedulerService _scheduler;
	private readonly ScheduleApiService _sut;
	private readonly SessionNodeModel _session;
	private readonly SessionNodeModel _other;

	public ScheduleApiServiceTests()
	{
		var directory = _env.AddDirectory("proj");
		_session   = _env.Registry.AddSession(directory, "One");
		_other     = _env.Registry.AddSession(directory, "Two");
		_scheduler = new SchedulerService(_env.Settings, new FakeTurnService(), _env.Registry);
		_sut       = new ScheduleApiService(_scheduler, _env.Registry, _clock);
	}

	public void Dispose() => _env.Dispose();

	private DateTimeOffset Later(TimeSpan by) => _clock.GetUtcNow() + by;

	private ScheduleAddRequestModel At(DateTimeOffset when, string? note = null) =>
		new(_session.NodeId, "wake", "at", when, null, null, note, null);

	// ── add ──────────────────────────────────────────────────────────────────────────

	[Fact]
	public void AnAtSchedule_IsStoredAndAnswered()
	{
		var added = _sut.Add(At(Later(TimeSpan.FromHours(2)), "morning"));

		Assert.Equal(8, added.Id.Length);
		Assert.Equal((_session.NodeId, ScheduleKindDto.At, "wake", "morning"), (added.NodeId, added.Kind, added.Prompt, added.Note));
		Assert.Equal(Later(TimeSpan.FromHours(2)), added.FireAtUtc);
		Assert.Equal((MissedFireDto.FireOnce, 0, 0, false), (added.MissedFire, added.FireCount, added.MaxFires, added.IsMailbox));
		Assert.Equal(added.Id, Assert.Single(_scheduler.GetSchedules()).ScheduleId);
	}

	[Fact]
	public void ADelaySchedule_IsOneThatFiresOnce_AtTheGivenTime()
	{
		var added = _sut.Add(new ScheduleAddRequestModel(_session.NodeId, "later", "Delay", Later(TimeSpan.FromMinutes(5)), null, "skip", null, 7));

		Assert.Equal(ScheduleKindDto.Delay, added.Kind);
		Assert.Equal(MissedFireDto.Skip, added.MissedFire);
		Assert.Equal(0, added.MaxFires);   // a limit is for cron only
	}

	[Fact]
	public void ACronSchedule_GetsItsFirstFireTime_AndTheDefaultLimitOfTheAgentTools()
	{
		var added = _sut.Add(new ScheduleAddRequestModel(_session.NodeId, "tick", "cron", null, " */5 * * * * ", null, null, null));

		Assert.Equal(ScheduleKindDto.Cron, added.Kind);
		Assert.Equal("*/5 * * * *", added.Cron);
		Assert.NotNull(added.FireAtUtc);
		Assert.Equal(Constants.Agent.MaxTurnsPerLoop, added.MaxFires);
	}

	[Fact]
	public void ACronLimitOfZero_MeansNoLimit()
	{
		var added = _sut.Add(new ScheduleAddRequestModel(_session.NodeId, "tick", "cron", null, "0 9 * * *", null, null, 0));

		Assert.Equal(0, added.MaxFires);
	}

	[Fact]
	public void AFireTimeSlightlyInThePast_IsAccepted_ForClockDrift()
	{
		var added = _sut.Add(At(Later(TimeSpan.FromSeconds(-30))));

		Assert.NotNull(added);
	}

	public static TheoryData<string, ScheduleAddRequestModel> Invalid() => new()
	{
		{ "blank prompt", new("n", " ", "at", null, null, null, null, null) },
		{ "long prompt", new("n", new string('p', Constants.ScheduleApi.PromptMaxChars + 1), "at", null, null, null, null, null) },
		{ "long note", new("n", "p", "at", null, null, null, new string('n', 501), null) },
		{ "unknown kind", new("n", "p", "weekly", null, null, null, null, null) },
		{ "missing kind", new("n", "p", null!, null, null, null, null, null) },
		{ "at without a time", new("n", "p", "at", null, null, null, null, null) },
		{ "delay without a time", new("n", "p", "delay", null, null, null, null, null) },
		{ "at with a cron", new("n", "p", "at", new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero), "* * * * *", null, null, null) },
		{ "cron without an expression", new("n", "p", "cron", null, null, null, null, null) },
		{ "cron with a time", new("n", "p", "cron", new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero), "* * * * *", null, null, null) },
		{ "invalid cron", new("n", "p", "cron", null, "not a cron", null, null, null) },
		{ "too long cron", new("n", "p", "cron", null, new string('*', 201), null, null, null) },
		{ "past", new("n", "p", "at", new DateTimeOffset(2026, 11, 4, 11, 0, 0, TimeSpan.Zero), null, null, null, null) },
		{ "too far ahead", new("n", "p", "at", new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), null, null, null, null) },
		{ "unknown missed-fire policy", new("n", "p", "at", new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero), null, "retry", null, null) },
		{ "negative limit", new("n", "p", "cron", null, "*/5 * * * *", null, null, -1) },
		{ "huge limit", new("n", "p", "cron", null, "*/5 * * * *", null, null, 10_001) },
		{ "every minute", new("n", "p", "cron", null, "* * * * *", null, null, null) },
		{ "two fires a minute apart", new("n", "p", "cron", null, "0,1 * * * *", null, null, null) },
	};

	[Theory]
	[MemberData(nameof(Invalid))]
	public void AnInvalidSchedule_IsRefusedByField_AndNothingIsStored(string why, ScheduleAddRequestModel request)
	{
		var withNode = request with { NodeId = _session.NodeId };

		var error = Assert.Throws<TdcpInvalidParamsException>(() => _sut.Add(withNode));

		Assert.False(string.IsNullOrWhiteSpace(error.Message), why);
		Assert.DoesNotContain(_env.Root, error.Message);
		Assert.Empty(_scheduler.GetSchedules());
	}

	[Fact]
	public void AnUnknownSession_IsADaemonError_AndNothingIsStored()
	{
		var error = Assert.Throws<TdcpException>(() => _sut.Add(At(Later(TimeSpan.FromHours(1)) ) with { NodeId = "nope" }));

		Assert.DoesNotContain(_env.Root, error.Message);
		Assert.Empty(_scheduler.GetSchedules());
	}

	[Theory]
	[InlineData("*/2 * * * *")]
	[InlineData("*/5 * * * *")]
	[InlineData("0 0 31 2 *")]   // valid, never matches: parked
	public void ACronAtLeastTwoMinutesApart_IsAccepted(string cron)
	{
		var added = _sut.Add(new ScheduleAddRequestModel(_session.NodeId, "tick", "cron", null, cron, null, null, null));

		Assert.Equal(cron, added.Cron);
	}

	[Fact]
	public void ASessionHasAtMostOneHundredSchedules_AndMailboxDeliveriesDoNotCount()
	{
		_scheduler.AddOrMergeMailboxDelivery(_session.NodeId, "worker", "done");
		for (var i = 0; i < Constants.ScheduleApi.MaxPerSession; i++)
			_sut.Add(At(Later(TimeSpan.FromHours(1 + i))));

		var error = Assert.Throws<TdcpInvalidParamsException>(() => _sut.Add(At(Later(TimeSpan.FromHours(500)))));

		Assert.Contains("too many", error.Message);
		_sut.Add(At(Later(TimeSpan.FromHours(500))) with { NodeId = _other.NodeId });   // another session is not affected
	}

	[Fact]
	public void ThereAreAtMostOneThousandSchedulesInAll()
	{
		var directory = _env.AddDirectory("many");
		var sessions = Enumerable.Range(0, 10).Select(i => _env.Registry.AddSession(directory, "S" + i)).ToList();
		foreach (var session in sessions)
		{
			for (var i = 0; i < Constants.ScheduleApi.MaxPerSession; i++)
				_scheduler.AddSchedule(new ScheduleModel { ScheduleId = Guid.NewGuid().ToString("N")[..8], TargetNodeId = session.NodeId, Prompt = "p", Kind = ScheduleKind.At, FireAtUtc = Later(TimeSpan.FromHours(1)) });
		}

		var error = Assert.Throws<TdcpInvalidParamsException>(() => _sut.Add(At(Later(TimeSpan.FromHours(2)))));

		Assert.Contains("too many", error.Message);
		Assert.Equal(Constants.ScheduleApi.MaxTotal, _scheduler.GetSchedules().Count);
	}

	[Fact]
	public void AMalformedRequest_IsJudgedBeforeTheSessionIsLookedUp()
	{
		Assert.Throws<TdcpInvalidParamsException>(() => _sut.Add(new ScheduleAddRequestModel("nope", "", "at", null, null, null, null, null)));
	}

	[Fact]
	public void TheSchedule_SurvivesARestart()
	{
		var added = _sut.Add(At(Later(TimeSpan.FromHours(2))));

		var reloaded = new AppSettingsService(_env.Root);
		reloaded.Load();

		Assert.Equal(added.Id, Assert.Single(reloaded.Settings.Schedules).ScheduleId);
	}

	// ── list and remove ──────────────────────────────────────────────────────────────

	[Fact]
	public void List_ReturnsAllSchedules_OrThoseOfOneSession()
	{
		var mine = _sut.Add(At(Later(TimeSpan.FromHours(1))));
		var theirs = _sut.Add(At(Later(TimeSpan.FromHours(2))) with { NodeId = _other.NodeId });

		Assert.Equal([mine.Id, theirs.Id], _sut.List(null).Schedules.Select(s => s.Id));
		Assert.Equal([theirs.Id], _sut.List(_other.NodeId).Schedules.Select(s => s.Id));
		Assert.Equal([mine.Id, theirs.Id], _sut.List(" ").Schedules.Select(s => s.Id));   // a blank node id is "all"
	}

	[Fact]
	public void AMailboxDelivery_IsListedAsOne()
	{
		_scheduler.AddOrMergeMailboxDelivery(_session.NodeId, "worker", "done");

		var listed = Assert.Single(_sut.List(_session.NodeId).Schedules);

		Assert.True(listed.IsMailbox);
		Assert.Equal(ScheduleKindDto.Delay, listed.Kind);
	}

	[Fact]
	public void Remove_TakesTheScheduleAway_AndSaysSoOnlyOnce()
	{
		var added = _sut.Add(At(Later(TimeSpan.FromHours(1))));

		var first = _sut.Remove(added.Id);
		var second = _sut.Remove(added.Id);

		Assert.True(first.Removed);
		Assert.False(second.Removed);
		Assert.Empty(_scheduler.GetSchedules());
	}

	[Fact]
	public void TheSchedulerEvent_FollowsAddAndRemove()
	{
		var raised = 0;
		_scheduler.ScheduleChanged += (_, _) => raised++;

		var added = _sut.Add(At(Later(TimeSpan.FromHours(1))));
		_sut.Remove(added.Id);

		Assert.Equal(2, raised);
	}
}
