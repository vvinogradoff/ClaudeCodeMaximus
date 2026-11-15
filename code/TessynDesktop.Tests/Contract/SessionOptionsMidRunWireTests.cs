using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Tests.Services.Turns;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// FR.11.12 through the daemon (P1.J1): <c>session.setOptions</c> on a session whose turn runs in the real <c>TurnEngine</c> sends the mid-run
/// correction on its own, not only through the desktop's observer. The "process" is the scripted fake: the turn blocks on its first launch,
/// and the correction is the second launch (a one-shot <c>--resume</c> process, as <c>TurnEngine.SendCorrection</c> spawns it).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionOptionsMidRunWireTests : IDisposable
{
	private readonly TreeMutationWireEnv _env = new();

	public void Dispose() => _env.Dispose();

	private async Task<JsonNode> StartBlockedTurnAsync()
	{
		_env.Session.ClaudeSessionId = "claude-1";
		_env.Turn.Process.Script((_, _, ct) => Task.Delay(Timeout.Infinite, ct));

		var sent = await _env.CallAsync("turn.send", new JsonObject { ["nodeId"] = _env.Session.NodeId, ["text"] = "long work" });
		Assert.Null(sent["error"]);
		await Waits.Until(() => _env.Turn.Process.Calls.Count == 1);
		return sent;
	}

	[Fact]
	public async Task ChangingAutoCommitWhileTheTurnRuns_SpawnsTheCorrectionProcess_AndReportsTheNotice()
	{
		_env.Session.IsAutoCommit = false;
		await StartBlockedTurnAsync();

		var response = await _env.CallAsync("session.setOptions", new JsonObject { ["nodeId"] = _env.Session.NodeId, ["autoCommit"] = true });

		Assert.Null(response["error"]);
		Assert.Equal(["[AutoCommit was enabled for this run]"], response["result"]!["corrections"]!.AsArray().Select(n => n!.GetValue<string>()));
		await Waits.Until(() => _env.Turn.Process.Calls.Count == 2);
		var correction = _env.Turn.Process.Calls[1];
		Assert.Equal(Constants.Instructions.MidRunAutoCommitOn, correction.Message);
		Assert.Equal("claude-1", correction.SessionId);
		Assert.Equal(_env.Session.WorkingDirectory, correction.WorkingDirectory);
		Assert.True(_env.Session.IsAutoCommit);
	}

	[Fact]
	public async Task ChangingAutoDocumentWhileTheTurnRuns_SendsTheDocumentCorrection()
	{
		_env.Session.IsAutoDocument = true;
		await StartBlockedTurnAsync();

		await _env.CallAsync("session.setOptions", new JsonObject { ["nodeId"] = _env.Session.NodeId, ["autoDocument"] = false });

		await Waits.Until(() => _env.Turn.Process.Calls.Count == 2);
		Assert.Equal(Constants.Instructions.MidRunAutoDocumentOff, _env.Turn.Process.Calls[1].Message);
	}

	[Fact]
	public async Task TheSameChangeWhileNoTurnRuns_StoresTheOption_AndSpawnsNothing()
	{
		_env.Session.ClaudeSessionId = "claude-1";
		_env.Session.IsAutoCommit    = false;

		var response = await _env.CallAsync("session.setOptions", new JsonObject { ["nodeId"] = _env.Session.NodeId, ["autoCommit"] = true });

		Assert.Null(response["result"]!["corrections"]);
		Assert.True(_env.Session.IsAutoCommit);
		Assert.Empty(_env.Turn.Process.Calls);
	}

	[Fact]
	public async Task ResetConversation_IsRefusedWhileTheTurnRuns_AndWorksAfterStop()
	{
		await StartBlockedTurnAsync();

		var refused = await _env.CallAsync("session.resetConversation", new JsonObject { ["nodeId"] = _env.Session.NodeId });
		Assert.Equal(-32000, refused["error"]!["code"]!.GetValue<int>());
		Assert.Equal("claude-1", _env.Session.ClaudeSessionId);

		await _env.CallAsync("turn.stop", new JsonObject { ["nodeId"] = _env.Session.NodeId });
		await Waits.Until(() => !_env.Turn.Engine.IsBusy(_env.Session.NodeId));

		var cleared = await _env.CallAsync("session.resetConversation", new JsonObject { ["nodeId"] = _env.Session.NodeId });
		Assert.True(cleared["result"]!["cleared"]!.GetValue<bool>());
		Assert.Null(_env.Session.ClaudeSessionId);
	}
}
