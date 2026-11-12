using System.Text.Json.Nodes;
using Tessyn.Web.E2E.Tests.Harness;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests;

/// <summary>Flows 4-7 and 11: sending, queuing, stopping, Focus and compaction through the real stack.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class StackTurnFlowTests
{
	private const string DefaultProfileLabel = "[Sys] " + FakeProfileService.DefaultEmail;

	private static JsonNode P(string json) => JsonNode.Parse(json)!;

	private static async Task<JsonArray> EntriesAsync(WebStack stack, HttpClient browser, string nodeId, int limit = 50)
	{
		var result = await stack.RpcAsync(browser, "session.entries", P($$"""{"nodeId":"{{nodeId}}","limit":{{limit}}}"""));
		return result["entries"]!.AsArray();
	}

	private static string Role(JsonNode entry) => entry["role"]!.GetValue<string>();

	[Fact]
	public async Task F04_Send_Running_Appended_Completed_And_EntriesStamped()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var id = stack.Seed.BetaId;
		await using var events = await SseClient.OpenAsync(browser, "/api/events");
		await events.WaitForMethodAsync("link.status");
		stack.Daemon.Process.Delay = TimeSpan.FromMilliseconds(400);

		var before = (await stack.RpcAsync(browser, "session.get", P($$"""{"nodeId":"{{id}}"}""")))["entryCount"]!.GetValue<int>();
		var send = await stack.RpcAsync(browser, "session.send", P($$"""{"nodeId":"{{id}}","text":"Hello from the phone","modelId":"{{FakeModelService.OpusId}}","effort":"high"}"""));
		Assert.True(send["accepted"]!.GetValue<bool>());
		Assert.False(send["queued"]!.GetValue<bool>());

		var running = await events.WaitForAsync(f => f.Method == "session.changed" && f.Params["state"]!["id"]!.GetValue<string>() == id && f.Params["state"]!["running"]!.GetValue<bool>());
		Assert.True(running.Params["state"]!["running"]!.GetValue<bool>());
		var appended = await events.WaitForMethodAsync("session.appended");
		Assert.Equal(id, appended.Params["nodeId"]!.GetValue<string>());
		Assert.Equal(0, appended.Params["epoch"]!.GetValue<int>());
		var completed = await events.WaitForMethodAsync("turn.completed");
		Assert.Equal(id, completed.Params["nodeId"]!.GetValue<string>());
		Assert.False(completed.Params["cancelled"]!.GetValue<bool>());
		Assert.Null(completed.Params["error"]);

		var entries = await EntriesAsync(stack, browser, id);
		Assert.Equal(before + 2, entries.Count);
		var user = entries[^2]!;
		var assistant = entries[^1]!;
		Assert.Equal("user", Role(user));
		Assert.Contains("Hello from the phone", user["content"]!.GetValue<string>());
		Assert.Equal(DefaultProfileLabel, user["profileName"]!.GetValue<string>());
		Assert.Equal("assistant", Role(assistant));
		Assert.StartsWith("Fake reply", assistant["content"]!.GetValue<string>());
		Assert.Equal(FakeModelService.OpusId, assistant["modelId"]!.GetValue<string>());
		Assert.Equal("high", assistant["effort"]!.GetValue<string>());

		var call = Assert.Single(stack.Daemon.Process.Snapshot());
		Assert.Equal(FakeModelService.OpusId, call.Model);
		Assert.Equal("high", call.Effort);

		var detail = await stack.RpcAsync(browser, "session.get", P($$"""{"nodeId":"{{id}}"}"""));
		Assert.Equal(FakeModelService.OpusId, detail["lastUsed"]!["modelId"]!.GetValue<string>());
		Assert.Equal("high", detail["lastUsed"]!["effort"]!.GetValue<string>());
		Assert.False(detail["state"]!["running"]!.GetValue<bool>());
	}

	[Fact]
	public async Task F05_SendWhileRunning_IsQueued_And_BothTurnsCompleteInOrder()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var id = stack.Seed.BetaId;
		await using var events = await SseClient.OpenAsync(browser, "/api/events");
		await events.WaitForMethodAsync("link.status");
		stack.Daemon.Process.Delay = TimeSpan.FromMilliseconds(600);

		var first = await stack.RpcAsync(browser, "session.send", P($$"""{"nodeId":"{{id}}","text":"first prompt"}"""));
		Assert.False(first["queued"]!.GetValue<bool>());
		var second = await stack.RpcAsync(browser, "session.send", P($$"""{"nodeId":"{{id}}","text":"second prompt"}"""));
		Assert.True(second["queued"]!.GetValue<bool>());

		await events.WaitForMethodAsync("turn.completed", TimeSpan.FromSeconds(15));
		await events.WaitForMethodAsync("turn.completed", TimeSpan.FromSeconds(15));

		var entries = await EntriesAsync(stack, browser, id);
		var tail = entries.TakeLast(4).Select(e => (Role(e!), e!["content"]!.GetValue<string>())).ToList();
		Assert.Equal("user", tail[0].Item1);
		Assert.Contains("first prompt", tail[0].Item2);
		Assert.Equal("assistant", tail[1].Item1);
		Assert.Equal("user", tail[2].Item1);
		Assert.Contains("second prompt", tail[2].Item2);
		Assert.Equal("assistant", tail[3].Item1);
		Assert.Equal(2, stack.Daemon.Process.Snapshot().Count);
	}

	[Fact]
	public async Task F06_Stop_CancelsSlowTurn()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var id = stack.Seed.BetaId;
		await using var events = await SseClient.OpenAsync(browser, "/api/events");
		await events.WaitForMethodAsync("link.status");
		stack.Daemon.Process.Delay = TimeSpan.FromSeconds(60);

		await stack.RpcAsync(browser, "session.send", P($$"""{"nodeId":"{{id}}","text":"this will be cancelled"}"""));
		await events.WaitForAsync(f => f.Method == "session.changed" && f.Params["state"]!["running"]!.GetValue<bool>());

		// The fake process must actually be in flight, otherwise cancellation would race the launch.
		await WebStack.PollAsync(() => Task.FromResult(stack.Daemon.Process.Snapshot().Count > 0 ? "started" : null));
		var stop = await stack.RpcAsync(browser, "session.stop", P($$"""{"nodeId":"{{id}}"}"""));
		Assert.True(stop["stopped"]!.GetValue<bool>());

		var completed = await events.WaitForMethodAsync("turn.completed");
		Assert.True(completed.Params["cancelled"]!.GetValue<bool>());

		var entries = await EntriesAsync(stack, browser, id);
		var last = entries[^1]!;
		Assert.Equal("system", Role(last));
		Assert.Equal("[Turn cancelled]", last["content"]!.GetValue<string>());
		Assert.False((await stack.RpcAsync(browser, "session.get", P($$"""{"nodeId":"{{id}}"}""")))["state"]!["running"]!.GetValue<bool>());
	}

	[Fact]
	public async Task F07_Focus_List_Reply_Dismiss()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var seed = stack.Seed;
		await using var events = await SseClient.OpenAsync(browser, "/api/events");
		await events.WaitForMethodAsync("link.status");

		var list = await stack.RpcAsync(browser, "attention.list", P($$"""{"nodeId":"{{seed.SupervisorId}}"}"""));
		Assert.Equal(3, list["summary"]!["openCount"]!.GetValue<int>());
		Assert.Equal("ask", list["summary"]!["topKind"]!.GetValue<string>());
		var items = list["items"]!.AsArray();
		Assert.Equal(["ask", "todo", "alert"], items.Select(i => i!["kind"]!.GetValue<string>()).ToArray());
		Assert.Equal(SeedModel.AskTitle, items[0]!["title"]!.GetValue<string>());
		Assert.Equal(["Postgres", "SQLite"], items[0]!["options"]!.AsArray().Select(o => o!.GetValue<string>()).ToArray());
		Assert.Equal("Postgres", items[0]!["recommendation"]!.GetValue<string>());
		Assert.All(items, i => Assert.Equal("open", i!["status"]!.GetValue<string>()));

		// attention.reply: marks answered BEFORE the send, then the send carries the exact answers prompt.
		string? statusAtSend = null;
		stack.Daemon.Process.Reply = _ =>
		{
			statusAtSend = stack.Daemon.Attention.GetItems(seed.SupervisorId).Single(i => i.Id == seed.AskItemId).Status.ToString();
			return "Understood, using Postgres.";
		};
		var reply = await stack.RpcAsync(browser, "attention.reply", P($$"""{"nodeId":"{{seed.SupervisorId}}","answers":[{"itemId":"{{seed.AskItemId}}","text":"Postgres"}]}"""));
		Assert.Equal([seed.AskItemId], reply["answeredIds"]!.AsArray().Select(i => i!.GetValue<string>()).ToArray());
		Assert.Empty(reply["skippedIds"]!.AsArray());
		Assert.True(reply["turnStarted"]!.GetValue<bool>());
		await events.WaitForMethodAsync("attention.changed");
		await events.WaitForMethodAsync("turn.completed");

		Assert.Equal("Answered", statusAtSend);
		var expected = AttentionPromptBuilder.BuildAnswersPrompt([new AttentionAnswerModel { ItemId = seed.AskItemId, Title = SeedModel.AskTitle, Answer = "Postgres" }]);
		var call = Assert.Single(stack.Daemon.Process.Snapshot());
		Assert.Contains(expected, call.Prompt);

		var afterReply = await stack.RpcAsync(browser, "attention.list", P($$"""{"nodeId":"{{seed.SupervisorId}}"}"""));
		var answered = afterReply["items"]!.AsArray().Single(i => i!["id"]!.GetValue<string>() == seed.AskItemId)!;
		Assert.Equal("answered", answered["status"]!.GetValue<string>());
		Assert.Equal("Postgres", answered["answer"]!.GetValue<string>());

		// attention.dismiss: dismissed + a SYSTEM entry + attention.changed.
		var dismiss = await stack.RpcAsync(browser, "attention.dismiss", P($$"""{"nodeId":"{{seed.SupervisorId}}","itemId":"{{seed.TodoItemId}}"}"""));
		Assert.True(dismiss["dismissed"]!.GetValue<bool>());
		var changed = await events.WaitForMethodAsync("attention.changed");
		Assert.Equal(seed.SupervisorId, changed.Params["nodeId"]!.GetValue<string>());
		Assert.Equal(1, changed.Params["summary"]!["openCount"]!.GetValue<int>());

		var afterDismiss = await stack.RpcAsync(browser, "attention.list", P($$"""{"nodeId":"{{seed.SupervisorId}}"}"""));
		Assert.Equal("dismissed", afterDismiss["items"]!.AsArray().Single(i => i!["id"]!.GetValue<string>() == seed.TodoItemId)!["status"]!.GetValue<string>());
		var entries = await EntriesAsync(stack, browser, seed.SupervisorId);
		Assert.Contains(entries, e => Role(e!) == "system" && e!["content"]!.GetValue<string>() == $"[Attention {seed.TodoItemId} dismissed by the user]");

		// Dismissing again is a no-op.
		var again = await stack.RpcAsync(browser, "attention.dismiss", P($$"""{"nodeId":"{{seed.SupervisorId}}","itemId":"{{seed.TodoItemId}}"}"""));
		Assert.False(again["dismissed"]!.GetValue<bool>());
	}

	[Fact]
	public async Task F11_Compact_RewritesFile_ResetEpoch_EntriesReloadable()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var id = stack.Seed.BetaId;
		await using var events = await SseClient.OpenAsync(browser, "/api/events");
		await events.WaitForMethodAsync("link.status");
		// Compaction asks Claude to answer in the session-file format, so the fake does too.
		stack.Daemon.Process.Reply = _ => "[2026-11-12T10:00:00Z] USER\nSummary of the conversation so far.\n\n[2026-11-12T10:00:05Z] ASSISTANT\nNoted.";

		var before = await stack.RpcAsync(browser, "session.get", P($$"""{"nodeId":"{{id}}"}"""));
		Assert.Equal(0, before["epoch"]!.GetValue<int>());

		var compact = await stack.RpcAsync(browser, "session.compact", P($$"""{"nodeId":"{{id}}"}"""));
		Assert.True(compact["accepted"]!.GetValue<bool>());

		var reset = await events.WaitForMethodAsync("session.reset");
		Assert.Equal(id, reset.Params["nodeId"]!.GetValue<string>());
		Assert.Equal(1, reset.Params["epoch"]!.GetValue<int>());

		var result = await stack.RpcAsync(browser, "session.entries", P($$"""{"nodeId":"{{id}}"}"""));
		Assert.Equal(1, result["epoch"]!.GetValue<int>());
		var entries = result["entries"]!.AsArray();
		Assert.True(entries.Any(e => Role(e!) == "compaction"), entries.ToJsonString());
		Assert.True(entries.Any(e => e!["content"]!.GetValue<string>().Contains("Summary of the conversation")), entries.ToJsonString());

		// A client still holding epoch 0 is told the history changed.
		var stale = await stack.RpcRawAsync(browser, "session.entry", P($$"""{"nodeId":"{{id}}","index":0,"epoch":0}"""));
		Assert.Equal(-32000, WebStack.ErrorCode(stale));
		var fresh = await stack.RpcAsync(browser, "session.entry", P($$"""{"nodeId":"{{id}}","index":0,"epoch":1}"""));
		Assert.NotNull(fresh["content"]);
	}

	[Fact]
	public async Task Catalog_And_Usage_ThroughTheStack()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();

		var catalog = await stack.RpcAsync(browser, "catalog.get");
		var models = catalog["models"]!.AsArray();
		Assert.Equal("default", models[0]!["id"]!.GetValue<string>());
		Assert.Contains(models, m => m!["id"]!.GetValue<string>() == FakeModelService.OpusId);
		Assert.Equal(["default", "max", "high", "medium", "low"], catalog["efforts"]!.AsArray().Select(e => e!.GetValue<string>()).ToArray());
		var profiles = catalog["profiles"]!.AsArray();
		Assert.Equal(DefaultProfileLabel, profiles[0]!["displayName"]!.GetValue<string>());
		Assert.Equal("profile_1", profiles[1]!["id"]!.GetValue<string>());
		Assert.DoesNotContain("configDir", catalog.ToJsonString(), StringComparison.OrdinalIgnoreCase);

		var usage = await stack.RpcAsync(browser, "usage.get", P("""{"profileId":"default"}"""));
		Assert.Equal("ok", usage["state"]!.GetValue<string>());
		Assert.Equal(FakeUsageFetcher.FiveHour, usage["fiveHour"]!["utilization"]!.GetValue<double>());
		Assert.Equal(FakeUsageFetcher.SevenDay, usage["sevenDay"]!["utilization"]!.GetValue<double>());
		Assert.False(usage["fiveHour"]!["isStale"]!.GetValue<bool>());
	}
}
