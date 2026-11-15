using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services.Turns;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// The turn stream over a real loopback connection (P1.I stage 3): <c>turn.send</c> and <c>turn.stop</c> (loopback only), the capability, the
/// <c>turn.*</c> notifications on the local SSE stream with their ids, ordering, batching and the <c>turnId</c> of <c>turn.completed</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnStreamWireTests : IDisposable
{
	private readonly TurnWireEnv _env = new();

	public void Dispose() => _env.Dispose();

	private static ClaudeStreamEvent ToolUse(string id, string name, string? summary = null) =>
		new() { Type = "tool_use", ToolUseId = id, ToolName = name, ToolSummary = summary };

	private static ClaudeStreamEvent ToolResult(string id, string? summary = null) =>
		new() { Type = "tool_result", ToolUseId = id, ToolSummary = summary };

	private Task<JsonNode> SendAsync(string text, string? extra = null)
	{
		var p = new JsonObject { ["nodeId"] = _env.NodeId, ["text"] = text };
		if (extra != null)
			foreach (var (key, value) in JsonNode.Parse(extra)!.AsObject())
				p[key] = value?.DeepClone();
		return _env.Host.CallAsync("turn.send", p);
	}

	[Fact]
	public async Task Hello_AnnouncesTheTurnStreamCapability_WhenTheEngineIsWired()
	{
		var result = (await _env.Host.CallAsync("client.hello"))["result"]!;

		var capabilities = result["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
		Assert.Equal(["events.local", "events.resume", "turn.stream"], capabilities);
	}

	[Fact]
	public async Task Hello_DoesNotAnnounceIt_WithoutTheEngine()
	{
		using var env = new TurnWireEnv(withEngineCapability: false);

		var result = (await env.Host.CallAsync("client.hello"))["result"]!;

		Assert.DoesNotContain("turn.stream", result["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()));
	}

	[Fact]
	public async Task TurnSend_ReturnsTheTurnIdAtOnce_AndTheTurnStreamsToTheLocalClient()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		_env.Process.ScriptEvents(
			Evt.Assistant("Let me look. "), ToolUse("tu_1", "Read", "a.txt"), ToolResult("tu_1", "contents"), Evt.Assistant("Found it."),
			Evt.Result("sess-1", inputTokens: 100, outputTokens: 50, cost: 0.25));

		var response = await SendAsync("Hello", """{"clientRequestId":"req-42"}""");
		var result = response["result"]!;
		var turnId = result["turnId"]!.GetValue<string>();

		Assert.Equal((true, false), (result["accepted"]!.GetValue<bool>(), result["queued"]!.GetValue<bool>()));
		Assert.False(string.IsNullOrEmpty(turnId));

		var frames = await TurnWireEnv.ReadUntilCompletedAsync(sse, turnId);

		Assert.Equal("turn.started", frames[0].Method);
		Assert.Equal(("req-42", _env.NodeId, turnId), (
			frames[0].Params!["clientRequestId"]!.GetValue<string>(), frames[0].Params!["nodeId"]!.GetValue<string>(), frames[0].Params!["turnId"]!.GetValue<string>()));
		Assert.All(frames, f => Assert.Equal(turnId, f.Params!["turnId"]!.GetValue<string>()));
		Assert.Equal("turn.completed", frames[^1].Method);
		Assert.False(frames[^1].Params!["cancelled"]!.GetValue<bool>());

		var text = string.Concat(frames.Where(f => f.Method == "turn.delta").Select(f => f.Params!["text"]!.GetValue<string>()));
		Assert.Equal("Let me look. Found it.", text);

		var tools = frames.Where(f => f.Method == "turn.tool").Select(f => (f.Params!["status"]!.GetValue<string>(), f.Params!["name"]!.GetValue<string>(), f.Params!["summary"]!.GetValue<string>())).ToList();
		Assert.Equal([("started", "Read", "a.txt"), ("result", "Read", "contents")], tools);

		var usage = frames.Single(f => f.Method == "turn.usage").Params!;
		Assert.Equal((100, 50, 0.25), (usage["inputTokens"]!.GetValue<int>(), usage["outputTokens"]!.GetValue<int>(), usage["costUsd"]!.GetValue<double>()));

		var states = frames.Where(f => f.Method == "turn.status").Select(f => f.Params!["state"]!.GetValue<string>()).ToList();
		Assert.Equal(["running"], states);
	}

	[Fact]
	public async Task EveryFrameOfTheTurn_HasAnIncreasingSseId_AndTheTurnsNotificationsKeepTheirOrder()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		_env.Process.ScriptEvents(Evt.Assistant("a"), ToolUse("t", "Bash", "ls"), ToolResult("t"), Evt.Assistant("b"), Evt.Result());

		var turnId = (await SendAsync("Go"))["result"]!["turnId"]!.GetValue<string>();
		var frames = await TurnWireEnv.ReadUntilCompletedAsync(sse, turnId);

		Assert.Equal(frames.Select(f => f.Id).OrderBy(i => i), frames.Select(f => f.Id));
		Assert.Equal(frames.Count, frames.Select(f => f.Id).Distinct().Count());

		var order = frames.Select(f => f.Method == "turn.tool" ? "tool:" + f.Params!["status"]!.GetValue<string>() : f.Method).ToList();
		Assert.True(order.IndexOf("tool:started") < order.IndexOf("tool:result"));
		Assert.Equal("turn.completed", order[^1]);
		Assert.Equal(1, order.Count(m => m == "turn.completed"));
	}

	[Fact]
	public async Task TheTurnIsPersistedByTheEngine_TheDeltasAreOnlyAPreview()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		_env.Process.ScriptEvents(Evt.Assistant("the answer"), Evt.Result("s"));

		var turnId = (await SendAsync("Question"))["result"]!["turnId"]!.GetValue<string>();
		await TurnWireEnv.ReadUntilCompletedAsync(sse, turnId);

		Assert.Equal(["append:USER:Question", "append:ASSISTANT:the answer", "completed:cancelled=False:error=null"], _env.Files.Log);
		Assert.Equal([turnId], _env.Files.CompletionTurnIds);
	}

	[Fact]
	public async Task ASecondSendWhileATurnRuns_IsQueued_AndReportsQueuedThenRunning()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_env.Process.Script(async (_, onEvent, _) =>
		{
			await gate.Task;
			onEvent(Evt.Assistant("first"));
		});
		_env.Process.ScriptEvents(Evt.Assistant("second"));

		var first = (await SendAsync("one"))["result"]!;
		await Waits.Until(() => _env.Process.Calls.Count == 1);
		var second = (await SendAsync("two"))["result"]!;
		var secondId = second["turnId"]!.GetValue<string>();

		Assert.False(first["queued"]!.GetValue<bool>());
		Assert.True(second["queued"]!.GetValue<bool>());

		// The first turn is released only once the second one has been seen waiting.
		var frames = new System.Collections.Generic.List<SseTestFrame>();
		while (!(frames.Count > 0 && IsStatus(frames[^1], secondId, "queued")))
			frames.Add(await sse.NextAsync());

		gate.SetResult();
		frames.AddRange(await TurnWireEnv.ReadUntilCompletedAsync(sse, secondId));

		var secondStates = frames.Where(f => IsStatus(f, secondId, null)).Select(f => f.Params!["state"]!.GetValue<string>()).ToList();
		Assert.Equal(["queued", "running"], secondStates);
	}

	private static bool IsStatus(SseTestFrame frame, string turnId, string? state) =>
		frame.Method == "turn.status" && frame.Params!["turnId"]!.GetValue<string>() == turnId
		&& (state == null || frame.Params["state"]!.GetValue<string>() == state);

	[Fact]
	public async Task TurnStop_StopsTheRunningTurn_AndTheCompletionIsCancelledWithTheSameTurnId()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		_env.Process.Script((_, _, ct) => Task.Delay(Timeout.Infinite, ct));

		var turnId = (await SendAsync("long job"))["result"]!["turnId"]!.GetValue<string>();
		await Waits.Until(() => _env.Process.Calls.Count == 1);

		var stop = await _env.Host.CallAsync("turn.stop", new JsonObject { ["nodeId"] = _env.NodeId });
		var frames = await TurnWireEnv.ReadUntilCompletedAsync(sse, turnId);

		Assert.True(stop["result"]!["stopped"]!.GetValue<bool>());
		Assert.True(frames[^1].Params!["cancelled"]!.GetValue<bool>());
		Assert.Contains("append:SYSTEM:[Interrupted by user]", _env.Files.Log);
	}

	[Fact]
	public async Task TurnStop_WithNothingRunning_AnswersNotStopped()
	{
		var stop = await _env.Host.CallAsync("turn.stop", new JsonObject { ["nodeId"] = _env.NodeId });

		Assert.False(stop["result"]!["stopped"]!.GetValue<bool>());
	}

	[Fact]
	public async Task AQueuedTurnStoppedBeforeItRan_StillEndsWithATurnCompleted_CarryingItsId()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		_env.Process.Script((_, _, ct) => Task.Delay(Timeout.Infinite, ct));

		await SendAsync("one");
		await Waits.Until(() => _env.Process.Calls.Count == 1);
		var secondId = (await SendAsync("two"))["result"]!["turnId"]!.GetValue<string>();

		// Stop only once the second turn was seen waiting behind the first.
		var frames = new System.Collections.Generic.List<SseTestFrame>();
		while (!(frames.Count > 0 && IsStatus(frames[^1], secondId, "queued")))
			frames.Add(await sse.NextAsync());

		await _env.Host.CallAsync("turn.stop", new JsonObject { ["nodeId"] = _env.NodeId });
		frames.AddRange(await TurnWireEnv.ReadUntilCompletedAsync(sse, secondId));

		Assert.Equal("turn.completed", frames[^1].Method);
		Assert.True(frames[^1].Params!["cancelled"]!.GetValue<bool>());
		Assert.Equal(1, _env.Process.Calls.Count);
	}

	[Theory]
	[InlineData("turn.send")]
	[InlineData("turn.stop")]
	public async Task TheTurnMethods_AreLoopbackOnly_TheRelayAnswersMethodNotFound(string method)
	{
		var body = new JsonObject
		{
			["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method,
			["params"] = new JsonObject { ["nodeId"] = _env.NodeId, ["text"] = "x" },
		}.ToJsonString();

		var response = await _env.Dispatcher.HandleTextAsync(body, TdcpTransport.Relay, CancellationToken.None);

		Assert.Equal(-32601, response!["error"]!["code"]!.GetValue<int>());
	}

	[Fact]
	public async Task TurnSend_Validation_BlankTextUnknownSessionUnknownModelAndMissingParams()
	{
		var blank = await SendAsync("   ");
		var unknownNode = await _env.Host.CallAsync("turn.send", new JsonObject { ["nodeId"] = "nope", ["text"] = "x" });
		var unknownModel = await SendAsync("x", """{"modelId":"no-such-model"}""");
		var missing = await _env.Host.CallAsync("turn.send", new JsonObject { ["nodeId"] = _env.NodeId });

		Assert.Equal(-32602, blank["error"]!["code"]!.GetValue<int>());
		Assert.Equal(-32000, unknownNode["error"]!["code"]!.GetValue<int>());
		Assert.Equal(-32602, unknownModel["error"]!["code"]!.GetValue<int>());
		Assert.Equal(-32602, missing["error"]!["code"]!.GetValue<int>());
		Assert.Empty(_env.Process.Calls);
		Assert.Empty(_env.Files.Log);
	}

	[Fact]
	public async Task TurnSend_ModelEffortAndProfile_ReachTheProcess_AndTheUserEntry()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		_env.Models.Models = [new ClaudeModelInfo("claude-x", "x", "X")];
		_env.ProfileService.Email = "me@example.com";
		_env.Process.ScriptEvents(Evt.Assistant("ok"), Evt.Result("s"));

		var turnId = (await SendAsync("Hi", """{"modelId":"claude-x","effort":"high","profileId":"default"}"""))["result"]!["turnId"]!.GetValue<string>();
		await TurnWireEnv.ReadUntilCompletedAsync(sse, turnId);

		var call = Assert.Single(_env.Process.Calls);
		Assert.Equal(("claude-x", "high"), (call.Model, call.Effort));
		Assert.Equal("[Sys] me@example.com", _env.Files.Entries[0].ProfileName);
		Assert.Equal(("claude-x", "high"), (_env.Files.Entries[1].ModelId, _env.Files.Entries[1].Effort));
	}

	[Fact]
	public async Task WithoutOptions_TheSessionsDirectoryAndAppDefaultsApply()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		_env.Settings.Settings.SelectedModelId = "claude-app-default";
		_env.Process.ScriptEvents(Evt.Result("s"));

		var turnId = (await SendAsync("Hi"))["result"]!["turnId"]!.GetValue<string>();
		await TurnWireEnv.ReadUntilCompletedAsync(sse, turnId);

		Assert.Equal("claude-app-default", Assert.Single(_env.Process.Calls).Model);
		Assert.Null(_env.Process.Calls[0].Effort);
	}

	[Fact]
	public async Task TheExistingSessionSend_StillWorksSideBySide_AsTheContractRequires()
	{
		// session.send is not part of this env's modules: the point is that turn.* does not shadow or rename anything of the 21.
		var methods = typeof(RemoteProtocol.Methods).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToList();

		Assert.Contains("session.send", methods);
		Assert.DoesNotContain("turn.send", methods);
		Assert.DoesNotContain("turn.stop", methods);
		Assert.Equal(25, methods.Count); // the 21 of the web contract, file.read and file.preview (Focus refs step 1), attachment.list and attachment.fetch (FR.31)
	}

	[Fact]
	public void TheLocalOnlyEvents_AreTheFiveStreamEvents_NotTurnCompleted()
	{
		Assert.Equal(["turn.started", "turn.delta", "turn.tool", "turn.status", "turn.usage"], RemoteProtocol.TurnStream.LocalOnlyEvents);
		Assert.DoesNotContain(RemoteProtocol.Events.TurnCompleted, RemoteProtocol.TurnStream.LocalOnlyEvents);
	}
}
