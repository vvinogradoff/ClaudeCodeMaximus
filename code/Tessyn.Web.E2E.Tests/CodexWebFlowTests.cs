using System.Text.Json.Nodes;
using Tessyn.Web.E2E.Tests.Harness;
using Tessyn.Web.E2E.Tests.Harness.Codex;

namespace Tessyn.Web.E2E.Tests;

/// <summary>
/// TC.CX.42 (FR.33.2, FR.33.3, FR.33.17, FR.25.24) through the real stack: Tessyn.Web, the relay connector and the seeded daemon with the Codex
/// engine over the in-process fake app-server. A GPT session is opened and sent to from the browser side; what the web pickers are built from
/// (the catalog's engines, per-model efforts and ChatGPT profiles) and the read-only Codex status reach it; the ChatGPT and settings
/// administration does not.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexWebFlowTests
{
	private static JsonNode P(string json) => JsonNode.Parse(json)!;

	private static string[] Strings(JsonNode array) => [.. array.AsArray().Select(n => n!.GetValue<string>())];

	[Fact]
	public async Task CX42_1_Hello_carries_the_engines_and_the_read_only_Codex_status_without_a_path()
	{
		await using var stack = await WebStack.StartAsync(codex: true);
		var browser = await stack.LoginAsync();

		var hello = await stack.RpcAsync(browser, "client.hello");

		Assert.Equal(["claude", "codex"], Strings(hello["engines"]!));
		Assert.Contains("engines", Strings(hello["capabilities"]!));
		Assert.DoesNotContain("profiles.codex", Strings(hello["capabilities"]!));
		Assert.True(hello["codex"]!["found"]!.GetValue<bool>());
		Assert.Equal("0.171.0", hello["codex"]!["version"]!.GetValue<string>());
		Assert.NotNull(hello["codex"]!["testedVersion"]);
		Assert.DoesNotContain(@"Tools\\Codex", hello.ToJsonString());
	}

	[Fact]
	public async Task CX42_2_The_catalog_gives_the_pickers_their_engines_efforts_and_ChatGPT_profiles()
	{
		await using var stack = await WebStack.StartAsync(codex: true);
		var browser = await stack.LoginAsync();

		var catalog = await stack.RpcAsync(browser, "catalog.get");
		var models = catalog["models"]!.AsArray();
		var gpt = models.Single(m => m!["id"]!.GetValue<string>() == CodexDaemonWiring.GptModelId)!;
		var luna = models.Single(m => m!["id"]!.GetValue<string>() == CodexDaemonWiring.OtherGptModelId)!;
		var claudeDefault = models.Single(m => m!["id"]!.GetValue<string>() == "default")!;
		var chatGpt = catalog["profiles"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == CodexDaemonWiring.ProfileId)!;

		Assert.Equal("codex", gpt["engine"]!.GetValue<string>());
		Assert.Equal("openAI", gpt["provider"]!.GetValue<string>());
		Assert.True(gpt["isDefault"]!.GetValue<bool>());
		Assert.Equal(["low", "medium", "high", "xhigh", "max"], gpt["efforts"]!.AsArray().Select(e => e!["id"]!.GetValue<string>()).ToArray()); // no ultra
		Assert.Equal(["low", "medium"], luna["efforts"]!.AsArray().Select(e => e!["id"]!.GetValue<string>()).ToArray());
		Assert.Equal("claude", claudeDefault["engine"]!.GetValue<string>());
		Assert.Equal("codex", chatGpt["engine"]!.GetValue<string>());
		Assert.Equal("plus", chatGpt["plan"]!.GetValue<string>());
		Assert.Null(catalog["gptHint"]); // there are GPT models, so no hint line
		Assert.All(catalog["profiles"]!.AsArray().Where(p => p!["id"]!.GetValue<string>() != CodexDaemonWiring.ProfileId),
			p => Assert.Equal("claude", p!["engine"]!.GetValue<string>()));
	}

	[Fact]
	public async Task CX42_3_A_GPT_session_is_opened_and_sent_to_from_the_web_with_the_web_choice()
	{
		await using var stack = await WebStack.StartAsync(codex: true);
		var browser = await stack.LoginAsync();
		var id = stack.Seed.GptSessionId;
		await using var events = await SseClient.OpenAsync(browser, "/api/events");
		await events.WaitForMethodAsync("link.status");

		var opened = await stack.RpcAsync(browser, "session.get", P($$"""{"nodeId":"{{id}}"}"""));
		Assert.Equal("codex", opened["state"]!["engine"]!.GetValue<string>());
		Assert.Equal(CodexDaemonWiring.GptModelId, opened["lastUsed"]!["modelId"]!.GetValue<string>());
		Assert.Equal(CodexDaemonWiring.ProfileId, opened["lastUsed"]!["profileId"]!.GetValue<string>());

		// FR.25.24: the web always sends its choice; xhigh is a GPT-only effort of this model.
		var send = await stack.RpcAsync(browser, "session.send", P($$"""
			{"nodeId":"{{id}}","text":"Hello GPT from the phone","modelId":"{{CodexDaemonWiring.GptModelId}}","effort":"xhigh","profileId":"{{CodexDaemonWiring.ProfileId}}"}
			"""));
		Assert.True(send["accepted"]!.GetValue<bool>());

		var completed = await events.WaitForAsync(f => f.Method == "turn.completed" && f.Params["nodeId"]!.GetValue<string>() == id);
		Assert.False(completed.Params["cancelled"]!.GetValue<bool>());
		Assert.Null(completed.Params["error"]);

		var entries = (await stack.RpcAsync(browser, "session.entries", P($$"""{"nodeId":"{{id}}","limit":50}""")))["entries"]!.AsArray();
		var user = entries.Last(e => e!["role"]!.GetValue<string>() == "user")!;
		var assistant = entries.Last(e => e!["role"]!.GetValue<string>() == "assistant")!;
		Assert.Contains("Hello GPT from the phone", user["content"]!.GetValue<string>());
		Assert.Equal("Hello from the fake.", assistant["content"]!.GetValue<string>().Trim());
		Assert.Equal(CodexDaemonWiring.GptModelId, assistant["modelId"]!.GetValue<string>());
		Assert.Equal("xhigh", assistant["effort"]!.GetValue<string>());

		var launcher = stack.Daemon.Get<FakeCodexProcessLauncher>();
		var turnStart = launcher.Servers.SelectMany(s => s.RequestsOf("turn/start")).Single();
		Assert.Equal(CodexDaemonWiring.GptModelId, turnStart.Params.GetProperty("model").GetString());
		Assert.Equal("xhigh", turnStart.Params.GetProperty("effort").GetString());

		var after = await stack.RpcAsync(browser, "session.get", P($$"""{"nodeId":"{{id}}"}"""));
		Assert.Equal("xhigh", after["lastUsed"]!["effort"]!.GetValue<string>());
		Assert.Equal("codex", after["state"]!["engine"]!.GetValue<string>());
	}

	[Fact]
	public async Task CX42_4_Usage_of_the_ChatGPT_profile_carries_the_window_labels()
	{
		await using var stack = await WebStack.StartAsync(codex: true);
		var browser = await stack.LoginAsync();

		var usage = await WebStack.PollAsync<JsonNode>(async () =>
		{
			var r = await stack.RpcAsync(browser, "usage.get", P($$"""{"profileId":"{{CodexDaemonWiring.ProfileId}}"}"""));
			return r["state"]!.GetValue<string>() == "ok" ? r : null;
		});

		Assert.Equal("codex", usage["engine"]!.GetValue<string>());
		Assert.Equal("5h", usage["fiveHour"]!["label"]!.GetValue<string>());
		Assert.Equal("7d", usage["sevenDay"]!["label"]!.GetValue<string>());
	}

	[Theory]
	[InlineData("profiles.codexLogin", """{"mode":"browser"}""")]
	[InlineData("profiles.codexLogout", """{"profileId":"codex_1"}""")]
	[InlineData("profiles.codexLoginCancel", """{"loginId":"x"}""")]
	[InlineData("settings.get", """{"section":"general"}""")]
	[InlineData("profile.list", "{}")]
	public async Task CX42_5_Settings_and_ChatGPT_sign_in_stay_on_the_desktop(string method, string paramsJson)
	{
		await using var stack = await WebStack.StartAsync(codex: true);
		var browser = await stack.LoginAsync();

		var response = await stack.RpcRawAsync(browser, method, P(paramsJson));

		Assert.NotNull(response["error"]);
		Assert.Null(response["result"]);
	}
}
