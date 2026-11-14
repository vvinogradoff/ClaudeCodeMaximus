using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// The headless turn path (scheduler, orchestration, <c>session.send</c>) reports the same progress as an engine turn (P1.I stage 3) - and
/// everything it persisted and reported before is unchanged (see <c>SessionTurnServiceTests</c>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTurnServiceProgressTests : IDisposable
{
	private readonly string _tempDir;
	private readonly AppSettingsService _appSettings;
	private readonly FakeProcessManagerForTurnService _process = new();
	private readonly FakeStreamingManagerForTurnService _streaming = new();
	private readonly InMemorySessionFileService _files = new();
	private readonly FakeRegistryForTurnService _registry = new();
	private readonly FakeProfileServiceForTurnService _profiles = new();
	private readonly AttentionTestEnv _attention = new();
	private readonly TagTestEnv _tags = new();
	private readonly RecordingProgressSink _sink = new();
	private readonly SessionTurnService _sut;

	public SessionTurnServiceProgressTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), $"turn-service-progress-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
		_appSettings = new AppSettingsService(_tempDir);
		_appSettings.Settings.SessionFilesRoot  = _tempDir;
		_appSettings.Settings.AgentToolsEnabled = false;

		_sut = new SessionTurnService(
			_process, _files, _appSettings, _profiles,
			new Lazy<IAgentMcpServer>(() => new FakeMcpServerForTurnService()),
			_registry, new RecordingSessionStatusService(), new FakeModelServiceForTurnService(),
			new Lazy<ITelegramBridgeService>(() => new FakeBridgeForTurnService()),
			_streaming, _attention.Service, _tags.Service,
			new ProfileLabelService(_profiles, _appSettings), [_sink]);
	}

	public void Dispose()
	{
		_attention.Dispose();
		_tags.Dispose();
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	private static SessionNodeModel Node() =>
		new() { NodeId = "node-1", Name = "Test", FileName = "node-1.txt", WorkingDirectory = @"C:\proj" };

	[Fact]
	public async Task AHeadlessTurn_ReportsStartedRunningDeltasToolsUsageFinished_AndTheCompletionCarriesItsId()
	{
		_process.EmitEvents = onEvent =>
		{
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "Working." });
			onEvent(new ClaudeStreamEvent { Type = "tool_use", ToolUseId = "t1", ToolName = "Grep", ToolSummary = "TODO" });
			onEvent(new ClaudeStreamEvent { Type = "tool_result", ToolUseId = "t1", ToolSummary = "3 matches" });
			onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "s1", InputTokens = 5, OutputTokens = 6, CostUsd = 0.1 });
			return Task.CompletedTask;
		};

		var result = await _sut.RunTurnAsync(Node(), "Hello", TurnSource.Web).Within();

		Assert.Equal(
			["started:-", "status:Running", "delta:Text:Working.", "tool:Started:Grep:t1:TODO", "tool:Result:Grep:t1:3 matches", "usage:5/6/0.1",
			 "finished:cancelled=False:error=null:ran=True"],
			_sink.Lines());
		Assert.Equal("Working.", result.AssistantText);
		Assert.Equal([_sink.Items[0].TurnId], _files.CompletionTurnIds);
		Assert.Equal("completed:cancelled=False:error=null", _files.Log[^1]);
	}

	[Fact]
	public async Task AHeadlessTurnsPersistence_IsUnchangedByTheNewEvents()
	{
		_process.EmitEvents = onEvent =>
		{
			onEvent(new ClaudeStreamEvent { Type = "tool_use", ToolUseId = "t", ToolName = "Bash" });
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "answer" });
			onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "s" });
			return Task.CompletedTask;
		};

		await _sut.RunTurnAsync(Node(), "Q", TurnSource.Web).Within();

		Assert.Equal(["append:USER:Q", "append:ASSISTANT:answer", "completed:cancelled=False:error=null"], _files.Log);
	}

	[Fact]
	public async Task AnErrorResult_IsReportedInFinished_AndInTheCompletion()
	{
		_process.EmitEvents = onEvent =>
		{
			onEvent(new ClaudeStreamEvent { Type = "result", IsError = true, Content = "Usage limit reached" });
			return Task.CompletedTask;
		};

		await _sut.RunTurnAsync(Node(), "Q", TurnSource.Scheduled).Within();

		Assert.Equal("finished:cancelled=False:error=Usage limit reached:ran=True", _sink.Lines()[^1]);
		Assert.Equal("completed:cancelled=False:error=Usage limit reached", _files.Log[^1]);
	}

	[Fact]
	public async Task ACancelledHeadlessTurn_ReportsFinishedCancelled()
	{
		_process.EmitEvents = async _ => await Task.Delay(Timeout.Infinite, new CancellationTokenSource(100).Token);

		var result = await _sut.RunTurnAsync(Node(), "Q", TurnSource.Web).Within();

		Assert.Equal("Cancelled", result.ErrorMessage);
		Assert.Equal("finished:cancelled=True:error=null:ran=True", _sink.Lines()[^1]);
	}

	[Fact]
	public async Task AHeadlessTurnStoppedWhileQueued_ReportsFinishedWithRanFalse()
	{
		var node = Node();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_process.EmitEvents = async _ => await gate.Task;

		var first = _sut.RunTurnAsync(node, "one", TurnSource.Web);
		await Waits.Until(() => _process.Calls.Count == 1);
		using var cts = new CancellationTokenSource();
		var second = _sut.RunTurnAsync(node, "two", TurnSource.Web, cts.Token);
		await Waits.Until(() => _sink.Lines().Contains("status:Queued"));

		cts.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
		gate.SetResult();
		await first.Within();

		var finished = _sink.Items.OfType<TessynDesktop.Models.Turns.TurnFinishedProgress>().ToList();
		Assert.Equal([false, true], finished.Select(f => f.Ran));
		Assert.True(finished[0].Cancelled);
		Assert.Single(_files.CompletionTurnIds);
	}

	[Fact]
	public async Task AHeadlessTurnRedirectedIntoALiveStreamingProcess_ReportsTheStreamsEventsUnderItsOwnId()
	{
		_streaming.AliveResult = true;
		_streaming.EmitTurnEvents = onEvent =>
		{
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "from the stream" });
			onEvent(new ClaudeStreamEvent { Type = "tool_use", ToolUseId = "t", ToolName = "Read", ToolSummary = "a.txt" });
			onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "s" });
			return Task.CompletedTask;
		};

		await _sut.RunTurnAsync(Node(), "Q", TurnSource.Web).Within();

		Assert.Equal(
			["started:-", "status:Running", "delta:Text:from the stream", "tool:Started:Read:t:a.txt", "finished:cancelled=False:error=null:ran=True"],
			_sink.Lines());
	}
}
