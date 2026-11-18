using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Tessyn.Web.E2E.Tests.Harness;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests;

/// <summary>Flows 1-3 and 8-13: login, tree, create, recent, tags, search, security and link drop, through the real web server, connector and Core.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class StackReadFlowTests
{
	private static JsonNode P(string json) => JsonNode.Parse(json)!;

	private static string Names(JsonNode sessions) =>
		string.Join("|", sessions.AsArray().Select(s => s!["name"]!.GetValue<string>()));

	private static JsonNode Directory(JsonNode snapshot, string label) =>
		snapshot["directories"]!.AsArray().Single(d => d!["label"]!.GetValue<string>() == label)!;

	[Fact]
	public async Task F01_Login_Hello_And_LinkStatusOnlineFirst()
	{
		await using var stack = await WebStack.StartAsync();

		var anonymous = stack.Web.CreateBrowser();
		Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/auth/me")).StatusCode);

		var browser = await stack.LoginAsync();
		var me = await (await browser.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonNode>();
		Assert.Equal(E2EWebFactory.Email, me!["email"]!.GetValue<string>());

		await using var events = await SseClient.OpenAsync(browser, "/api/events");
		var first = await events.NextAsync();
		Assert.Equal("link.status", first.Method);
		Assert.True(first.Params["online"]!.GetValue<bool>());

		var hello = await stack.RpcAsync(browser, "client.hello");
		Assert.Equal(1, hello["protocolVersion"]!.GetValue<int>());
		Assert.False(string.IsNullOrEmpty(hello["daemonVersion"]?.GetValue<string>()));
		Assert.False(string.IsNullOrEmpty(hello["machineName"]?.GetValue<string>()));
		Assert.Equal(32, hello["runId"]!.GetValue<string>().Replace("-", "").Length);
		Assert.NotNull(hello["serverTimeUtc"]);
	}

	[Fact]
	public async Task F02_TreeSnapshot_IdsOnly_WorkersCounted_SinceVersionUnchanged()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var seed = stack.Seed;

		var snapshot = await stack.RpcAsync(browser, "tree.snapshot");
		var json = snapshot.ToJsonString();

		Assert.Equal(2, snapshot["directories"]!.AsArray().Count);
		var alpha = Directory(snapshot, SeedModel.AlphaLabel);
		Assert.Equal(seed.AlphaDirectoryId, alpha["id"]!.GetValue<string>());

		// Workers are excluded from the listing and counted on the supervisor.
		Assert.Equal($"{SeedModel.SupervisorName}|{SeedModel.FavoriteName}", Names(alpha["sessions"]!));
		var supervisor = alpha["sessions"]![0]!;
		Assert.Equal(2, supervisor["workerCount"]!.GetValue<int>());
		Assert.DoesNotContain(SeedModel.WorkerOneName, json);
		Assert.DoesNotContain(SeedModel.WorkerTwoName, json);

		var group = alpha["groups"]!.AsArray().Single()!;
		Assert.Equal(SeedModel.GroupName, group["name"]!.GetValue<string>());
		Assert.Equal(SeedModel.BackendName, Names(group["sessions"]!));
		Assert.Equal(SeedModel.NestedGroupName, group["groups"]![0]!["name"]!.GetValue<string>());
		Assert.Equal(SeedModel.DeepName, Names(group["groups"]![0]!["sessions"]!));

		// Desktop state mirrored: favourite flag, attention badge on the supervisor.
		Assert.True(alpha["sessions"]![1]!["state"]!["favorite"]!.GetValue<bool>());
		var badge = supervisor["state"]!["attention"]!;
		Assert.Equal(3, badge["count"]!.GetValue<int>());

		// Nothing the contract forbids on the wire.
		Assert.DoesNotContain("agentToken", json, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(seed.AgentToken, json);
		Assert.DoesNotContain(seed.ClaudeSessionId, json);
		Assert.DoesNotContain("claudeSessionId", json, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(stack.Daemon.Root, json);
		Assert.DoesNotContain(seed.AlphaPath, json);
		Assert.DoesNotContain(".txt", json);
		Assert.DoesNotContain("\"fileName\"", json);
		Assert.DoesNotContain("\\\\", json);

		var version = snapshot["version"]!.GetValue<long>();
		var same = await stack.RpcAsync(browser, "tree.snapshot", P($$"""{"sinceVersion":{{version}}}"""));
		Assert.True(same["unchanged"]!.GetValue<bool>());
		Assert.Null(same["directories"]);

		var workers = await stack.RpcAsync(browser, "tree.workers", P($$"""{"supervisorId":"{{seed.SupervisorId}}"}"""));
		Assert.Equal($"{SeedModel.WorkerOneName}|{SeedModel.WorkerTwoName}", Names(workers["workers"]!));
		Assert.All(workers["workers"]!.AsArray(), w => Assert.Equal(seed.SupervisorId, w!["supervisorId"]!.GetValue<string>()));
	}

	[Fact]
	public async Task F03_CreateSession_Positions_TreeChangedPushed_SnapshotReflects()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var seed = stack.Seed;
		await using var events = await SseClient.OpenAsync(browser, "/api/events");
		await events.WaitForMethodAsync("link.status");

		var versionBefore = (await stack.RpcAsync(browser, "tree.snapshot"))["version"]!.GetValue<long>();

		// Anchor = session: right after it in the same container (favourite is the 2nd top-level session of alpha).
		var afterSession = await stack.RpcAsync(browser, "tree.createSession", P($$"""{"anchorId":"{{seed.SupervisorId}}","name":"  After supervisor  "}"""));
		Assert.Equal("After supervisor", afterSession["session"]!["name"]!.GetValue<string>());
		Assert.Equal(seed.AlphaDirectoryId, afterSession["directoryId"]!.GetValue<string>());
		Assert.Null(afterSession["groupId"]);
		var changed = await events.WaitForMethodAsync("tree.changed");
		Assert.True(changed.Params["version"]!.GetValue<long>() > versionBefore);

		// Anchor = group or directory: after the last session of its sessions (FR.1.30).
		var inGroup = await stack.RpcAsync(browser, "tree.createSession", P($$"""{"anchorId":"{{seed.GroupId}}","name":"Group last"}"""));
		Assert.Equal(seed.GroupId, inGroup["groupId"]!.GetValue<string>());
		var inDir = await stack.RpcAsync(browser, "tree.createSession", P($$"""{"anchorId":"{{seed.BetaDirectoryId}}","name":"Beta last"}"""));
		Assert.Null(inDir["groupId"]);
		Assert.Equal(seed.BetaDirectoryId, inDir["directoryId"]!.GetValue<string>());

		var snapshot = await stack.RpcAsync(browser, "tree.snapshot");
		var alpha = Directory(snapshot, SeedModel.AlphaLabel);
		Assert.Equal($"{SeedModel.SupervisorName}|After supervisor|{SeedModel.FavoriteName}", Names(alpha["sessions"]!));
		Assert.Equal($"{SeedModel.BackendName}|Group last", Names(alpha["groups"]![0]!["sessions"]!));
		Assert.EndsWith("|Beta last", Names(Directory(snapshot, SeedModel.BetaLabel)["sessions"]!));
		Assert.True(snapshot["version"]!.GetValue<long>() > versionBefore);

		var empty = await stack.RpcRawAsync(browser, "tree.createSession", P($$"""{"anchorId":"{{seed.SupervisorId}}","name":"   "}"""));
		Assert.Equal(-32602, WebStack.ErrorCode(empty));
	}

	[Fact]
	public async Task F08_Favorite_PinsInRecent_And_Hide()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var seed = stack.Seed;

		// Give an otherwise quiet session recent activity so it is a plain (unpinned) recent row.
		var deep = stack.Daemon.Registry.FindModelByNodeId(seed.DeepId)!;
		stack.Daemon.Files.AppendMessage(deep.FileName, "USER", "ping");

		var before = await stack.RpcAsync(browser, "recent.list");
		Assert.NotNull(before["serverTimeUtc"]);
		var rows = before["sessions"]!.AsArray();
		var deepRow = rows.Single(r => r!["id"]!.GetValue<string>() == seed.DeepId)!;
		Assert.False(deepRow["pinned"]!.GetValue<bool>());
		Assert.True(deepRow["canHide"]!.GetValue<bool>());
		Assert.Equal(SeedModel.AlphaLabel, deepRow["directoryLabel"]!.GetValue<string>());

		// Favourite and open-attention sessions are pinned and cannot be hidden.
		var supervisorRow = rows.Single(r => r!["id"]!.GetValue<string>() == seed.SupervisorId)!;
		Assert.True(supervisorRow["pinned"]!.GetValue<bool>());
		Assert.False((await stack.RpcAsync(browser, "recent.hide", P($$"""{"nodeId":"{{seed.SupervisorId}}"}"""))) ["hidden"]!.GetValue<bool>());

		var fav = await stack.RpcAsync(browser, "session.setFavorite", P($$"""{"nodeId":"{{seed.BetaId}}","value":true}"""));
		Assert.True(fav["value"]!.GetValue<bool>());
		var afterFav = (await stack.RpcAsync(browser, "recent.list"))["sessions"]!.AsArray();
		var betaRow = afterFav.Single(r => r!["id"]!.GetValue<string>() == seed.BetaId)!;
		Assert.True(betaRow["pinned"]!.GetValue<bool>());
		Assert.True(betaRow["state"]!["favorite"]!.GetValue<bool>());

		var hidden = await stack.RpcAsync(browser, "recent.hide", P($$"""{"nodeId":"{{seed.DeepId}}"}"""));
		Assert.True(hidden["hidden"]!.GetValue<bool>());
		var afterHide = (await stack.RpcAsync(browser, "recent.list"))["sessions"]!.AsArray();
		Assert.DoesNotContain(afterHide, r => r!["id"]!.GetValue<string>() == seed.DeepId);
	}

	[Fact]
	public async Task F09_TagsSearch_SelectionGroupsSessions()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var seed = stack.Seed;

		var none = await stack.RpcAsync(browser, "tags.search", P("""{"text":"","selected":[]}"""));
		Assert.Equal("selectTags", none["sessionsEmptyReason"]!.GetValue<string>());
		var chips = none["chips"]!.AsArray();
		var pr = chips.Single(c => c!["url"]!.GetValue<string>() == SeedModel.PullRequestUrl)!;
		Assert.Equal(2, pr["sessionCount"]!.GetValue<int>());
		Assert.Equal("gitHubPullRequest", pr["kind"]!.GetValue<string>());
		Assert.Contains(chips, c => c!["url"]!.GetValue<string>() == SeedModel.RepositoryUrl);

		var selected = new JsonObject
		{
			["text"] = "",
			["selected"] = new JsonArray(new JsonObject
			{
				["key"] = pr["key"]!.GetValue<string>(),
				["label"] = pr["label"]!.GetValue<string>(),
				["url"] = pr["url"]!.GetValue<string>(),
				["kind"] = pr["kind"]!.GetValue<string>(),
			}),
		};
		var result = await stack.RpcAsync(browser, "tags.search", selected);
		Assert.Null(result["sessionsEmptyReason"]);
		var groups = result["groups"]!.AsArray();
		Assert.Equal(2, groups.Count);
		var all = groups.SelectMany(g => g!["sessions"]!.AsArray()).Select(s => s!["id"]!.GetValue<string>()).ToHashSet();
		Assert.Contains(seed.BackendId, all);
		Assert.Contains(seed.BetaId, all);
		Assert.Contains(result["chips"]!.AsArray(), c => c!["selected"]!.GetValue<bool>());
	}

	[Fact]
	public async Task F10_TreeSearch_RealIndex()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		var seed = stack.Seed;

		// A real temp FTS index over a Claude JSONL transcript (not the file-scan fallback).
		var result = await stack.RpcAsync(browser, "tree.search", P($$"""{"query":"{{SeedModel.SearchWord}}"}"""));
		Assert.False(result["usedFallback"]!.GetValue<bool>());
		Assert.False(result["effectiveExact"]!.GetValue<bool>());
		Assert.False(result["autoExact"]!.GetValue<bool>());
		var match = result["matches"]!.AsArray().Single()!;
		Assert.Equal(seed.SearchTargetId, match["id"]!.GetValue<string>());
		Assert.True(match["matchedSelf"]!.GetValue<bool>());
		Assert.True(match["loose"]!.GetValue<int>() > 0);

		var blank = await stack.RpcAsync(browser, "tree.search", P("""{"query":"   "}"""));
		Assert.Empty(blank["matches"]!.AsArray());

		// A short query is auto-exact (desktop rule); exact needs the phrase to be literal.
		var shortQuery = await stack.RpcAsync(browser, "tree.search", P("""{"query":"cage"}"""));
		Assert.True(shortQuery["autoExact"]!.GetValue<bool>());
		Assert.True(shortQuery["effectiveExact"]!.GetValue<bool>());
		Assert.Single(shortQuery["matches"]!.AsArray());

		var scoped = await stack.RpcAsync(browser, "tree.search", P($$"""{"query":"{{SeedModel.SearchWord}}","scopeNodeId":"{{seed.SupervisorId}}"}"""));
		Assert.Empty(scoped["matches"]!.AsArray());
	}

	[Fact]
	public async Task F12a_NonAllowlistedMethod_RejectedAtTheServer()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();

		var loopbackOnly = await stack.RpcRawAsync(browser, "run.send", P("""{"nodeId":"x","text":"rm -rf"}"""));
		Assert.Equal(-32601, WebStack.ErrorCode(loopbackOnly));
		var invented = await stack.RpcRawAsync(browser, "nope.nothing");
		Assert.Equal(-32601, WebStack.ErrorCode(invented));

		var noHeader = stack.Web.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
		var login = await noHeader.PostAsJsonAsync("/api/auth/login", new { email = E2EWebFactory.Email, password = E2EWebFactory.Password });
		Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
	}

	[Fact]
	public async Task F12b_AllowedByServer_NotRemoteCallableOnDaemon_Gives32601FromDaemon()
	{
		var injected = new TdcpDispatcher([new NonRemoteUsageModule()]);
		await using var stack = await WebStack.StartAsync(injected);
		var browser = await stack.LoginAsync();

		// usage.get is on the server allowlist; this daemon registers it but does not mark it remote-callable.
		var response = await stack.RpcRawAsync(browser, "usage.get", P("""{}"""));
		Assert.Equal(-32601, WebStack.ErrorCode(response));
		Assert.False(injected.IsRemoteCallable("usage.get"));
	}

	[Fact]
	public async Task F12c_BadLinkToken_ConnectorStateError()
	{
		await using var stack = await WebStack.StartAsync(token: "definitely-the-wrong-token", waitOnline: false);

		await stack.Link.WaitForStateAsync(RelayState.Error);
		Assert.Contains("token", stack.Link.Connector.Status.LastError, StringComparison.OrdinalIgnoreCase);

		var browser = await stack.LoginAsync();
		var response = await stack.RpcRawAsync(browser, "client.hello");
		Assert.Equal(-32001, WebStack.ErrorCode(response));
	}

	[Fact]
	public async Task F13_LinkDrop_Offline_Then_Back_Online()
	{
		await using var stack = await WebStack.StartAsync();
		var browser = await stack.LoginAsync();
		await using var events = await SseClient.OpenAsync(browser, "/api/events");
		Assert.True((await events.NextAsync()).Params["online"]!.GetValue<bool>());

		stack.Link.Stop();
		var offline = await events.WaitForMethodAsync("link.status");
		Assert.False(offline.Params["online"]!.GetValue<bool>());
		var down = await stack.RpcRawAsync(browser, "client.hello");
		Assert.Equal(-32001, WebStack.ErrorCode(down));

		stack.Link.Start();
		var online = await events.WaitForMethodAsync("link.status");
		Assert.True(online.Params["online"]!.GetValue<bool>());
		var hello = await stack.RpcAsync(browser, "client.hello");
		Assert.Equal(1, hello["protocolVersion"]!.GetValue<int>());
	}
}
