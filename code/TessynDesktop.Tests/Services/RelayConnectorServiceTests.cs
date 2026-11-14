using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class RelayConnectorServiceTests : IDisposable
{
	private const string Token = "link-token-for-tests";

	private readonly string _tempDir =
		Path.Combine(Path.GetTempPath(), "TessynRelayConnectorTests_" + Guid.NewGuid().ToString("N"));
	private readonly LoopbackLinkServer _server = new();
	private readonly RelayConfigStore _store;
	private readonly RemoteEventBus _bus = new();
	private readonly TdcpDispatcher _dispatcher = new([new SampleRpcModule()]);

	public RelayConnectorServiceTests()
	{
		_store = new RelayConfigStore(Path.Combine(_tempDir, "web-relay.dat"));
	}

	public void Dispose()
	{
		_server.Dispose();
		_bus.Dispose();
		try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort cleanup */ }
	}

	private static RelayConnectorTimings FastTimings(TimeSpan? deadLink = null) => new(
		DeadLinkTimeout: deadLink ?? TimeSpan.FromSeconds(10),
		EventBatchWindow: TimeSpan.FromMilliseconds(120),
		BackoffResetAfter: TimeSpan.FromMinutes(1),
		ReconnectBackoff: [TimeSpan.FromMilliseconds(50)]);

	private RelayConnectorService Create(RelayConnectorTimings? timings = null) =>
		new(_store, _dispatcher, _bus, new RelayHttpClientFactory(), timings ?? FastTimings());

	private void Configure(bool enabled = true) =>
		_store.Save(new RelayConfigModel { Enabled = enabled, Url = _server.Url, Token = Token });

	private static async Task WaitUntilAsync(Func<bool> condition, string because, int timeoutMs = 8000)
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException("Timed out waiting for: " + because);
			await Task.Delay(20);
		}
	}

	private static string Frame(string json) => "data: " + json + "\n\n";

	private async Task<RelayConnectorService> StartConnectedAsync(RelayConnectorTimings? timings = null)
	{
		Configure();
		var service = Create(timings);
		service.Start();
		await WaitUntilAsync(() => service.Status.State == RelayState.Connected, "Connected");
		return service;
	}

	[Fact]
	public void Start_NoConfigFile_IsNoOp()
	{
		var service = Create();

		service.Start();

		Assert.Equal(RelayState.Disabled, service.Status.State);
		Assert.Equal(0, _server.Connections);
		service.Stop();
	}

	[Fact]
	public async Task Start_ConfigDisabled_IsNoOp()
	{
		Configure(enabled: false);
		var service = Create();

		service.Start();
		await Task.Delay(200);

		Assert.Equal(RelayState.Disabled, service.Status.State);
		Assert.Equal(0, _server.Connections);
	}

	[Fact]
	public void Start_InvalidUrl_ErrorWithoutThrowing()
	{
		_store.Save(new RelayConfigModel { Enabled = true, Url = "not a url", Token = Token });
		var service = Create();

		service.Start();

		Assert.Equal(RelayState.Error, service.Status.State);
		Assert.Equal(0, _server.Connections);
	}

	[Fact]
	public async Task Connect_SendsBearerAndDaemonHeaders_AndReportsConnected()
	{
		var service = await StartConnectedAsync();
		try
		{
			Assert.True(_server.StreamHeaders.TryPeek(out var headers));
			Assert.Equal("Bearer " + Token, headers.Authorization);
			Assert.Equal(Environment.MachineName, headers.Daemon);
			Assert.Equal("1", headers.Protocol);
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task Request_IsDispatched_AndReplyPostedWithSameId()
	{
		var service = await StartConnectedAsync();
		try
		{
			_server.Send(Frame("""{"jsonrpc":"2.0","id":"r-7","method":"sample.echo","params":{"name":"hello"}}"""));

			await WaitUntilAsync(() => !_server.Replies.IsEmpty, "reply POST");

			Assert.True(_server.Replies.TryPeek(out var body));
			var batch = JsonNode.Parse(body)!.AsArray();
			Assert.Single(batch);
			Assert.Equal("r-7", batch[0]!["id"]!.GetValue<string>());
			Assert.Equal("hello", batch[0]!["result"]!["name"]!.GetValue<string>());
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task NonRemoteMethod_IsAnsweredWithMethodNotFound()
	{
		var service = await StartConnectedAsync();
		try
		{
			_server.Send(Frame("""{"jsonrpc":"2.0","id":"r-1","method":"sample.local"}"""));

			await WaitUntilAsync(() => !_server.Replies.IsEmpty, "reply POST");

			_server.Replies.TryPeek(out var body);
			var reply = JsonNode.Parse(body!)!.AsArray()[0]!;
			Assert.Equal("r-1", reply["id"]!.GetValue<string>());
			Assert.Equal(-32601, reply["error"]!["code"]!.GetValue<int>());
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task ServerNotificationsWithoutId_AndPingComments_AreNotAnswered()
	{
		var service = await StartConnectedAsync();
		try
		{
			_server.Send(": ping\n\n");
			_server.Send("id: 5\n" + Frame("""{"method":"resync","params":{}}"""));
			_server.Send(Frame("""{"method":"link.status","params":{"online":true}}"""));
			_server.Send(Frame("""{"jsonrpc":"2.0","id":"r-2","method":"sample.echo","params":{"name":"x"}}"""));

			await WaitUntilAsync(() => !_server.Replies.IsEmpty, "reply POST");
			await Task.Delay(200);

			Assert.Single(_server.Replies);
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task ManyConcurrentRequests_AreAllAnswered()
	{
		var service = await StartConnectedAsync();
		try
		{
			for (var i = 0; i < 25; i++)
				_server.Send(Frame($$"""{"jsonrpc":"2.0","id":"r-{{i}}","method":"sample.async"}"""));

			await WaitUntilAsync(() => _server.Replies.Count == 25, "25 replies");

			var ids = _server.Replies.Select(b => JsonNode.Parse(b)!.AsArray()[0]!["id"]!.GetValue<string>()).ToHashSet();
			Assert.Equal(25, ids.Count);
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task PublishedEvents_CarryNoSeq_WithoutAnEventLog()
	{
		var service = await StartConnectedAsync();
		try
		{
			_bus.Publish("tree.changed", new { version = 1 });
			await WaitUntilAsync(() => !_server.Events.IsEmpty, "event POST");

			_server.Events.TryPeek(out var body);
			Assert.Null(JsonNode.Parse(body!)!["events"]![0]!["seq"]);
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task PublishedEvents_CarryTheStampedSeq_WithAnEventLog()
	{
		using var log = new EventLog(_bus);
		Configure();
		var service = new RelayConnectorService(_store, _dispatcher, _bus, new RelayHttpClientFactory(), FastTimings(), log);
		service.Start();
		try
		{
			await WaitUntilAsync(() => service.Status.State == RelayState.Connected, "Connected");
			_bus.Publish("tree.changed", new { version = 1 });
			_bus.Publish("tags.changed", new { });
			await WaitUntilAsync(() => !_server.Events.IsEmpty, "event POST");
			await Task.Delay(300);

			_server.Events.TryPeek(out var body);
			var events = JsonNode.Parse(body!)!["events"]!.AsArray();
			Assert.Equal(2, events.Count);
			Assert.Equal(1, events[0]!["seq"]!.GetValue<long>());
			Assert.Equal(2, events[1]!["seq"]!.GetValue<long>());
			Assert.Equal(1, events[0]!["params"]!["version"]!.GetValue<int>());
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task PublishedEvents_AreBatchedIntoOnePost()
	{
		var service = await StartConnectedAsync();
		try
		{
			_bus.Publish("tree.changed", new { version = 1 });
			_bus.Publish("tags.changed", new { });
			_bus.Publish("index.changed", new { });

			await WaitUntilAsync(() => !_server.Events.IsEmpty, "event POST");
			await Task.Delay(300);

			Assert.Single(_server.Events);
			_server.Events.TryPeek(out var body);
			var events = JsonNode.Parse(body!)!["events"]!.AsArray();
			Assert.Equal(3, events.Count);
			Assert.Equal("tree.changed", events[0]!["method"]!.GetValue<string>());
			Assert.Equal(1, events[0]!["params"]!["version"]!.GetValue<int>());
			Assert.Equal("index.changed", events[2]!["method"]!.GetValue<string>());
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task EntriesAppended_IsNeverRelayed_ButSessionAppendedStillIs()
	{
		var service = await StartConnectedAsync();
		try
		{
			_bus.Publish("entries.appended", new { nodeId = "n", fileId = "f", fromSeq = 0, toSeq = 1 });
			_bus.Publish("session.appended", new { nodeId = "n", epoch = 0 });

			await WaitUntilAsync(() => !_server.Events.IsEmpty, "event POST");
			await Task.Delay(300);

			var methods = _server.Events.SelectMany(b => JsonNode.Parse(b)!["events"]!.AsArray()).Select(e => e!["method"]!.GetValue<string>()).ToList();
			Assert.Equal(["session.appended"], methods);
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task EventsPublishedWhileNotConnected_AreDropped()
	{
		Configure();
		var service = Create();
		_bus.Publish("tree.changed", new { version = 1 });
		service.Start();
		try
		{
			await WaitUntilAsync(() => service.Status.State == RelayState.Connected, "Connected");
			await Task.Delay(400);

			Assert.Empty(_server.Events);
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task SilentLink_TimesOutAndReconnects()
	{
		var service = await StartConnectedAsync(FastTimings(deadLink: TimeSpan.FromMilliseconds(400)));
		try
		{
			await WaitUntilAsync(() => _server.Connections >= 2, "second connection after the heartbeat timeout");
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task Pings_KeepTheLinkAlive()
	{
		var service = await StartConnectedAsync(FastTimings(deadLink: TimeSpan.FromMilliseconds(1500)));
		try
		{
			// Pings for longer than the dead-link timeout, at a tenth of it: a stall of the test thread on a loaded machine
			// would have to last over a second to look like a dead link.
			for (var i = 0; i < 20; i++)
			{
				_server.Send(": ping\n\n");
				await Task.Delay(100);
			}

			Assert.Equal(1, _server.Connections);
			Assert.Equal(RelayState.Connected, service.Status.State);
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task ServerClosingTheStream_Reconnects()
	{
		var service = await StartConnectedAsync();
		try
		{
			_server.CloseStream();

			await WaitUntilAsync(() => _server.Connections >= 2, "reconnect");
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task Unauthorized_ReportsErrorAndKeepsRetrying()
	{
		_server.StreamStatus = 401;
		Configure();
		var service = Create();
		service.Start();
		try
		{
			await WaitUntilAsync(() => service.Status is { State: RelayState.Error, LastError: "Link token rejected" }, "Error: Link token rejected");
			await WaitUntilAsync(() => _server.Connections >= 2, "retry after back-off");
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task Recovery_AfterError_ReturnsToConnected()
	{
		_server.StreamStatus = 500;
		Configure();
		var service = Create();
		service.Start();
		try
		{
			await WaitUntilAsync(() => service.Status.State == RelayState.Error, "Error");

			_server.StreamStatus = null;

			await WaitUntilAsync(() => service.Status.State == RelayState.Connected, "Connected again");
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task Stop_EndsTheLoop_AndStopsRelayingEvents()
	{
		var service = await StartConnectedAsync();

		service.Stop();

		Assert.Equal(RelayState.Disabled, service.Status.State);
		var connections = _server.Connections;
		_bus.Publish("tree.changed", new { version = 2 });
		await Task.Delay(400);
		Assert.Empty(_server.Events);
		Assert.Equal(connections, _server.Connections);
	}

	[Fact]
	public async Task StopThenStart_AndRestart_Work_AndStatusChangedFires()
	{
		var states = new System.Collections.Concurrent.ConcurrentQueue<RelayState>();
		Configure();
		var service = Create();
		service.StatusChanged += s => states.Enqueue(s.State);

		service.Start();
		await WaitUntilAsync(() => service.Status.State == RelayState.Connected, "Connected");
		service.Restart();
		await WaitUntilAsync(() => service.Status.State == RelayState.Connected && _server.Connections >= 2, "Connected after restart");
		service.Stop();
		service.Stop();

		Assert.Contains(RelayState.Connecting, states);
		Assert.Contains(RelayState.Connected, states);
		Assert.Equal(RelayState.Disabled, states.Last());
	}

	[Fact]
	public async Task Restart_PicksUpNewConfig_AndDisabling_Disconnects()
	{
		var service = await StartConnectedAsync();

		Configure(enabled: false);
		service.Restart();

		Assert.Equal(RelayState.Disabled, service.Status.State);
		var connections = _server.Connections;
		await Task.Delay(200);
		Assert.Equal(connections, _server.Connections);
	}
}
