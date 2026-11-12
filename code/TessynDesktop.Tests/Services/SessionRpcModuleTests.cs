using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.Tests.Services;

/// <summary>session.get / entries / entry / send / stop / setFavorite / setStreaming (contract sections 3.9 to 3.13, 3.15, 3.16).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionRpcModuleTests : IDisposable
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

	private readonly RemoteSessionEnv _env = new();

	public SessionRpcModuleTests()
	{
		_env.Models.Models = [new ClaudeModelInfo("claude-opus-5-5", "opus", "Opus 5.5"), new ClaudeModelInfo("claude-sonnet-5-5", "sonnet", "Sonnet 5.5")];
		_env.Settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "Work" });
	}

	public void Dispose() => _env.Dispose();

	private SessionNodeModel Node => _env.Session;

	private Task<JsonNode?> Call(string method, string extra = "") => _env.CallAsync(method, _env.NodeJson(extra));

	private async Task<TdcpInvalidParamsException> InvalidParams(string method, string extra = "") =>
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => Call(method, extra));

	// ── Registration ────────────────────────────────────────────────────────────

	[Fact]
	public void EveryMethod_IsRegisteredAsRemoteCallable()
	{
		var registry = new TdcpMethodRegistry();
		_env.Module.Register(registry);

		var names = registry.Entries.Select(e => e.Name).ToHashSet();
		string[] expected =
		[
			Constants.Remote.Methods.SessionGet, Constants.Remote.Methods.SessionEntries, Constants.Remote.Methods.SessionEntry,
			Constants.Remote.Methods.SessionSend, Constants.Remote.Methods.SessionStop,
			Constants.Remote.Methods.SessionSetFavorite, Constants.Remote.Methods.SessionSetStreaming,
		];
		Assert.True(expected.ToHashSet().SetEquals(names));
		Assert.All(registry.Entries, e => Assert.True(e.RemoteCallable));
	}

	[Theory]
	[InlineData("session.get")]
	[InlineData("session.entries")]
	[InlineData("session.send")]
	[InlineData("session.stop")]
	[InlineData("session.setFavorite")]
	[InlineData("session.setStreaming")]
	public async Task UnknownSession_IsAUserFacingError(string method)
	{
		var error = await Assert.ThrowsAsync<TdcpException>(() =>
			RemoteTestHelpers.CallAsync(_env.Module, method, "{\"nodeId\":\"nope\",\"text\":\"x\",\"value\":true,\"enabled\":true}"));

		Assert.Equal("Session not found", error.Message);
	}

	[Theory]
	[InlineData("session.get")]
	[InlineData("session.entries")]
	[InlineData("session.entry")]
	[InlineData("session.send")]
	[InlineData("session.stop")]
	public async Task MissingNodeId_IsInvalidParams(string method)
	{
		await Assert.ThrowsAsync<TdcpInvalidParamsException>(() => RemoteTestHelpers.CallAsync(_env.Module, method, "{}"));
	}

	// ── session.get ─────────────────────────────────────────────────────────────

	[Fact]
	public async Task Get_ReturnsTheDetailsOfASessionDirectlyUnderItsDirectory()
	{
		_env.Files.Set(Node.FileName, [User("hi", 1, "Work"), Assistant("yo", 2, "claude-opus-5-5", "high"), User("again", 3, "Work")]);
		_env.Tags.Tags.Add(new SessionTagModel { Kind = SessionTagKind.LinearIssue, Key = "HSE-1", Label = "HSE-1", Url = "https://linear.app/x/issue/HSE-1", Source = SessionTagSource.Session, AddedUtc = T0 });
		_env.Attention.Summary = new AttentionSummaryModel { OpenCount = 1, TopKind = AttentionKind.Ask, AllOpenCount = 2, AllOpenTopKind = AttentionKind.Ask };
		_env.Files.RaiseRewritten(Node.FileName);
		Node.IsStreamingMode = true;

		var json = (await Call("session.get"))!;

		Assert.Equal(Node.NodeId, (string?)json["id"]);
		Assert.Equal("Main session", (string?)json["name"]);
		Assert.Equal(_env.Directory.NodeId, (string?)json["directoryId"]);
		Assert.Null(json["groupId"]);
		Assert.Null(json["supervisorId"]);
		Assert.NotNull(json["location"]!["directoryLabel"]);
		Assert.Equal(Node.NodeId, (string?)json["state"]!["id"]);
		Assert.Equal("linearIssue", (string?)json["tags"]![0]!["kind"]);
		Assert.Equal("session", (string?)json["tags"]![0]!["source"]);
		Assert.Equal(1, (int?)json["attention"]!["openCount"]);
		Assert.Equal(2, (int?)json["attention"]!["allOpenCount"]);
		Assert.Equal("claude-opus-5-5", (string?)json["lastUsed"]!["modelId"]);
		Assert.Equal("high", (string?)json["lastUsed"]!["effort"]);
		Assert.Equal("p1", (string?)json["lastUsed"]!["profileId"]);
		Assert.Equal("Work", (string?)json["lastUsed"]!["profileName"]);
		Assert.True((bool?)json["isStreamingMode"]);
		Assert.Equal(3, (int?)json["entryCount"]);
		Assert.Equal(1, (int?)json["epoch"]);
	}

	[Fact]
	public async Task Get_AFreshSession_HasDefaultLastUsedSettingsAndNoEntries()
	{
		var json = (await Call("session.get"))!;

		Assert.Equal("default", (string?)json["lastUsed"]!["modelId"]);
		Assert.Equal("default", (string?)json["lastUsed"]!["effort"]);
		Assert.Equal("default", (string?)json["lastUsed"]!["profileId"]);
		Assert.Equal("[Sys] me@example.com", (string?)json["lastUsed"]!["profileName"]);
		Assert.Equal(0, (int?)json["entryCount"]);
		Assert.Equal(0, (int?)json["epoch"]);
		Assert.Empty(json["tags"]!.AsArray());
	}

	[Fact]
	public async Task Get_ASessionInANestedGroup_ReportsTheImmediateGroup()
	{
		var outer  = _env.Registry.AddGroup(_env.Directory, "outer");
		var inner  = _env.Registry.AddGroupToGroup(outer, "inner");
		var nested = _env.Registry.AddSessionToGroup(inner, "Nested");

		var json = (await _env.CallAsync("session.get", $"{{\"nodeId\":\"{nested.NodeId}\"}}"))!;

		Assert.Equal(_env.Directory.NodeId, (string?)json["directoryId"]);
		Assert.Equal(inner.NodeId, (string?)json["groupId"]);
		Assert.Contains("inner", (string?)json["location"]!["treePath"]);
	}

	[Fact]
	public async Task Get_ReportsTheSupervisorOnlyWhenItExists()
	{
		var worker = _env.Registry.AddSession(_env.Directory, "Worker");
		worker.SupervisorNodeId = Node.NodeId;
		var orphan = _env.Registry.AddSession(_env.Directory, "Orphan");
		orphan.SupervisorNodeId = "missing";

		var workerJson = (await _env.CallAsync("session.get", $"{{\"nodeId\":\"{worker.NodeId}\"}}"))!;
		var orphanJson = (await _env.CallAsync("session.get", $"{{\"nodeId\":\"{orphan.NodeId}\"}}"))!;

		Assert.Equal(Node.NodeId, (string?)workerJson["supervisorId"]);
		Assert.Null(orphanJson["supervisorId"]);
	}

	// ── session.entries / session.entry ─────────────────────────────────────────

	[Fact]
	public async Task Entries_DefaultsToTheLastFiftyAndReportsEpochAndTotal()
	{
		_env.Files.Set(Node.FileName, Enumerable.Range(0, 80).Select(i => User($"m{i}", i)));

		var json = (await Call("session.entries"))!;

		Assert.Equal(Node.NodeId, (string?)json["nodeId"]);
		Assert.Equal(80, (int?)json["total"]);
		Assert.Equal(0, (int?)json["epoch"]);
		Assert.Equal(50, json["entries"]!.AsArray().Count);
		Assert.Equal(30, (int?)json["entries"]![0]!["index"]);
		Assert.Equal("user", (string?)json["entries"]![0]!["role"]);
	}

	[Fact]
	public async Task Entries_LimitIsCappedAtTwoHundred()
	{
		_env.Files.Set(Node.FileName, Enumerable.Range(0, 300).Select(i => User($"m{i}", i)));

		var json = (await Call("session.entries", "\"limit\":1000"))!;

		Assert.Equal(Constants.Remote.Limits.EntriesMaxLimit, json["entries"]!.AsArray().Count);
	}

	[Fact]
	public async Task Entries_AfterAndBeforeIndexAreHonoured()
	{
		_env.Files.Set(Node.FileName, Enumerable.Range(0, 10).Select(i => User($"m{i}", i)));

		var after  = (await Call("session.entries", "\"afterIndex\":6"))!;
		var before = (await Call("session.entries", "\"beforeIndex\":3,\"limit\":2"))!;

		Assert.Equal([7, 8, 9], after["entries"]!.AsArray().Select(e => (int)e!["index"]!));
		Assert.Equal([1, 2], before["entries"]!.AsArray().Select(e => (int)e!["index"]!));
	}

	[Fact]
	public async Task Entries_InvalidCombinationsAndLimits_AreInvalidParams()
	{
		await InvalidParams("session.entries", "\"afterIndex\":1,\"beforeIndex\":5");
		await InvalidParams("session.entries", "\"limit\":0");
		await InvalidParams("session.entries", "\"limit\":-3");
		await InvalidParams("session.entries", "\"limit\":\"many\"");
		await InvalidParams("session.entries", "\"afterIndex\":\"x\"");
	}

	[Fact]
	public async Task Entries_TruncatedContent_IsFlaggedOnTheWire()
	{
		_env.Files.Set(Node.FileName, [User(new string('x', Constants.Remote.Limits.EntryContentMaxChars + 5))]);

		var json = (await Call("session.entries"))!;

		Assert.True((bool?)json["entries"]![0]!["truncated"]);
		Assert.Equal(Constants.Remote.Limits.EntryContentMaxChars + 5, (int?)json["entries"]![0]!["fullLength"]);
	}

	[Fact]
	public async Task Entries_OmitNullAttributesOnTheWire()
	{
		_env.Files.Set(Node.FileName, [User("hello")]);

		var entry = (await Call("session.entries"))!["entries"]![0]!.AsObject();

		Assert.False(entry.ContainsKey("profileName"));
		Assert.False(entry.ContainsKey("truncated"));
		Assert.False(entry.ContainsKey("fullLength"));
	}

	[Fact]
	public async Task Entry_ReturnsTheFullContent_AndRejectsAStaleEpoch()
	{
		var longText = new string('z', Constants.Remote.Limits.EntryContentMaxChars * 2);
		_env.Files.Set(Node.FileName, [User("a"), User(longText)]);

		var entry = (await Call("session.entry", "\"index\":1,\"epoch\":0"))!;
		Assert.Equal(longText.Length, ((string?)entry["content"])!.Length);

		_env.Files.RaiseRewritten(Node.FileName);
		var error = await Assert.ThrowsAsync<TdcpException>(() => Call("session.entry", "\"index\":1,\"epoch\":0"));
		Assert.Equal("Session history changed", error.Message);
	}

	[Fact]
	public async Task Entry_RequiresIndexAndEpoch()
	{
		await InvalidParams("session.entry", "\"epoch\":0");
		await InvalidParams("session.entry", "\"index\":0");
	}

	// ── session.send ────────────────────────────────────────────────────────────

	private async Task WaitForRuns(int count)
	{
		for (var i = 0; i < count; i++)
			Assert.True(await _env.Turns.Started.WaitAsync(Wait));
	}

	[Fact]
	public async Task Send_AcceptsAndRunsAWebTurnWithoutOptions()
	{
		var json = (await Call("session.send", "\"text\":\"  hello there  \""))!;
		await WaitForRuns(1);

		Assert.True((bool?)json["accepted"]);
		Assert.False((bool?)json["queued"]);
		var run = _env.Turns.Runs.Single();
		Assert.Same(Node, run.Node);
		Assert.Equal("hello there", run.Prompt);
		Assert.Equal(TurnSource.Web, run.Source);
		Assert.Null(run.Options);
	}

	[Fact]
	public async Task Send_PassesEachOptionToTheTurn()
	{
		await Call("session.send", "\"text\":\"a\",\"modelId\":\"claude-opus-5-5\"");
		await Call("session.send", "\"text\":\"b\",\"effort\":\"high\"");
		await Call("session.send", "\"text\":\"c\",\"profileId\":\"p1\"");
		await Call("session.send", "\"text\":\"d\",\"profileId\":\"default\"");
		await Call("session.send", "\"text\":\"e\",\"modelId\":\"default\",\"effort\":\"default\"");
		await WaitForRuns(5);

		var byPrompt = _env.Turns.Runs.ToDictionary(r => r.Prompt, r => r.Options);
		Assert.Equal(new TurnOptionsModel("claude-opus-5-5"), byPrompt["a"]);
		Assert.Equal(new TurnOptionsModel(Effort: "high"), byPrompt["b"]);
		Assert.Equal(new TurnOptionsModel(ProfileName: "Work"), byPrompt["c"]);
		Assert.Equal(new TurnOptionsModel(ProfileName: "[Sys] me@example.com"), byPrompt["d"]);
		Assert.Equal(new TurnOptionsModel("default", "default"), byPrompt["e"]);
	}

	[Fact]
	public async Task Send_AllThreeOptionsTogether()
	{
		await Call("session.send", "\"text\":\"x\",\"modelId\":\"claude-sonnet-5-5\",\"effort\":\"low\",\"profileId\":\"p1\"");
		await WaitForRuns(1);

		Assert.Equal(new TurnOptionsModel("claude-sonnet-5-5", "low", "Work"), _env.Turns.Runs.Single().Options);
	}

	[Theory]
	[InlineData("\"text\":\"\"")]
	[InlineData("\"text\":\"   \"")]
	[InlineData("\"text\":5")]
	[InlineData("\"text\":\"x\",\"modelId\":\"claude-unknown\"")]
	[InlineData("\"text\":\"x\",\"effort\":\"extreme\"")]
	[InlineData("\"text\":\"x\",\"profileId\":\"nobody\"")]
	[InlineData("\"text\":\"x\",\"modelId\":3")]
	public async Task Send_InvalidParams_AreRejectedAndNothingRuns(string extra)
	{
		await InvalidParams("session.send", extra);

		Assert.Empty(_env.Turns.Runs);
	}

	[Fact]
	public async Task Send_MissingText_IsInvalidParams()
	{
		await InvalidParams("session.send");
	}

	[Fact]
	public async Task Send_WhileATurnIsRunning_IsQueued()
	{
		_env.Registry.SetSessionRunning(Node.NodeId, true);

		var json = (await Call("session.send", "\"text\":\"later\""))!;

		Assert.True((bool?)json["queued"]);
		await WaitForRuns(1);
	}

	[Fact]
	public async Task Send_ASecondSendBeforeTheFirstFinished_IsQueued_AndTheFirstIsNot()
	{
		_env.Turns.HoldTurns = true;

		var first  = (await Call("session.send", "\"text\":\"one\""))!;
		var second = (await Call("session.send", "\"text\":\"two\""))!;
		_env.Turns.Release();
		await WaitForRuns(2);

		Assert.False((bool?)first["queued"]);
		Assert.True((bool?)second["queued"]);
	}

	[Fact]
	public async Task Send_AfterTheEarlierTurnsFinished_IsNotQueuedAgain()
	{
		await Call("session.send", "\"text\":\"one\"");
		await WaitForRuns(1);
		await Task.Delay(100);   // the pending counter is released after the turn task completes

		var second = (await Call("session.send", "\"text\":\"two\""))!;

		Assert.False((bool?)second["queued"]);
		await WaitForRuns(1);
	}

	[Fact]
	public async Task Send_ATurnThatThrows_DoesNotBreakTheNextSend()
	{
		_env.Turns.ThrowOnRun = true;
		await Call("session.send", "\"text\":\"boom\"");
		await WaitForRuns(1);
		await Task.Delay(100);

		_env.Turns.ThrowOnRun = false;
		var json = (await Call("session.send", "\"text\":\"fine\""))!;

		Assert.True((bool?)json["accepted"]);
		Assert.False((bool?)json["queued"]);
		await WaitForRuns(1);
	}

	// ── session.stop ────────────────────────────────────────────────────────────

	[Fact]
	public async Task Stop_WithALiveProcess_StopsItAndCancelsTheTurn()
	{
		_env.Streaming.Alive.Add(Node.NodeId);

		var json = (await Call("session.stop"))!;

		Assert.True((bool?)json["stopped"]);
		Assert.Equal([Node.NodeId], _env.Streaming.Stopped);
		Assert.Equal([Node.NodeId], _env.Turns.Cancelled);
	}

	[Fact]
	public async Task Stop_WithoutAProcessButARunningTurn_Cancels()
	{
		_env.Turns.CancelResult = true;

		var json = (await Call("session.stop"))!;

		Assert.True((bool?)json["stopped"]);
		Assert.Empty(_env.Streaming.Stopped);
	}

	[Fact]
	public async Task Stop_WithNothingToStop_ReportsFalse()
	{
		var json = (await Call("session.stop"))!;

		Assert.False((bool?)json["stopped"]);
		Assert.Empty(_env.Streaming.Stopped);
		Assert.Equal([Node.NodeId], _env.Turns.Cancelled);
	}

	// ── session.setFavorite ─────────────────────────────────────────────────────

	[Fact]
	public async Task SetFavorite_SetsTheFlag_SavesAndRaisesTheSettingsEvent()
	{
		var json = (await Call("session.setFavorite", "\"value\":true"))!;

		Assert.True((bool?)json["value"]);
		Assert.True(Node.IsFavorite);
		Assert.Equal([Node.NodeId], _env.SettingsChanged);
	}

	[Fact]
	public async Task SetFavorite_PersistsToTheSettingsFile()
	{
		await Call("session.setFavorite", "\"value\":true");

		var second = new AppSettingsService(_env.TempDir);
		second.Load();

		Assert.True(second.Settings.Tree.SelectMany(d => d.Sessions).Single(x => x.NodeId == Node.NodeId).IsFavorite);
	}

	[Fact]
	public async Task SetFavorite_UnsettingAndRepeating_RaisesOnlyOnRealChanges()
	{
		await Call("session.setFavorite", "\"value\":true");
		await Call("session.setFavorite", "\"value\":true");
		await Call("session.setFavorite", "\"value\":false");

		Assert.False(Node.IsFavorite);
		Assert.Equal(2, _env.SettingsChanged.Count);
	}

	[Fact]
	public async Task SetFavorite_RequiresAValue()
	{
		await InvalidParams("session.setFavorite");
		await InvalidParams("session.setFavorite", "\"value\":\"yes\"");
	}

	// ── session.setStreaming ────────────────────────────────────────────────────

	[Fact]
	public async Task SetStreaming_TurnsItOn()
	{
		var json = (await Call("session.setStreaming", "\"enabled\":true"))!;

		Assert.True((bool?)json["enabled"]);
		Assert.False((bool?)json["needsConfirm"]);
		Assert.True(Node.IsStreamingMode);
		Assert.Equal([Node.NodeId], _env.SettingsChanged);
	}

	[Fact]
	public async Task SetStreaming_TurnsItOffWithoutAProcess()
	{
		Node.IsStreamingMode = true;

		var json = (await Call("session.setStreaming", "\"enabled\":false"))!;

		Assert.False((bool?)json["enabled"]);
		Assert.False((bool?)json["needsConfirm"]);
		Assert.False(Node.IsStreamingMode);
		Assert.Empty(_env.Streaming.Stopped);
	}

	[Fact]
	public async Task SetStreaming_OffWhileAProcessIsAlive_NeedsConfirmAndChangesNothing()
	{
		Node.IsStreamingMode = true;
		_env.Streaming.Alive.Add(Node.NodeId);

		var json = (await Call("session.setStreaming", "\"enabled\":false"))!;

		Assert.True((bool?)json["needsConfirm"]);
		Assert.True((bool?)json["enabled"]);
		Assert.True(Node.IsStreamingMode);
		Assert.Empty(_env.Streaming.Stopped);
		Assert.Empty(_env.SettingsChanged);
	}

	[Fact]
	public async Task SetStreaming_OffWithForce_StopsTheProcessAndTurnsItOff()
	{
		Node.IsStreamingMode = true;
		_env.Streaming.Alive.Add(Node.NodeId);

		var json = (await Call("session.setStreaming", "\"enabled\":false,\"force\":true"))!;

		Assert.False((bool?)json["needsConfirm"]);
		Assert.False((bool?)json["enabled"]);
		Assert.False(Node.IsStreamingMode);
		Assert.Equal([Node.NodeId], _env.Streaming.Stopped);
		Assert.Equal([Node.NodeId], _env.SettingsChanged);
	}

	[Fact]
	public async Task SetStreaming_OnWhileAProcessIsAlive_NeverNeedsConfirm()
	{
		_env.Streaming.Alive.Add(Node.NodeId);

		var json = (await Call("session.setStreaming", "\"enabled\":true"))!;

		Assert.False((bool?)json["needsConfirm"]);
		Assert.Empty(_env.Streaming.Stopped);
	}

	[Fact]
	public async Task SetStreaming_RequiresEnabled()
	{
		await InvalidParams("session.setStreaming");
		await InvalidParams("session.setStreaming", "\"enabled\":1");
	}
}
