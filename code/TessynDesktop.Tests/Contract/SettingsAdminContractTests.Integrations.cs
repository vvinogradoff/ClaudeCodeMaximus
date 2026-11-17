using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// Package J3 part 2 on the wire (MCP registry and OAuth, Telegram token, relay link, model refresh): the error codes,
/// the rule that an error never echoes a secret, the notifications, and the behaviours that need the whole daemon stack (the discovery limit, one authorization
/// at a time, the bridge starting and stopping with the token). The result shapes and the secrecy scan are in the main part of this class.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SettingsAdminContractTests
{
	// ── Errors ───────────────────────────────────────────────────────────────────────────────

	[Theory]
	[InlineData("mcp.upsert", """{}""")]                                                                                                                  // displayName
	[InlineData("mcp.upsert", """{"displayName":"x"}""")]                                                                                                // an http server needs a url
	[InlineData("mcp.upsert", """{"displayName":"x","url":"ftp://a.example.com"}""")]
	[InlineData("mcp.upsert", """{"displayName":"x","url":"https://user:pw@a.example.com/"}""")]                                                       // user info
	[InlineData("mcp.upsert", """{"id":"..\\evil","displayName":"x","url":"https://a.example.com"}""")]                                                  // the id is a file name
	[InlineData("mcp.upsert", """{"id":"a/b","displayName":"x","url":"https://a.example.com"}""")]
	[InlineData("mcp.upsert", """{"id":"x\n","displayName":"x","url":"https://a.example.com"}""")]                                                    // a trailing line break: `$` would have matched it
	[InlineData("mcp.upsert", """{"id":"x\r\n","displayName":"x","url":"https://a.example.com"}""")]
	[InlineData("mcp.upsert", """{"displayName":"x","url":"https://a.example.com","bogus":1}""")]                                                       // outside the request
	[InlineData("mcp.upsert", """{"displayName":"x","url":"https://a.example.com","transport":"carrier"}""")]
	[InlineData("mcp.upsert", """{"displayName":"x","url":"https://a.example.com","authType":"staticHeader"}""")]                                      // no header name
	[InlineData("mcp.upsert", """{"displayName":"x","url":"https://a.example.com","authType":"staticHeader","staticHeaderName":"A B","staticHeaderValue":"v"}""")]
	[InlineData("mcp.upsert", """{"displayName":"x","url":"https://a.example.com","authType":"staticHeader","staticHeaderName":"A","staticHeaderValue":"v\r\nX: y"}""")] // header injection
	[InlineData("mcp.upsert", """{"displayName":"x","url":"https://a.example.com","staticHeaderValue":"v"}""")]                                        // not a static-header server
	[InlineData("mcp.upsert", """{"displayName":"x","url":"https://a.example.com","oauth":{"clientId":"c"}}""")]                                         // not an OAuth server
	[InlineData("mcp.upsert", """{"displayName":"x","url":"https://a.example.com","authType":"oauth2","oauth":{"tokenEndpoint":"file:///c:/t"}}""")]
	[InlineData("mcp.upsert", """{"displayName":"x","transport":"stdio"}""")]                                                                            // no command
	[InlineData("mcp.upsert", """{"displayName":"x","transport":"stdio","command":"\\\\host\\share\\x.exe"}""")]                                          // UNC: refused before any disk access
	[InlineData("mcp.upsert", """{"displayName":"x","transport":"stdio","command":"\\\\?\\C:\\x.exe"}""")]
	[InlineData("mcp.upsert", """{"displayName":"x","transport":"stdio","command":"C:x.exe"}""")]
	[InlineData("mcp.upsert", """{"displayName":"x","transport":"stdio","command":"..\\x.exe"}""")]
	[InlineData("mcp.upsert", """{"displayName":"x","transport":"stdio","command":"x","workingDirectory":"\\\\host\\share"}""")]
	[InlineData("mcp.upsert", """{"displayName":"x","transport":"stdio","command":"x","workingDirectory":"work"}""")]
	[InlineData("mcp.upsert", """{"displayName":"x","transport":"stdio","command":"x","url":"https://a.example.com"}""")]                                // http member on a stdio server
	[InlineData("mcp.upsert", """{"id":"mcp1","url":"https://other.example.com/mcp"}""")]                                                               // a stored header value is not sent to a new origin
	[InlineData("mcp.setEnabled", """{"id":"nope","enabled":true}""")]
	[InlineData("mcp.setEnabled", """{"id":"mcp1"}""")]
	[InlineData("mcp.remove", """{}""")]
	[InlineData("mcp.authorize", """{"id":"nope"}""")]
	[InlineData("mcp.authorize", """{}""")]
	[InlineData("mcp.discover", """{}""")]
	[InlineData("mcp.discover", """{"url":"file:///c:/x"}""")]
	[InlineData("mcp.discover", """{"url":"https://user:pw@a.example.com"}""")]
	[InlineData("mcp.discover", """{"url":"https://a.example.com","oauth":{"tokenEndpoint":"ftp://x"}}""")]
	[InlineData("mcp.discover", """{"url":"https://a.example.com","oauth":{"clientSecret":"x"}}""")]                                                    // discovery takes no secret
	[InlineData("telegram.setToken", """{}""")]
	[InlineData("telegram.setToken", """{"token":"abc"}""")]
	[InlineData("telegram.setToken", """{"token":"123456:short"}""")]
	[InlineData("telegram.setToken", """{"token":"123456789:AAF bad space 0123456789abcdefgh"}""")]
	[InlineData("telegram.setToken", """{"token":"123456789:AAF/../0123456789abcdefgh"}""")]                                                           // goes into a URL path
	[InlineData("relay.configure", """{"enabled":true,"url":"https://u:pw@web.example.com","linkToken":"abc"}""")]
	[InlineData("relay.configure", """{"url":"https://web.example.com/?k=v","linkToken":"t"}""")]
	[InlineData("relay.configure", """{"url":"ftp://web.example.com","linkToken":"t"}""")]
	[InlineData("relay.configure", """{"url":"https://other.example.com"}""")]                                                                           // the stored token is not sent to a new origin
	[InlineData("relay.configure", """{"enabled":true,"url":"","linkToken":"t"}""")]
	[InlineData("relay.configure", """{"linkToken":"has space"}""")]
	[InlineData("relay.configure", """{"unknown":1}""")]
	public async Task BadParams_OfThePart2Methods_AreInvalidParams(string method, string paramsJson)
	{
		var response = await _env.Host.CallAsync(method, JsonNode.Parse(paramsJson));

		Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
	}

	[Fact]
	public async Task ARejectedWrite_ChangesNothing()
	{
		var before = _env.Mcp.List().Count;
		var relayBefore = _env.RelayStore.Load()!;

		await _env.Host.CallAsync("mcp.upsert", JsonNode.Parse("""{"displayName":"x","transport":"stdio","command":"\\\\10.0.0.5\\share\\x.exe"}"""));
		await _env.Host.CallAsync("relay.configure", JsonNode.Parse("""{"url":"https://other.example.com"}"""));
		await _env.Host.CallAsync("telegram.setToken", JsonNode.Parse("""{"token":"nope"}"""));

		Assert.Equal(before, _env.Mcp.List().Count);
		var relayAfter = _env.RelayStore.Load()!;
		Assert.Equal((relayBefore.Url, relayBefore.Token, relayBefore.Enabled), (relayAfter.Url, relayAfter.Token, relayAfter.Enabled));
		Assert.Equal(SettingsAdminEnv.TelegramBotToken, _env.TelegramTokens.Token);
		Assert.Empty(_env.McpTokens.Deleted);
	}

	[Fact]
	public async Task AnError_NeverEchoesASecretTheRequestCarried()
	{
		const string secretValue = "request-secret-value-9f8e7d6c5b4a";
		var failing = new (string Method, string Params)[]
		{
			("telegram.setToken", $$"""{"token":"{{secretValue}}"}"""),
			("telegram.setToken", $$"""{"token":"123456789:{{secretValue}}/../x"}"""),
			("mcp.upsert", $$"""{"displayName":"x","url":"https://a.example.com","authType":"staticHeader","staticHeaderName":"A","staticHeaderValue":"{{secretValue}}\r\nX: y"}"""),
			("mcp.upsert", $$"""{"displayName":"x","url":"https://a.example.com","staticHeaderValue":"{{secretValue}}"}"""),
			("mcp.upsert", $$$"""{"displayName":"x","url":"https://a.example.com","oauth":{"clientSecret":"{{{secretValue}}}"}}"""),
			("mcp.upsert", $$"""{"displayName":"x","url":"https://u:{{secretValue}}@a.example.com"}"""),
			("mcp.upsert", $$"""{"displayName":"x","url":"https://a.example.com","bogus":"{{secretValue}}"}"""),
			("relay.configure", $$"""{"linkToken":"{{secretValue}} x"}"""),
			("relay.configure", $$"""{"url":"https://u:{{secretValue}}@web.example.com","linkToken":"t"}"""),
			("mcp.discover", $$$"""{"url":"https://a.example.com","oauth":{"clientSecret":"{{{secretValue}}}"}}"""),
		};

		foreach (var (method, paramsJson) in failing)
		{
			var response = await _env.Host.CallAsync(method, JsonNode.Parse(paramsJson));

			Assert.NotNull(response["error"]);
			Assert.DoesNotContain(secretValue, response.ToJsonString());
			Assert.DoesNotContain(SettingsAdminEnv.TelegramBotToken, response.ToJsonString());
		}
	}

	// ── Notifications on the local stream ────────────────────────────────────────────────────

	[Fact]
	public async Task TheMcpWrites_PublishMcpChanged_WithTheServerId()
	{
		using var sse = await _env.Host.OpenStreamAsync();

		await _env.Host.CallAsync("mcp.upsert", JsonNode.Parse("""{"id":"mcp9","displayName":"New","url":"https://new.example.com/mcp"}"""));
		Assert.Equal("mcp9", (await NextAsync(sse, "mcp.changed")).Params!["id"]!.GetValue<string>());

		await _env.Host.CallAsync("mcp.setEnabled", JsonNode.Parse("""{"id":"mcp9","enabled":false}"""));
		Assert.Equal("mcp9", (await NextAsync(sse, "mcp.changed")).Params!["id"]!.GetValue<string>());

		await _env.Host.CallAsync("mcp.authorize", JsonNode.Parse("""{"id":"mcp2"}"""));
		Assert.Equal("mcp2", (await NextAsync(sse, "mcp.changed")).Params!["id"]!.GetValue<string>());

		await _env.Host.CallAsync("mcp.remove", JsonNode.Parse("""{"id":"mcp9"}"""));
		Assert.Equal("mcp9", (await NextAsync(sse, "mcp.changed")).Params!["id"]!.GetValue<string>());
	}

	[Fact]
	public async Task ModelsRefresh_PublishesCatalogChanged_AndAnswersTheCatalogSize()
	{
		using var sse = await _env.Host.OpenStreamAsync();
		_env.ModelCatalog.AfterRefresh = [new("gemma4:26b", string.Empty, "gemma4:26b", Models.ModelProvider.Ollama)];

		var response = await _env.Host.CallAsync("models.refresh");

		Assert.Equal(3, response["result"]!["count"]!.GetValue<int>());
		Assert.Empty((await NextAsync(sse, "catalog.changed")).Params!.AsObject());
		Assert.Equal(1, _env.ModelCatalog.Refreshes);
	}

	// ── Behaviour on the wire ────────────────────────────────────────────────────────────────

	[Fact]
	public async Task McpList_ShowsNoSecret_ButSaysWhichOnesAreStored()
	{
		var list = (await _env.Host.CallAsync("mcp.list"))["result"]!["servers"]!.AsArray();

		var docs = list.Single(s => s!["id"]!.GetValue<string>() == "mcp1")!;
		Assert.Equal("https://docs.example.com/mcp", docs["url"]!.GetValue<string>());        // the query is cut off
		Assert.True(docs["urlHasQuery"]!.GetValue<bool>());
		Assert.True(docs["hasStaticHeaderValue"]!.GetValue<bool>());
		Assert.Null(docs["staticHeaderValue"]);

		var tracker = list.Single(s => s!["id"]!.GetValue<string>() == "mcp2")!;
		Assert.Equal(("connected", true, "Alex Example"), (tracker["status"]!.GetValue<string>(), tracker["authorized"]!.GetValue<bool>(), tracker["connectedIdentity"]!.GetValue<string>()));
		Assert.True(tracker["oauth"]!["hasClientSecret"]!.GetValue<bool>());

		var local = list.Single(s => s!["id"]!.GetValue<string>() == "mcp3")!;
		Assert.True(local["hasArguments"]!.GetValue<bool>());
		Assert.Null(local["arguments"]);
	}

	[Fact]
	public async Task McpUpsert_KeepsTheStoredSecret_WhenTheRequestLeavesItOut()
	{
		await _env.Host.CallAsync("mcp.upsert", JsonNode.Parse("""{"id":"mcp1","displayName":"Docs renamed"}"""));

		var server = _env.Settings.Settings.ExternalMcpServers.Single(s => s.Id == "mcp1");
		Assert.Equal(("Docs renamed", SettingsAdminEnv.Secrets[1]), (server.DisplayName, server.StaticHeaderValue));
		Assert.Equal("https://docs.example.com/mcp?key=" + SettingsAdminEnv.Secrets[4], server.Url);
	}

	[Fact]
	public async Task McpDiscover_IsStoppedByTheDaemonTimeout_WhetherOrNotTheProbeHonoursItsToken()
	{
		_env.McpOAuth.OnDiscover = async (_, _, ct) => await Task.Delay(Timeout.Infinite, ct);   // honours the token
		var watch = System.Diagnostics.Stopwatch.StartNew();
		var honouring = await _env.Host.CallAsync("mcp.discover", JsonNode.Parse("""{"url":"https://slow.example.com/mcp"}"""));

		_env.McpOAuth.OnDiscover = (_, _, _) => new TaskCompletionSource().Task;                   // hangs and ignores it
		var ignoring = await _env.Host.CallAsync("mcp.discover", JsonNode.Parse("""{"url":"https://slow.example.com/mcp"}"""));

		Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"took {watch.Elapsed}");
		Assert.True(honouring["result"]!["timedOut"]!.GetValue<bool>());
		Assert.True(ignoring["result"]!["timedOut"]!.GetValue<bool>());
		Assert.Equal("", honouring["result"]!["config"]!["authorizationEndpoint"]!.GetValue<string>());
	}

	[Fact]
	public async Task McpAuthorize_RunsOneFlowPerServer_AndReportsAFailureWithoutASecret()
	{
		var gate = new TaskCompletionSource<Models.McpAuthorizeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		_env.McpOAuth.OnAuthorize = (_, _) => gate.Task;

		var first = _env.Host.CallAsync("mcp.authorize", JsonNode.Parse("""{"id":"mcp2"}"""));
		while (_env.McpOAuth.Authorized.Count == 0)
			await Task.Delay(10);
		var second = await _env.Host.CallAsync("mcp.authorize", JsonNode.Parse("""{"id":"mcp2"}"""));
		gate.SetResult(new Models.McpAuthorizeResult(false, "Timed out waiting for the browser authorization redirect."));
		var firstResult = (await first)["result"]!;

		Assert.False(second["result"]!["authorized"]!.GetValue<bool>());
		Assert.Contains("already running", second["result"]!["error"]!.GetValue<string>());
		Assert.False(firstResult["authorized"]!.GetValue<bool>());
		Assert.Equal("Timed out waiting for the browser authorization redirect.", firstResult["error"]!.GetValue<string>());
		Assert.Single(_env.McpOAuth.Authorized);
	}

	[Fact]
	public async Task TelegramToken_IsStoredWriteOnly_AndTheBridgeFollowsIt()
	{
		_env.Settings.Settings.TelegramEnabled = true;
		_env.TelegramTokens.Token = null;
		const string token = "555666777:AAEnewBotTokenForTheBridge_0123456789ab";

		var set = (await _env.Host.CallAsync("telegram.setToken", new JsonObject { ["token"] = token }))["result"]!;
		var cleared = (await _env.Host.CallAsync("telegram.clearToken"))["result"]!;

		Assert.Equal(1, _env.TelegramBridge.Starts);                                                    // started once, by the set
		Assert.Equal((true, true, true), (set["enabled"]!.GetValue<bool>(), set["running"]!.GetValue<bool>(), set["hasToken"]!.GetValue<bool>()));
		Assert.Equal((true, false, false), (cleared["enabled"]!.GetValue<bool>(), cleared["running"]!.GetValue<bool>(), cleared["hasToken"]!.GetValue<bool>()));
		Assert.Null(_env.TelegramTokens.Token);
		Assert.Equal(1, _env.TelegramBridge.Stops);
		Assert.DoesNotContain(token, set.ToJsonString() + cleared.ToJsonString());
	}

	[Fact]
	public async Task TelegramStatus_CarriesTheBridgesLastError_WithoutAnyToken()
	{
		_env.TelegramBridge.LastError = "Telegram rejected the bot token";

		var status = (await _env.Host.CallAsync("telegram.status"))["result"]!;

		Assert.Equal("Telegram rejected the bot token", status["lastError"]!.GetValue<string>());
		AssertCarriesNoSecret("telegram.status", status, accountEmailAllowed: false);
	}

	[Fact]
	public async Task RelayConfigure_StoresTheLink_AndRestartsTheConnectorOffTheRequest()
	{
		var response = await _env.Host.CallAsync("relay.configure", JsonNode.Parse("""{"url":"https://web2.example.com","linkToken":"new-link-token-0002"}"""));

		Assert.True(await _env.Relay.RestartSignal.WaitAsync(TimeSpan.FromSeconds(5)));
		var stored = _env.RelayStore.Load()!;
		Assert.Equal(("https://web2.example.com", "new-link-token-0002", true), (stored.Url, stored.Token, stored.Enabled));
		Assert.Equal("https://web2.example.com", response["result"]!["url"]!.GetValue<string>());
		Assert.DoesNotContain("new-link-token-0002", response.ToJsonString());
	}

	[Fact]
	public async Task RelayConfigure_KeepsTheStoredToken_WhenTheOriginStaysAndSwitchesOff()
	{
		await _env.Host.CallAsync("relay.configure", JsonNode.Parse("""{"enabled":false}"""));
		await _env.Host.CallAsync("relay.configure", JsonNode.Parse("""{"url":"https://web.example.com/other-base"}"""));   // same origin: no token needed

		Assert.True(await _env.Relay.RestartSignal.WaitAsync(TimeSpan.FromSeconds(5)));
		var stored = _env.RelayStore.Load()!;
		Assert.Equal((false, "https://web.example.com/other-base", SettingsAdminEnv.RelayLinkToken), (stored.Enabled, stored.Url, stored.Token));
	}

	[Fact]
	public async Task RelayStatus_ShowsTheStateAndTheLinkError_WithTheTokenCutOut()
	{
		_env.Relay.Status = new Models.RelayStatusModel(Models.RelayState.Error, DateTimeOffset.UnixEpoch, $"Link broke near {SettingsAdminEnv.RelayLinkToken} now");

		var status = (await _env.Host.CallAsync("relay.status"))["result"]!;

		Assert.Equal("error", status["state"]!.GetValue<string>());
		Assert.Equal("Link broke near *** now", status["lastError"]!.GetValue<string>());
		Assert.True(status["hasToken"]!.GetValue<bool>());
		Assert.DoesNotContain(SettingsAdminEnv.RelayLinkToken, status.ToJsonString());
	}

	[Fact]
	public async Task ModelsRefresh_ThatHangs_IsAnErrorAfterTheDaemonTimeout()
	{
		_env.ModelCatalog.OnRefresh = _ => new TaskCompletionSource().Task;   // ignores its token

		var response = await _env.Host.CallAsync("models.refresh");

		Assert.Equal(-32000, response["error"]!["code"]!.GetValue<int>());
		Assert.Contains("too long", response["error"]!["message"]!.GetValue<string>());
	}

	private static async Task<SseTestFrame> NextAsync(SseTestClient sse, string method, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (true)
		{
			var remaining = deadline - DateTime.UtcNow;
			if (remaining <= TimeSpan.Zero)
				throw new TimeoutException($"No {method} within {timeout}");

			var frame = await sse.NextAsync(remaining);
			if (frame.Method == method)
				return frame;
		}
	}
}
