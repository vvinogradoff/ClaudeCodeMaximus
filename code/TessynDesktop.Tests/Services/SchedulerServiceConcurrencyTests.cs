using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Robustness item 0.6/1 (ISSUE.008 class): <see cref="SchedulerService"/> mutated
/// <c>Settings.Schedules</c> under its private lock while <see cref="IAppSettingsService.Save"/> serializes the
/// graph under <see cref="IAppSettingsService.SyncRoot"/> — two different locks, so a Save could enumerate the
/// list mid-mutation ("Collection was modified"). The scheduler must use the shared SyncRoot.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SchedulerServiceConcurrencyTests : IDisposable
{
	private readonly string _tempDir;

	public SchedulerServiceConcurrencyTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cmx_sched_conc_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	public static IEnumerable<object[]> Operations() => new[]
	{
		new object[] { "AddSchedule" },
		new object[] { "AddOrMergeMailboxDelivery" },
		new object[] { "RemoveSchedule" },
		new object[] { "GetSchedules" },
		new object[] { "Poll" },
	};

	/// <summary>
	/// A Save serializes the settings graph while holding SyncRoot. Every scheduler access to
	/// <c>Settings.Schedules</c> must therefore wait for SyncRoot: while another thread holds it, the call must block.
	/// (A race test cannot show this reliably: System.Text.Json walks a List by index, so the symptom is torn
	/// data, not an exception.)
	/// </summary>
	[Theory]
	[MemberData(nameof(Operations))]
	public async Task ScheduleOperation_WaitsForSettingsSyncRoot(string operation)
	{
		var settings  = new AppSettingsService(_tempDir);
		var scheduler = new SchedulerService(settings, new FakeTurnService(), new FakeSessionRegistry());
		scheduler.AddSchedule(new ScheduleModel
		{
			ScheduleId = "seed", TargetNodeId = "n", Prompt = "p", Kind = ScheduleKind.Delay,
			FireAtUtc = DateTimeOffset.UtcNow.AddHours(1),
		});

		var holding  = new ManualResetEventSlim();
		var release  = new ManualResetEventSlim();
		var holder   = new Thread(() =>
		{
			lock (settings.SyncRoot) { holding.Set(); release.Wait(); }
		});
		holder.Start();
		holding.Wait();

		Task call;
		try
		{
			call = Task.Run(() =>
			{
				switch (operation)
				{
					case "AddSchedule":
						scheduler.AddSchedule(new ScheduleModel
						{
							ScheduleId = "x", TargetNodeId = "n", Prompt = "p", Kind = ScheduleKind.Delay,
							FireAtUtc = DateTimeOffset.UtcNow.AddHours(1),
						});
						break;
					case "AddOrMergeMailboxDelivery": scheduler.AddOrMergeMailboxDelivery("sup", "w", "r"); break;
					case "RemoveSchedule":            scheduler.RemoveSchedule("seed"); break;
					case "GetSchedules":              scheduler.GetSchedules(); break;
					case "Poll":                      scheduler.Poll(); break;
				}
			});
			await Task.WhenAny(call, Task.Delay(500));
			// Save() would also block on the held lock, so look at the list itself: it must be untouched.
			Assert.Single(settings.Settings.Schedules);
			Assert.Equal("seed", settings.Settings.Schedules[0].ScheduleId);
			if (operation is "GetSchedules" or "Poll")
				Assert.False(call.IsCompleted, $"{operation} read Settings.Schedules without waiting for SyncRoot");
		}
		finally { release.Set(); holder.Join(); }

		await call.WaitAsync(TimeSpan.FromSeconds(10)); // proceeds once the lock is released
	}

	// ── Robustness item 0.6/4: faults on the scheduler's fire-and-forget paths ─────────────────

	private static ScheduleModel BadCron(MissedFirePolicy policy = MissedFirePolicy.FireOnce) => new()
	{
		ScheduleId = "bad", TargetNodeId = "n", Prompt = "p", Kind = ScheduleKind.Cron,
		CronExpression = "not a cron", FireAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5), MissedFirePolicy = policy,
	};

	[Fact]
	public void Start_PersistedScheduleWithUnparsableCron_DoesNotAbortStartup()
	{
		var settings = new AppSettingsService(_tempDir);
		settings.Settings.Schedules.Add(BadCron(MissedFirePolicy.Skip));
		var scheduler = new SchedulerService(settings, new FakeTurnService(), new FakeSessionRegistry());

		var ex = Record.Exception(scheduler.Start);
		scheduler.Stop();

		Assert.Null(ex);
		Assert.Empty(settings.Settings.Schedules); // can never fire correctly: dropped, not retried forever
	}

	[Fact]
	public async Task Poll_ScheduleWithUnparsableCron_IsDroppedInsteadOfRefiringEveryPoll()
	{
		var settings = new AppSettingsService(_tempDir);
		var registry = new FakeSessionRegistry();
		registry.AddNode(new SessionNodeModel { NodeId = "n", Name = "n", FileName = "n.txt", WorkingDirectory = @"C:\proj" });
		settings.Settings.Schedules.Add(BadCron());
		var scheduler = new SchedulerService(settings, new FakeTurnService(), registry);

		scheduler.Poll();

		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && settings.Settings.Schedules.Count > 0)
			await Task.Delay(20);
		Assert.Empty(settings.Settings.Schedules);
	}

	// ── Review fixes R1/R2 ───────────────────────────────────────────────────────

	/// <summary>R1: 29 Feb fired on 29 Feb 2096 next matches 8 years later; that is not an invalid expression.</summary>
	[Fact]
	public void Poll_LeapDayCron_RearmsFarAheadInsteadOfBeingRemoved()
	{
		var settings = new AppSettingsService(_tempDir);
		var registry = new FakeSessionRegistry();
		registry.AddNode(new SessionNodeModel { NodeId = "n", Name = "n", FileName = "n.txt", WorkingDirectory = @"C:\proj" });
		var leap = new ScheduleModel
		{
			ScheduleId = "leap", TargetNodeId = "n", Prompt = "p", Kind = ScheduleKind.Cron,
			CronExpression = "0 0 29 2 *", FireAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
		};
		settings.Settings.Schedules.Add(leap);
		var scheduler = new SchedulerService(settings, new FakeTurnService(), registry);

		scheduler.Poll();

		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && leap.FireCount == 0)
			Thread.Sleep(20);
		Assert.Contains(leap, settings.Settings.Schedules);
		Assert.True(leap.FireAtUtc > DateTimeOffset.UtcNow);
		Assert.Equal(2, leap.FireAtUtc!.Value.Month);
		Assert.Equal(29, leap.FireAtUtc.Value.Day);
	}

	/// <summary>A valid expression that never matches (31 Feb) is parked, not rejected.</summary>
	[Fact]
	public void AddSchedule_ValidCronThatNeverMatches_IsParkedNotRejected()
	{
		var settings = new AppSettingsService(_tempDir);
		var scheduler = new SchedulerService(settings, new FakeTurnService(), new FakeSessionRegistry());

		// 31 Feb never exists: valid syntax, no occurrence. Parked (never due), not removed and not thrown.
		scheduler.AddSchedule(new ScheduleModel
		{
			ScheduleId = "never", TargetNodeId = "n", Prompt = "p", Kind = ScheduleKind.Cron, CronExpression = "0 0 31 2 *",
		});

		var saved = Assert.Single(settings.Settings.Schedules);
		Assert.Null(saved.FireAtUtc);
	}

	[Theory]
	[InlineData("*/0 * * * *")]
	[InlineData("0-30/0 * * * *")]
	[InlineData("* * * * */0")]
	[InlineData("*/-1 * * * *")]
	[InlineData("0-99999999 * * * *")]
	[InlineData("*/2147483647 * * * *")]
	public async Task AddSchedule_CronWithZeroStepOrBadRange_IsRejectedWithoutHanging(string cron)
	{
		var settings = new AppSettingsService(_tempDir);
		var scheduler = new SchedulerService(settings, new FakeTurnService(), new FakeSessionRegistry());
		var schedule = new ScheduleModel
		{
			ScheduleId = "z", TargetNodeId = "n", Prompt = "p", Kind = ScheduleKind.Cron, CronExpression = cron,
		};

		Exception? ex = null;
		var t = Task.Run(() => ex = Record.Exception(() => scheduler.AddSchedule(schedule)));

		await t.WaitAsync(TimeSpan.FromSeconds(10)); // TimeoutException = AddSchedule hung on the cron expression
		if (cron.StartsWith("*/2147483647"))
			Assert.Null(ex); // a huge step is valid: only minute 0 matches
		else
		{
			Assert.IsType<ArgumentException>(ex);
			Assert.Empty(settings.Settings.Schedules);
		}
	}

	[Fact]
	public async Task Start_PersistedScheduleWithZeroStep_IsDroppedAndDoesNotHang()
	{
		var settings = new AppSettingsService(_tempDir);
		var bad = BadCron(MissedFirePolicy.Skip);
		bad.CronExpression = "*/0 * * * *";
		settings.Settings.Schedules.Add(bad);
		var scheduler = new SchedulerService(settings, new FakeTurnService(), new FakeSessionRegistry());

		var t = Task.Run(scheduler.Start);

		await t.WaitAsync(TimeSpan.FromSeconds(10)); // TimeoutException = Start hung on a */0 cron expression
		scheduler.Stop();
		Assert.Empty(settings.Settings.Schedules);
	}
}
