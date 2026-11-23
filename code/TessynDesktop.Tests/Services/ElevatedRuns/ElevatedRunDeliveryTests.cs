using System;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.ElevatedRuns;

/// <summary>TC.ER.40 (the merge part): the outcome of an elevated run is a delivery of the mailbox machinery, merged with a pending one (FR.15.12, FR.15.19).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunDeliveryTests : IDisposable
{
	private readonly ElevatedRunTestEnv _env = new();
	private readonly SchedulerService _scheduler;

	public ElevatedRunDeliveryTests() => _scheduler = new SchedulerService(_env.Settings, new FakeTurnService(), _env.Registry);

	public void Dispose() => _env.Dispose();

	[Fact]
	public void AnOutcomeIsADelayZeroMailboxDeliveryForTheSession_TC_ER_40()
	{
		_scheduler.AddOrMergeElevatedRunDelivery(_env.Lead.NodeId, "aaaa1111", "[Elevated run aaaa1111 completed] r");

		var schedule = Assert.Single(_scheduler.GetSchedules());

		Assert.True(schedule.IsMailboxDelivery);
		Assert.Equal(_env.Lead.NodeId, schedule.TargetNodeId);
		Assert.Equal(ScheduleKind.Delay, schedule.Kind);
		Assert.Equal("[Elevated run aaaa1111 completed] r", schedule.Prompt);
		Assert.Equal("Elevated run aaaa1111", schedule.Note);
	}

	[Fact]
	public void TwoOutcomesAndAWorkerResultForTheSameSessionAreMergedIntoOneTurn_TC_ER_40()
	{
		_scheduler.AddOrMergeElevatedRunDelivery(_env.Lead.NodeId, "aaaa1111", "[Elevated run aaaa1111 completed] one");
		_scheduler.AddOrMergeElevatedRunDelivery(_env.Lead.NodeId, "bbbb2222", "[Elevated run bbbb2222 declined by the user] two");
		_scheduler.AddOrMergeMailboxDelivery(_env.Lead.NodeId, "worker", "done");

		var schedule = Assert.Single(_scheduler.GetSchedules());

		Assert.Equal(
			"[Elevated run aaaa1111 completed] one\n\n[Elevated run bbbb2222 declined by the user] two\n\n[Worker finished] worker\ndone",
			schedule.Prompt);
		Assert.Equal("Worker results (3): Elevated run aaaa1111, Elevated run bbbb2222, worker", schedule.Note);
	}

	[Fact]
	public void OutcomesForDifferentSessionsStaySeparate_TC_ER_40()
	{
		_scheduler.AddOrMergeElevatedRunDelivery(_env.Lead.NodeId, "aaaa1111", "one");
		_scheduler.AddOrMergeElevatedRunDelivery(_env.Wrk.NodeId, "bbbb2222", "two");

		Assert.Equal(new[] { _env.Lead.NodeId, _env.Wrk.NodeId }.Order(), _scheduler.GetSchedules().Select(s => s.TargetNodeId).Order());
	}
}
