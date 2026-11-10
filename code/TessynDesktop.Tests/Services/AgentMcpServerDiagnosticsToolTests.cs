using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>D1.7: the <c>diag_snapshot</c>, <c>diag_heap</c> and <c>diag_dump</c> cmx tools, built the way <see cref="AgentMcpServerTagToolTests"/> builds the server.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AgentMcpServerDiagnosticsToolTests : IDisposable
{
	private readonly string _tempDir;
	private readonly DiagnosticsTestEnv _diag = new();
	private readonly TagTestEnv _tags = new();
	private readonly AttentionTestEnv _attention = new();
	private readonly QueueingUiDispatcher _dispatcher = new();
	private readonly FakeRuntimeInspector _inspector = new();
	private readonly UiThreadTracker _ui = new();
	private readonly UiHangWatchdog _watchdog;
	private readonly AgentMcpServer _server;
	private readonly SessionNodeModel _node = TagTestEnv.Node("top");

	public AgentMcpServerDiagnosticsToolTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), $"cmx-diag-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
		_ui.CaptureCurrentThread();
		var operations = new OperationTimer(_ui, _diag.Clock, _diag.Sink.CreateLogger());
		_watchdog = new UiHangWatchdog(_diag.Settings, _dispatcher, _inspector, _diag.Store, operations, _ui, _diag.Clock, _diag.Sink.CreateLogger());
		_server = BuildServer(_inspector);
	}

	private AgentMcpServer BuildServer(IRuntimeInspector inspector)
	{
		var handler = new DiagnosticsToolHandler(new ProcessMetricsSampler(_diag.Settings, inspector, _diag.Store), _watchdog, inspector, _diag.Store);
		return new AgentMcpServer(
			new AppSettingsService(_tempDir),
			new Lazy<ISessionTurnService>(() => new FakeTurnService()),
			new Lazy<ISchedulerService>(() => new RecordingScheduler()),
			new FakeSessionRegistry(),
			new RecordingSessionFileService(),
			modelService: null!,
			tdcpServer: null!,
			configMerger: null!,
			new FakeTelegramBridge(),
			new FakeNotificationService(),
			new FakeDirectoryLabelService(),
			_attention.Service,
			_tags.Service,
			handler);
	}

	public void Dispose()
	{
		_diag.Dispose();
		_tags.Dispose();
		_attention.Dispose();
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	private Task<JsonNode> Call(string tool) =>
		_server.HandleToolCallAsync(new JsonObject { ["name"] = tool, ["arguments"] = new JsonObject() }, _node);

	private static string TextOf(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	private static JsonNode ToolsList() =>
		(JsonNode)typeof(AgentMcpServer).GetMethod("HandleToolsList", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;

	[Fact]
	public void ToolsList_AdvertisesTheThreeDiagTools()
	{
		var names = ToolsList()["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();

		Assert.Contains("diag_snapshot", names);
		Assert.Contains("diag_heap", names);
		Assert.Contains("diag_dump", names);
	}

	[Fact]
	public async Task DiagSnapshot_ReturnsMetricsAndWatchdogState()
	{
		var json = JsonNode.Parse(TextOf(await Call("diag_snapshot")))!;

		var metrics = json["Metrics"]!;
		Assert.True(metrics["PrivateMb"]!.GetValue<double>() > 0);
		Assert.NotNull(metrics["GcHeapMb"]);
		Assert.NotNull(metrics["NativeEstimateMb"]);
		Assert.NotNull(metrics["Gen2Collections"]);
		Assert.NotNull(metrics["ThreadPoolPendingWork"]);

		var watchdog = json["Watchdog"]!;
		Assert.False(watchdog["StalledNow"]!.GetValue<bool>());
		Assert.Equal(0, watchdog["CurrentStallMs"]!.GetValue<double>());
		Assert.Null(watchdog["LastStall"]);
		Assert.Null(watchdog["CurrentUiOperation"]);
		Assert.Equal(Environment.CurrentManagedThreadId, watchdog["UiThreadId"]!.GetValue<int>());
	}

	[Fact]
	public async Task DiagSnapshot_WhileUiIsStalled_StillAnswersAndReportsTheStall()
	{
		_watchdog.Tick();
		for (var i = 0; i < 6; i++)
		{
			_diag.Clock.Advance(TimeSpan.FromMilliseconds(250));
			_watchdog.Tick();
		}

		var json = JsonNode.Parse(TextOf(await Call("diag_snapshot")))!;

		Assert.True(json["Watchdog"]!["StalledNow"]!.GetValue<bool>());
		Assert.Equal(1500, json["Watchdog"]!["CurrentStallMs"]!.GetValue<double>());
		Assert.Equal(1, _dispatcher.PendingCount);
	}

	[Fact]
	public async Task DiagHeap_ReturnsPathAndTopLines()
	{
		var text = TextOf(await Call("diag_heap"));

		var path = Assert.Single(_inspector.HistogramPaths);
		Assert.Contains(path, text);
		Assert.Contains("line 1", text);
		Assert.Contains($"line {Constants.Diagnostics.HeapToolPreviewLines}", text);
		Assert.DoesNotContain($"line {Constants.Diagnostics.HeapToolPreviewLines + 1}\n", text + "\n");
	}

	[Fact]
	public async Task DiagDump_WhenDumpsDisabled_IsRefusedWithAClearMessage()
	{
		var server = BuildServer(new ClrMdRuntimeInspector(_ui, _diag.Settings));

		var ex = await Assert.ThrowsAnyAsync<Exception>(() => server.HandleToolCallAsync(
			new JsonObject { ["name"] = "diag_dump", ["arguments"] = new JsonObject() }, _node));

		Assert.Contains("disabled", ex.Message);
		Assert.Contains("DumpsEnabled", ex.Message);
	}
}
