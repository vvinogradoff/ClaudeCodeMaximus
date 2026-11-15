using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// R1: the up-to-3 s acknowledgement wait of the toast fan-out must not hold the thread that started the tool call (which can be the accept loop of the
/// MCP server): a hung presenting client delays its own notify_user result, and nothing else.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class NotifyUserOffLoopTests : IDisposable
{
	private static readonly TimeSpan AckTimeout = TimeSpan.FromMilliseconds(1500);

	private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"cmx-notify-offloop-{Guid.NewGuid():N}");
	private readonly ClientPresenceRegistry _presence = new();
	private readonly RemoteEventBus _bus = new();
	private readonly AttentionTestEnv _attention = new();
	private readonly AgentMcpServer _server;

	public NotifyUserOffLoopTests()
	{
		Directory.CreateDirectory(_tempDir);
		var settings = new AppSettingsService(_tempDir);
		var fanOut = new NotificationFanOut(new FakeNotificationService(), _presence, _bus, settings, new NotificationAckBook(), ackTimeout: AckTimeout);
		_server = new AgentMcpServer(
			settings,
			new Lazy<ISessionTurnService>(() => new FakeTurnService()),
			new Lazy<ISchedulerService>(() => new RecordingScheduler()),
			new FakeSessionRegistry(),
			fileService: null!, modelService: null!, tdcpServer: null!, configMerger: null!,
			new FakeTelegramBridge(), fanOut, new FakeDirectoryLabelService(), _attention.Service, TagTestEnv.Unused());
	}

	public void Dispose()
	{
		_bus.Dispose();
		_attention.Dispose();
		Directory.Delete(_tempDir, recursive: true);
	}

	private static SessionNodeModel Node(string id, int depth) => new()
	{
		NodeId = id, Name = "Session-" + id, FileName = id + ".txt", WorkingDirectory = @"C:\Proj\" + id, OrchestrationDepth = depth,
	};

	private Task<JsonNode> Notify(SessionNodeModel caller) =>
		_server.HandleToolCallAsync(
			new JsonObject { ["name"] = "notify_user", ["arguments"] = new JsonObject { ["message"] = "need you" } }, caller);

	[Fact]
	public async Task AHungPresentingClient_DoesNotHoldTheCallingThread_NorDelayAnUnrelatedCall()
	{
		_presence.Register("wpf", ["toast"]);
		using var stream = _presence.OpenStream("wpf"); // connected, never acknowledges

		// The first call returns its task at once (the caller is the accept loop in production), although its result takes the whole timeout.
		var watch = Stopwatch.StartNew();
		var hung = Notify(Node("top", depth: 0));
		var startedIn = watch.Elapsed;

		// An unrelated call from another session is answered while the first one still waits.
		var other = await Notify(Node("worker", depth: 1)).WaitAsync(TimeSpan.FromSeconds(5));
		var unrelatedDone = watch.Elapsed;

		Assert.True(startedIn < AckTimeout / 3, $"the call held its caller for {startedIn}");
		Assert.True(unrelatedDone < AckTimeout - TimeSpan.FromMilliseconds(300), $"an unrelated call was delayed to {unrelatedDone}");
		Assert.False(hung.IsCompleted);
		Assert.NotNull(other);

		await hung.WaitAsync(TimeSpan.FromSeconds(10)); // and the hung one still ends, with the host fallback
		Assert.True(watch.Elapsed >= AckTimeout - TimeSpan.FromMilliseconds(100));
	}
}
