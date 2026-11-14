using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Robustness item 0.6/1 (ISSUE.008 class): the topic maps in the settings graph are written under
/// <c>IAppSettingsService.SyncRoot</c> (and serialized by Save under it), so reading them without the lock
/// races a concurrent write (Dictionary corruption / wrong answer). Readers must take the same lock.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TelegramBridgeServiceTests
{
	/// <summary>Runs <paramref name="action"/> on a worker while another thread holds SyncRoot; returns whether it finished meanwhile.</summary>
	private async Task<bool> FinishesWhileSyncRootHeldAsync(Action action)
	{
		using var holding = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var holder = new Thread(() => { lock (_appSettings.SyncRoot) { holding.Set(); release.Wait(); } });
		holder.Start();
		holding.Wait();
		Task call;
		bool finished;
		try
		{
			call = Task.Run(action);
			finished = await Task.WhenAny(call, Task.Delay(500)) == call;
		}
		finally { release.Set(); holder.Join(); }
		await call.WaitAsync(TimeSpan.FromSeconds(10));
		return finished;
	}

	[Fact]
	public async Task EnsureTopicForSession_AlreadyMapped_ReadsTopicMapUnderSyncRoot()
	{
		MapTopic("node-1", 42);
		var svc = BuildReconcilingService();
		var node = MakeNode("node-1", "Fix login");

		var finished = await FinishesWhileSyncRootHeldAsync(() => svc.EnsureTopicForSessionAsync(node).GetAwaiter().GetResult());

		Assert.False(finished, "EnsureTopicForSessionAsync read TelegramTopicThreadIdsByNodeId without SyncRoot");
	}

	[Fact]
	public async Task Start_ReadsTopicMapUnderSyncRoot()
	{
		MapTopic("node-1", 42, "TestDir / Fix login");
		var svc = BuildReconcilingService();

		var finished = await FinishesWhileSyncRootHeldAsync(() => svc.Start());

		Assert.False(finished, "Start enumerated TelegramTopicThreadIdsByNodeId without SyncRoot");
		svc.Stop();
	}
}
