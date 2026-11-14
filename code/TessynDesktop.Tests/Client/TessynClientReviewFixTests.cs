using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Tests.Client;

/// <summary>Regression tests for the P2.A review findings (resync seq, malformed frames, timeouts, state races, backoff).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynClientReviewFixTests
{
	private static readonly Uri Endpoint = new("http://127.0.0.1:1/tdcp");

	private static readonly TessynClientOptions FastRetry = new()
	{
		InitialBackoff = TimeSpan.FromMilliseconds(50),
		MaxBackoff     = TimeSpan.FromMilliseconds(400),
	};

	private static TessynClient Scripted(ScriptedDaemonHandler handler, TessynClientOptions? options = null) =>
		new(Endpoint, "t", options ?? FastRetry, new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan });

	// #1: unknown-seq must replace the seq high-water mark, not max() with it.

	[Fact]
	public async Task After_an_unknown_seq_resync_the_events_of_the_new_run_are_not_dropped_as_duplicates()
	{
		var stale = new StaleHelloHandler();
		using var host = new ClientTestHost();
		await using var client = new TessynClient(host.Endpoint, ClientTestHost.Token, FastRetry, new HttpClient(stale) { Timeout = Timeout.InfiniteTimeSpan });
		var probe = new ClientProbe(client);
		await client.ConnectAsync();
		host.Publish(5);
		for (var i = 0; i < 5; i++)
			await probe.NextNotificationAsync();

		// The daemon restarts, but the retry's hello is answered with the old run's result: the stream GET then names the old run
		// and the new daemon answers unknown-seq with a head (2) below the client's old seq (5).
		stale.StaleHellos = 1;
		host.StopServer();
		await probe.WaitForStateAsync(TessynClientState.Reconnecting);
		host.RestartAsNewRun();
		host.Publish(2);

		var resync = await probe.NextResyncAsync();
		Assert.Equal(RemoteProtocol.ResyncReasons.UnknownSeq, resync.Reason);
		Assert.Equal(2, resync.EventSeq);
		await probe.WaitForStateAsync(TessynClientState.Connected);

		host.Publish(1, first: 2);
		var n = await probe.NextNotificationAsync();
		Assert.Equal(3, n.Seq);
		Assert.Equal(host.RunInfo.RunId, client.ServerInfo!.RunId);
	}

	// #2: a frame of the wrong shape is skipped and its seq passed, not replayed forever.

	[Fact]
	public async Task A_malformed_frame_is_skipped_and_the_reconnect_resumes_after_it()
	{
		var handler = new ScriptedDaemonHandler
		{
			StreamBody = number => number == 1
				? "id: 1\ndata: {\"jsonrpc\":\"2.0\",\"method\":5,\"params\":{}}\n\n"
					+ "id: 2\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"session.changed\",\"params\":{\"a\":1}}\n\n"
					+ "id: 3\ndata: 7\n\n"
					+ "id: 4\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"resync\",\"params\":{\"reason\":3,\"eventSeq\":9}}\n\n"
				: null,
		};
		await using var client = Scripted(handler);
		var probe = new ClientProbe(client);

		await client.ConnectAsync();

		var n = await probe.NextNotificationAsync();
		Assert.Equal(2, n.Seq);
		Assert.Equal(1, (int?)n.Params["a"]);

		// The first stream ended; the second request resumes after the last frame (4), so nothing is replayed.
		await WaitUntilAsync(() => handler.LastEventIds.Count >= 2);
		Assert.Equal(["0", "4"], handler.LastEventIds.Take(2));
		Assert.True(await probe.NotificationsStayQuietAsync(TimeSpan.FromMilliseconds(300)));
	}

	// #3: only the per-call timeout is a TimeoutException.

	[Fact]
	public async Task A_slow_daemon_gives_a_TimeoutException()
	{
		var handler = new ScriptedDaemonHandler { PostDelay = TimeSpan.FromSeconds(5) };
		await using var client = Scripted(handler, FastRetry with { RequestTimeout = TimeSpan.FromMilliseconds(100) });

		await Assert.ThrowsAsync<TimeoutException>(() => client.HelloAsync());
	}

	[Fact]
	public async Task A_cancellation_that_is_not_the_timeout_is_not_reported_as_a_timeout()
	{
		var handler = new ScriptedDaemonHandler { PostFailure = new TaskCanceledException("disposed underneath") };
		await using var client = Scripted(handler);

		var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.HelloAsync());

		Assert.IsNotType<TimeoutException>(error);
	}

	// #4: connect and state transitions are race-free.

	[Fact]
	public async Task Two_concurrent_connects_let_exactly_one_through_and_the_states_stay_in_order()
	{
		var handler = new ScriptedDaemonHandler();
		await using var client = Scripted(handler);
		var states = new List<TessynClientState>();
		client.StateChanged += (_, e) =>
		{
			lock (states)
				states.Add(e.Current);
		};

		var results = await Task.WhenAll(Attempt(client), Attempt(client));

		Assert.Equal(1, results.Count(ok => ok));
		await client.DisposeAsync();
		lock (states)
			Assert.Equal([TessynClientState.Connecting, TessynClientState.Connected, TessynClientState.Disconnected], states);
	}

	[Fact]
	public async Task Disposing_while_connecting_neither_throws_out_of_the_stream_task_nor_hangs()
	{
		for (var i = 0; i < 20; i++)
		{
			var handler = new ScriptedDaemonHandler { PostDelay = TimeSpan.FromMilliseconds(i % 5) };
			var client = Scripted(handler);

			var connect = Task.Run(async () =>
			{
				try
				{
					await client.ConnectAsync();
				}
				catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException or InvalidOperationException)
				{
					// Losing the race against dispose is fine.
				}
			});
			await client.DisposeAsync();
			await connect.WaitAsync(ClientProbe.Wait);
			await client.DisposeAsync();

			Assert.Equal(TessynClientState.Disconnected, client.State);
		}
	}

	// #5: a daemon that accepts the stream and closes at once gets a growing backoff.

	[Fact]
	public async Task A_stream_that_closes_at_once_does_not_reset_the_backoff()
	{
		var handler = new ScriptedDaemonHandler { StreamBody = _ => string.Empty };
		await using var client = Scripted(handler);

		await client.ConnectAsync();
		await WaitUntilAsync(() => handler.StreamRequestTimes.Count >= 5);

		var times = handler.StreamRequestTimes;
		var firstGap = times[1] - times[0];
		var lastGap  = times[4] - times[3];
		Assert.True(lastGap >= TimeSpan.FromMilliseconds(250), $"backoff did not grow: first gap {firstGap.TotalMilliseconds:0} ms, fourth {lastGap.TotalMilliseconds:0} ms");
	}

	private static async Task<bool> Attempt(TessynClient client)
	{
		try
		{
			await client.ConnectAsync();
			return true;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + ClientProbe.Wait;
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException("Condition not reached in time");
			await Task.Delay(20);
		}
	}
}
