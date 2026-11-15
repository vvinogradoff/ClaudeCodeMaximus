using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Tests.Contract;
using TessynDesktop.Tests.Services.Turns;

namespace TessynDesktop.Tests.Client;

/// <summary>The interactive turn methods and the typed turn notifications of <see cref="TessynClient"/> (P1.I stage 3), against a real loopback daemon.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TurnClientTests : IDisposable
{
	private readonly TurnWireEnv _env = new();

	public void Dispose() => _env.Dispose();

	private TessynClient NewClient() =>
		new(new Uri(_env.Host.BaseUrl + Constants.Agent.TdcpEndpointPath), TdcpTestHost.Token);

	[Fact]
	public async Task TurnSendAsync_StartsATurn_AndTheTypedNotificationsDescribeIt()
	{
		await using var client = NewClient();
		var probe = new ClientProbe(client);
		await client.ConnectAsync();
		_env.Process.ScriptEvents(
			Evt.Assistant("Checking."),
			new ClaudeStreamEvent { Type = "tool_use", ToolUseId = "tu", ToolName = "Bash", ToolSummary = "dotnet test" },
			new ClaudeStreamEvent { Type = "tool_result", ToolUseId = "tu", ToolSummary = "ok", ToolIsError = false },
			Evt.Result("s", inputTokens: 7, outputTokens: 9, cost: 0.01));

		var sent = await client.TurnSendAsync(_env.NodeId, "Run the tests", clientRequestId: "req-1");

		Assert.True(sent.Accepted);
		Assert.False(sent.Queued);

		TurnStartedPayload? started = null;
		string text = string.Empty;
		var tools = new List<TurnToolPayload>();
		TurnUsagePayload? usage = null;
		TurnStatusPayload? status = null;
		TurnCompletedPayload? completed = null;
		while (completed == null)
		{
			var n = await probe.NextNotificationAsync();
			if (n.TryGetTurnStarted(out var s)) started = s;
			else if (n.TryGetTurnDelta(out var d)) text += d.Text;
			else if (n.TryGetTurnTool(out var t)) tools.Add(t);
			else if (n.TryGetTurnUsage(out var u)) usage = u;
			else if (n.TryGetTurnStatus(out var st)) status = st;
			else if (n.TryGetTurnCompleted(out var c)) completed = c;
		}

		Assert.Equal((sent.TurnId, _env.NodeId, "req-1"), (started!.TurnId, started.NodeId, started.ClientRequestId));
		Assert.Equal("Checking.", text);
		Assert.Equal([TurnToolStatus.Started, TurnToolStatus.Result], tools.Select(t => t.Status));
		Assert.All(tools, t => Assert.Equal("Bash", t.Name));
		Assert.Equal("dotnet test", tools[0].Summary);
		Assert.Equal((7, 9, 0.01), (usage!.InputTokens, usage.OutputTokens, usage.CostUsd));
		Assert.Equal(TurnState.Running, status!.State);
		Assert.Equal(sent.TurnId, completed.TurnId);
		Assert.False(completed.Cancelled);
	}

	[Fact]
	public async Task TurnStopAsync_StopsTheTurn_AndTheTypedCompletionSaysCancelled()
	{
		await using var client = NewClient();
		var probe = new ClientProbe(client);
		await client.ConnectAsync();
		_env.Process.Script((_, _, ct) => Task.Delay(Timeout.Infinite, ct));

		var sent = await client.TurnSendAsync(_env.NodeId, "long");
		await Services.Turns.Waits.Until(() => _env.Process.Calls.Count == 1);
		var stop = await client.TurnStopAsync(_env.NodeId);

		TurnCompletedPayload? completed = null;
		while (completed == null)
		{
			if ((await probe.NextNotificationAsync()).TryGetTurnCompleted(out var c))
				completed = c;
		}

		Assert.True(stop.Stopped);
		Assert.Equal((sent.TurnId, true), (completed.TurnId, completed.Cancelled));
	}

	[Fact]
	public async Task ARefusedSend_IsATypedRpcException()
	{
		await using var client = NewClient();
		await client.ConnectAsync();

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => client.TurnSendAsync(_env.NodeId, "   "));

		Assert.Equal(-32602, error.Code);
	}

	[Fact]
	public void TheTypedReaders_RejectOtherMethodsAndMalformedParams()
	{
		var other = new RemoteNotification("session.changed", new System.Text.Json.Nodes.JsonObject());
		var malformed = new RemoteNotification("turn.delta", new System.Text.Json.Nodes.JsonObject { ["seq"] = "not a number" });

		Assert.False(other.TryGetTurnDelta(out _));
		Assert.False(malformed.TryGetTurnDelta(out _));
		Assert.False(other.TryGetTurnCompleted(out _));
	}

	[Fact]
	public void ATurnCompleted_WithoutTurnId_ReadsAsNull_ForOlderDaemons()
	{
		var legacy = new RemoteNotification("turn.completed", new System.Text.Json.Nodes.JsonObject { ["nodeId"] = "n", ["cancelled"] = false });

		Assert.True(legacy.TryGetTurnCompleted(out var completed));
		Assert.Null(completed.TurnId);
	}
}
