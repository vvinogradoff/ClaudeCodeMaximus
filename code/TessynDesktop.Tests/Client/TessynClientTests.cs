using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.Tests.Client;

/// <summary>
/// <see cref="TessynClient"/> against the real <see cref="TdcpServer"/> over loopback HTTP: handshake, typed calls, push
/// notifications, reconnect with resume, resync and authentication failures.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynClientTests
{
	private static readonly TessynClientOptions FastRetry = new()
	{
		InitialBackoff = TimeSpan.FromMilliseconds(50),
		MaxBackoff     = TimeSpan.FromMilliseconds(200),
	};

	private static TessynClient NewClient(ClientTestHost host, string? token = null) =>
		new(host.Endpoint, token ?? ClientTestHost.Token, FastRetry);

	// ── handshake ───────────────────────────────────────────────────────────────

	[Fact]
	public async Task Connect_performs_hello_and_exposes_the_capabilities()
	{
		using var host = new ClientTestHost();
		await using var client = NewClient(host);

		await client.ConnectAsync();

		Assert.Equal(TessynClientState.Connected, client.State);
		var info = client.ServerInfo!;
		Assert.Equal(host.RunInfo.RunId, info.RunId);
		Assert.Equal(RemoteProtocol.ProtocolVersion, info.ProtocolVersion);
		Assert.Equal(RemoteProtocol.MinProtocolVersion, info.MinProtocolVersion);
		Assert.Contains(RemoteProtocol.Capabilities.EventsLocal, info.Capabilities);
		Assert.Contains(RemoteProtocol.Capabilities.EventsResume, info.Capabilities);
		Assert.Equal(0, info.EventSeq);
	}

	[Fact]
	public async Task Connect_succeeds_so_the_client_sends_no_Origin_header()
	{
		// The daemon answers every request that carries an Origin header with HTTP 403 (P1.C), so a successful call proves there is none.
		using var host = new ClientTestHost();
		await using var client = NewClient(host);

		await client.ConnectAsync();
		var hello = await client.HelloAsync();

		Assert.Equal(host.RunInfo.RunId, hello.RunId);
	}

	[Fact]
	public async Task State_changes_are_reported_in_order()
	{
		using var host = new ClientTestHost();
		await using var client = NewClient(host);
		var states = new List<TessynClientState>();
		client.StateChanged += (_, e) => states.Add(e.Current);

		await client.ConnectAsync();
		await client.DisposeAsync();

		Assert.Equal([TessynClientState.Connecting, TessynClientState.Connected, TessynClientState.Disconnected], states);
	}

	[Fact]
	public async Task A_wrong_token_fails_the_connect_with_an_auth_exception_and_the_AuthFailed_state()
	{
		using var host = new ClientTestHost();
		await using var client = NewClient(host, token: "not-the-token");

		var error = await Assert.ThrowsAsync<TessynAuthException>(() => client.ConnectAsync());

		Assert.Equal(401, error.HttpStatus);
		Assert.Equal(TessynClientState.AuthFailed, client.State);
		Assert.Null(client.ServerInfo);
	}

	[Fact]
	public async Task An_unreachable_daemon_fails_the_connect_and_leaves_the_client_Disconnected()
	{
		var host = new ClientTestHost();
		var endpoint = host.Endpoint;
		host.Dispose();
		await using var client = new TessynClient(endpoint, ClientTestHost.Token, FastRetry);

		await Assert.ThrowsAsync<HttpRequestException>(() => client.ConnectAsync());

		Assert.Equal(TessynClientState.Disconnected, client.State);
	}

	[Fact]
	public async Task A_second_connect_while_connected_is_refused()
	{
		using var host = new ClientTestHost();
		await using var client = NewClient(host);
		await client.ConnectAsync();

		await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
	}

	[Fact]
	public async Task A_cancelled_token_cancels_the_call()
	{
		using var host = new ClientTestHost();
		await using var client = NewClient(host);
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.HelloAsync(cts.Token));
	}

	[Fact]
	public async Task Dispose_is_idempotent_and_later_calls_throw()
	{
		using var host = new ClientTestHost();
		var client = NewClient(host);
		await client.ConnectAsync();

		await client.DisposeAsync();
		await client.DisposeAsync();

		Assert.Equal(TessynClientState.Disconnected, client.State);
		await Assert.ThrowsAsync<ObjectDisposedException>(() => client.HelloAsync());
	}

	// ── typed calls ─────────────────────────────────────────────────────────────

	[Fact]
	public async Task TreeSnapshot_round_trips_and_answers_unchanged_for_the_current_version()
	{
		using var env = new RemoteTreeTestEnv();
		var dir = env.AddDirectory("proj");
		var sup = env.AddSession(dir, "Supervisor");
		env.AddSession(dir, "Worker", supervisorNodeId: sup.NodeId);
		using var host = new ClientTestHost((run, log) => new TdcpDispatcher(
		[
			new ClientRpcModule(runInfo: run, eventLog: log),
			new TreeRemoteRpcModule(new RemoteTreeReader(env.Settings), env.Registry, env.States, env.Activity, env.Version, new FakeDirectoryLabelService(), env.Index, env.FileSearch),
		]));
		await using var client = NewClient(host);
		await client.ConnectAsync();

		var snapshot = await client.TreeSnapshotAsync();

		Assert.NotNull(snapshot.Directories);
		var directory = Assert.Single(snapshot.Directories!);
		Assert.Contains(directory.Sessions, s => s.Name == "Supervisor");

		var again = await client.TreeSnapshotAsync(snapshot.Version);
		Assert.True(again.Unchanged);
		Assert.Equal(snapshot.Version, again.Version);

		var workers = await client.TreeWorkersAsync(sup.NodeId);
		Assert.Equal(sup.NodeId, workers.SupervisorId);
		Assert.Equal("Worker", Assert.Single(workers.Workers).Name);
	}

	[Fact]
	public async Task SessionEntries_and_SessionEntry_round_trip_and_a_rpc_error_surfaces_as_TessynRpcException()
	{
		using var env = new RemoteSessionEnv();
		env.Files.Set(env.Session.FileName, [User("hello", 1, "Work"), Assistant("world", 2, "claude-opus-5-5", "high")]);
		using var host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), env.Module]));
		await using var client = NewClient(host);
		await client.ConnectAsync();

		var page = await client.SessionEntriesAsync(env.Session.NodeId);

		Assert.Equal(2, page.Total);
		Assert.Equal(["hello", "world"], page.Entries.Select(e => e.Content));
		Assert.Equal(["user", "assistant"], page.Entries.Select(e => e.Role));

		var full = await client.SessionEntryAsync(env.Session.NodeId, 1, page.Epoch);
		Assert.Equal("world", full.Content);
		Assert.Equal("claude-opus-5-5", full.ModelId);

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => client.SessionGetAsync("no-such-session"));
		Assert.Equal(RemoteProtocol.ErrorCodes.DaemonError, error.Code);
		Assert.Equal("Session not found", error.Message);
	}

	[Fact]
	public async Task A_method_the_daemon_does_not_know_is_a_method_not_found_rpc_error()
	{
		using var host = new ClientTestHost(); // only client.hello is registered
		await using var client = NewClient(host);
		await client.ConnectAsync();

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => client.RecentListAsync());

		Assert.Equal(RemoteProtocol.ErrorCodes.MethodNotFound, error.Code);
	}

	// ── notifications ───────────────────────────────────────────────────────────

	[Fact]
	public async Task Notifications_arrive_in_order_with_their_seq()
	{
		using var host = new ClientTestHost();
		await using var client = NewClient(host);
		var probe = new ClientProbe(client);
		await client.ConnectAsync();

		host.Publish(3);

		for (var i = 1; i <= 3; i++)
		{
			var n = await probe.NextNotificationAsync();
			Assert.Equal(RemoteProtocol.Events.SessionChanged, n.Method);
			Assert.Equal(i, n.Seq);
			Assert.Equal("n" + (i - 1), (string?)n.Params["name"]);
		}
	}

	[Fact]
	public async Task Events_published_before_connecting_are_skipped_and_later_ones_are_delivered()
	{
		using var host = new ClientTestHost();
		host.Publish(2); // before the client exists: hello reports eventSeq 2, the stream starts after it
		await using var client = NewClient(host);
		var probe = new ClientProbe(client);

		await client.ConnectAsync();
		Assert.Equal(2, client.ServerInfo!.EventSeq);
		host.Publish(1, first: 2);

		var n = await probe.NextNotificationAsync();
		Assert.Equal(3, n.Seq);
		Assert.Equal("n2", (string?)n.Params["name"]);
	}

	[Fact]
	public async Task A_dropped_stream_reconnects_and_resumes_without_loss_or_duplicates()
	{
		using var host = new ClientTestHost();
		await using var client = NewClient(host);
		var probe = new ClientProbe(client);
		await client.ConnectAsync();
		host.Publish(2);
		Assert.Equal(1, (await probe.NextNotificationAsync()).Seq);
		Assert.Equal(2, (await probe.NextNotificationAsync()).Seq);

		host.StopServer();
		await probe.WaitForStateAsync(TessynClientState.Reconnecting);
		host.Publish(3, first: 2); // while the daemon is unreachable
		host.StartServer();
		await probe.WaitForStateAsync(TessynClientState.Connected);

		for (long seq = 3; seq <= 5; seq++)
			Assert.Equal(seq, (await probe.NextNotificationAsync()).Seq);

		host.Publish(1, first: 5);
		Assert.Equal(6, (await probe.NextNotificationAsync()).Seq);
		Assert.True(await probe.NotificationsStayQuietAsync(TimeSpan.FromMilliseconds(300)));
	}

	[Fact]
	public async Task A_gap_the_ring_cannot_replay_raises_Resynced()
	{
		using var host = new ClientTestHost(logCapacity: 3);
		await using var client = NewClient(host);
		var probe = new ClientProbe(client);
		await client.ConnectAsync();
		host.Publish(1);
		await probe.NextNotificationAsync();

		host.StopServer();
		await probe.WaitForStateAsync(TessynClientState.Reconnecting);
		host.Publish(10, first: 1); // the ring keeps 3: seq 2 is gone
		host.StartServer();

		var resync = await probe.NextResyncAsync();

		Assert.Equal(RemoteProtocol.ResyncReasons.Gap, resync.Reason);
		Assert.Equal(11, resync.EventSeq);

		// The stream continues after the head.
		await probe.WaitForStateAsync(TessynClientState.Connected);
		host.Publish(1, first: 11);
		Assert.Equal(12, (await probe.NextNotificationAsync()).Seq);
	}

	[Fact]
	public async Task A_restarted_daemon_with_a_new_run_id_raises_Resynced_and_the_stream_follows_the_new_run()
	{
		using var host = new ClientTestHost();
		await using var client = NewClient(host);
		var probe = new ClientProbe(client);
		await client.ConnectAsync();
		var firstRun = client.ServerInfo!.RunId;
		host.Publish(5);
		for (var i = 0; i < 5; i++)
			await probe.NextNotificationAsync();

		host.StopServer();
		await probe.WaitForStateAsync(TessynClientState.Reconnecting);
		host.RestartAsNewRun();

		var resync = await probe.NextResyncAsync();

		Assert.Equal(ResyncedEventArgs.RunChangedReason, resync.Reason);
		Assert.NotEqual(firstRun, client.ServerInfo!.RunId);
		Assert.Equal(host.RunInfo.RunId, client.ServerInfo.RunId);

		// Seqs restart per run: the client must not be stuck waiting for something above the old run's seq 5.
		await probe.WaitForStateAsync(TessynClientState.Connected);
		host.Publish(1);
		var n = await probe.NextNotificationAsync();
		Assert.Equal(1, n.Seq);
	}

	[Fact]
	public async Task A_token_that_stops_being_accepted_ends_the_stream_in_AuthFailed()
	{
		using var host = new ClientTestHost();
		await using var client = NewClient(host);
		var probe = new ClientProbe(client);
		await client.ConnectAsync();

		host.StopServer();
		await probe.WaitForStateAsync(TessynClientState.Reconnecting);
		host.StartServer(token: "a-rotated-token");

		await probe.WaitForStateAsync(TessynClientState.AuthFailed);
		Assert.Equal(TessynClientState.AuthFailed, client.State);
	}
}
