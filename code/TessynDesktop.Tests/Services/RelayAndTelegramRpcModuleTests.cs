using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// The relay and Telegram methods of package J3 part 2 on their own (no other module, nothing stored beforehand): the first configuration, the binding of the
/// link token to its origin, the status of an unconfigured link, and the bridge following the token.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RelayAndTelegramRpcModuleTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "relay-tg-" + Guid.NewGuid().ToString("N")[..8]);
	private readonly RelayConfigStore _store;
	private readonly RecordingRelayConnector _connector = new();
	private readonly AppSettingsService _settings;
	private readonly FakeTelegramTokenStore _tokens = new() { Token = null };
	private readonly IntegrationTelegramBridge _bridge;
	private readonly TdcpDispatcher _dispatcher;

	public RelayAndTelegramRpcModuleTests()
	{
		Directory.CreateDirectory(_dir);
		_store    = new RelayConfigStore(Path.Combine(_dir, "web-relay.dat"));
		_settings = new AppSettingsService(_dir);
		_bridge   = new IntegrationTelegramBridge(_settings);
		_dispatcher = new TdcpDispatcher([new RelaySettingsRpcModule(_store, new Lazy<IRelayConnectorService>(_connector)), new TelegramSettingsRpcModule(_tokens, _bridge, _settings)]);
	}

	public void Dispose()
	{
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}

	private async Task<JsonNode> CallAsync(string method, string? paramsJson = null)
	{
		var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = paramsJson == null ? new JsonObject() : JsonNode.Parse(paramsJson) }.ToJsonString();
		return (await _dispatcher.HandleTextAsync(request, TdcpTransport.Loopback, CancellationToken.None))!;
	}

	// ── relay ────────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task RelayStatus_OfAnUnconfiguredLink_IsDisabledWithoutUrlOrToken()
	{
		var result = (await CallAsync("relay.status"))["result"]!;

		Assert.Equal(("disabled", false, false), (result["state"]!.GetValue<string>(), result["enabled"]!.GetValue<bool>(), result["hasToken"]!.GetValue<bool>()));
		Assert.Null(result["url"]);
	}

	[Fact]
	public async Task RelayConfigure_TheFirstTime_NeedsTheUrlAndTheToken_ToSwitchOn()
	{
		var withoutToken = await CallAsync("relay.configure", """{"enabled":true,"url":"https://web.example.com"}""");
		var withoutUrl = await CallAsync("relay.configure", """{"enabled":true,"linkToken":"token-0001"}""");
		var both = await CallAsync("relay.configure", """{"enabled":true,"url":"https://web.example.com","linkToken":"token-0001"}""");

		Assert.Equal(-32602, withoutToken["error"]!["code"]!.GetValue<int>());
		Assert.Equal(-32602, withoutUrl["error"]!["code"]!.GetValue<int>());
		Assert.NotNull(both["result"]);
		Assert.Equal(("https://web.example.com", "token-0001", true), (_store.Load()!.Url, _store.Load()!.Token, _store.Load()!.Enabled));
	}

	[Fact]
	public async Task RelayConfigure_CanStoreTheLinkSwitchedOff_WithoutAToken()
	{
		var result = await CallAsync("relay.configure", """{"enabled":false,"url":"https://web.example.com"}""");

		Assert.NotNull(result["result"]);
		Assert.Equal((false, "https://web.example.com", string.Empty), (_store.Load()!.Enabled, _store.Load()!.Url, _store.Load()!.Token));
	}

	[Theory]
	[InlineData("https://web.example.com:8443")]      // another port is another origin
	[InlineData("http://web.example.com")]            // so is another scheme
	[InlineData("https://web.example.org")]
	public async Task TheStoredToken_IsNeverSentToAnotherOrigin_WithoutTheTokenAgain(string url)
	{
		await CallAsync("relay.configure", """{"enabled":true,"url":"https://web.example.com","linkToken":"token-0001"}""");

		var moved = await CallAsync("relay.configure", $$"""{"url":"{{url}}"}""");
		var movedWithToken = await CallAsync("relay.configure", $$"""{"url":"{{url}}","linkToken":"token-0002"}""");

		Assert.Equal(-32602, moved["error"]!["code"]!.GetValue<int>());
		Assert.NotNull(movedWithToken["result"]);
		Assert.Equal((url, "token-0002"), (_store.Load()!.Url, _store.Load()!.Token));
	}

	[Fact]
	public async Task TheSameOrigin_KeepsTheToken_WhateverTheCaseOrPath()
	{
		await CallAsync("relay.configure", """{"enabled":true,"url":"https://web.example.com","linkToken":"token-0001"}""");

		var result = await CallAsync("relay.configure", """{"url":"https://WEB.example.com/base"}""");

		Assert.NotNull(result["result"]);
		Assert.Equal("token-0001", _store.Load()!.Token);
	}

	[Fact]
	public async Task RelayConfigure_RestartsTheConnector_OnceEach_AndAnEmptyTokenClearsIt()
	{
		await CallAsync("relay.configure", """{"enabled":true,"url":"https://web.example.com","linkToken":"token-0001"}""");
		Assert.True(await _connector.RestartSignal.WaitAsync(TimeSpan.FromSeconds(5)));

		var cleared = await CallAsync("relay.configure", """{"enabled":false,"linkToken":""}""");
		Assert.True(await _connector.RestartSignal.WaitAsync(TimeSpan.FromSeconds(5)));

		Assert.Equal(2, _connector.Restarts);
		Assert.False(cleared["result"]!["hasToken"]!.GetValue<bool>());
		Assert.Equal(string.Empty, _store.Load()!.Token);
	}

	[Fact]
	public async Task RelayStatus_CutsTheTokenOutOfAnErrorText_AndCapsItsLength()
	{
		await CallAsync("relay.configure", """{"enabled":true,"url":"https://web.example.com","linkToken":"token-0001"}""");
		_connector.Status = new RelayStatusModel(RelayState.Error, DateTimeOffset.UnixEpoch, "failed with token-0001\r\n" + new string('x', 1000));

		var error = (await CallAsync("relay.status"))["result"]!["lastError"]!.GetValue<string>();

		Assert.DoesNotContain("token-0001", error);
		Assert.DoesNotContain('\n', error);
		Assert.True(error.Length <= Constants.IntegrationsApi.MaxErrorLength);
	}

	[Fact]
	public async Task RelayStatus_OfAnUrlWithAQueryOrUserInfoStoredByAnOlderBuild_ShowsOnlyTheBase()
	{
		_store.Save(new RelayConfigModel { Enabled = true, Url = "https://user:pw-0003@web.example.com/base?apikey=0004", Token = "t" });

		var text = (await CallAsync("relay.status"))["result"]!.ToJsonString();

		Assert.Contains("https://web.example.com/base", text);
		Assert.DoesNotContain("pw-0003", text);
		Assert.DoesNotContain("0004", text);
	}

	// ── telegram ─────────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task TelegramSetToken_WithTheBridgeOff_OnlyStoresTheToken()
	{
		var result = (await CallAsync("telegram.setToken", """{"token":" 555666777:AAEtrimmedTokenForTheTest_0123456789ab "}"""))["result"]!;

		Assert.Equal("555666777:AAEtrimmedTokenForTheTest_0123456789ab", _tokens.Token);   // trimmed
		Assert.Equal((false, false, true), (result["enabled"]!.GetValue<bool>(), result["running"]!.GetValue<bool>(), result["hasToken"]!.GetValue<bool>()));
		Assert.Equal(0, _bridge.Starts);
	}

	[Fact]
	public async Task TelegramSetToken_WithTheBridgeOn_StartsItOnce_AndASecondTokenDoesNotRestartIt()
	{
		_settings.Settings.TelegramEnabled = true;

		await CallAsync("telegram.setToken", """{"token":"555666777:AAEfirstTokenForTheTest_0123456789ab"}""");
		await CallAsync("telegram.setToken", """{"token":"555666777:AAEsecondTokenForTheTest_0123456789a"}""");

		Assert.Equal(1, _bridge.Starts);
		Assert.Equal("555666777:AAEsecondTokenForTheTest_0123456789a", _tokens.Token);
	}

	[Fact]
	public async Task TelegramClearToken_StopsTheBridge_AndIsHarmlessWithoutAToken()
	{
		_settings.Settings.TelegramEnabled = true;
		await CallAsync("telegram.setToken", """{"token":"555666777:AAEfirstTokenForTheTest_0123456789ab"}""");

		var cleared = (await CallAsync("telegram.clearToken"))["result"]!;
		var again = await CallAsync("telegram.clearToken");

		Assert.Equal((true, false, false), (cleared["enabled"]!.GetValue<bool>(), cleared["running"]!.GetValue<bool>(), cleared["hasToken"]!.GetValue<bool>()));
		Assert.NotNull(again["result"]);
		Assert.Null(_tokens.Token);
	}

	[Fact]
	public async Task TelegramStatus_ReportsTheBridgeError_AndHasTokenFromTheStore()
	{
		_bridge.LastError = "Telegram answered with error 429";
		_tokens.Token = "555666777:AAEfirstTokenForTheTest_0123456789ab";

		var status = (await CallAsync("telegram.status"))["result"]!;

		Assert.Equal(("Telegram answered with error 429", true), (status["lastError"]!.GetValue<string>(), status["hasToken"]!.GetValue<bool>()));
		Assert.DoesNotContain("555666777", status.ToJsonString());
	}
}
