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
}
