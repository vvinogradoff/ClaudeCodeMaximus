using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Tests.Services.Turns;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// TC.ME.02 over the loopback wire: the desktop's <c>turn.send</c> keeps a model or effort pick on the session, and a later send without a pick runs
/// with it, which is the bug of FR.12.35 (the effort reverting to the CLI default once the client forgot its pick) — FR.12.35; UC.ME.01, UC.ME.02.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionModelSettingsWireTests : IDisposable
{
	private readonly TurnWireEnv _env = new();

	public void Dispose() => _env.Dispose();

	private async Task SendAndWaitAsync(SseTestClient sse, string text, string? extra = null)
	{
		var p = new JsonObject { ["nodeId"] = _env.NodeId, ["text"] = text };
		if (extra != null)
			foreach (var (key, value) in JsonNode.Parse(extra)!.AsObject())
				p[key] = value?.DeepClone();

		_env.Process.ScriptEvents(Evt.Assistant("ok"), Evt.Result("s"));
		var turnId = (await _env.Host.CallAsync("turn.send", p))["result"]!["turnId"]!.GetValue<string>();
		await TurnWireEnv.ReadUntilCompletedAsync(sse, turnId);
	}

	[Fact]
	public async Task TurnSend_WithEffortPick_SessionKeepsIt_AndTheNextSendWithoutPickUsesIt()
	{
		using var sse = await _env.Host.OpenStreamAsync();

		await SendAndWaitAsync(sse, "first", """{"effort":"max"}""");
		await SendAndWaitAsync(sse, "second");

		Assert.Equal("max", _env.Session.Effort);
		Assert.Equal("max", _env.Process.Calls[0].Effort);
		Assert.Equal("max", _env.Process.Calls[1].Effort);
	}

	[Fact]
	public async Task EngineTurn_NoPick_RunsWithSessionModelAndEffort()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		_env.Models.Models = [new ClaudeModelInfo("claude-x", "x", "X")];
		_env.Session.ModelId = "claude-x";
		_env.Session.Effort  = "low";

		await SendAndWaitAsync(sse, "hi");

		var call = Assert.Single(_env.Process.Calls);
		Assert.Equal(("claude-x", "low"), (call.Model, call.Effort));
	}

	[Fact]
	public async Task TurnSend_DefaultPick_StopsTheInheritedModel_AndStays()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		_env.Settings.Settings.SelectedModelId = "claude-app-default";

		await SendAndWaitAsync(sse, "first", """{"modelId":"default"}""");
		await SendAndWaitAsync(sse, "second");

		Assert.Equal("default", _env.Session.ModelId);
		Assert.Null(_env.Process.Calls[1].Model);
	}

	[Fact]
	public async Task TurnSend_PickEqualToTheSessionsValue_PinsNothing()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		_env.Models.Models = [new ClaudeModelInfo("claude-app-default", "x", "X")];
		_env.Settings.Settings.SelectedModelId = "claude-app-default";

		await SendAndWaitAsync(sse, "hi", """{"modelId":"claude-app-default","effort":"default"}""");

		Assert.Null(_env.Session.ModelId);
		Assert.Null(_env.Session.Effort);
	}
}
