using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Contract;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// The loopback-only guarantee of package J3 part 2 through the real relay transport (the way J1 review T8 does it for J1). The new modules sit in the
/// dispatcher of a real <see cref="RelayConnectorService"/>; the "web server" is the loopback link server of the connector tests. Every request for one of
/// the fifteen methods that arrives on the link is answered with -32601 and changes nothing (the requests carry real-looking secrets, a stdio command and a
/// new relay URL, the things a hostile web server would try), a remote method on the same link gets its result, and the two new notifications are not
/// forwarded to the web server.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LoopbackOnlyJ3bMethodsRelayTransportTests : IDisposable
{
	private const string Token = "link-token-for-tests";

	private static readonly (string Method, string Params)[] Requests =
	[
		("mcp.list", "{}"),
		("mcp.upsert", """{"displayName":"evil","transport":"stdio","command":"C:\\Users\\Public\\evil.exe"}"""),
		("mcp.remove", """{"id":"mcp1"}"""),
		("mcp.setEnabled", """{"id":"mcp1","enabled":false}"""),
		("mcp.discover", """{"url":"https://attacker.example.com/mcp"}"""),
		("mcp.authorize", """{"id":"mcp2"}"""),
		("telegram.status", "{}"),
		("telegram.setToken", """{"token":"111222333:AAEattackerBotTokenForTheTest_0123456789"}"""),
		("telegram.clearToken", "{}"),
		("relay.status", "{}"),
		("relay.configure", """{"enabled":true,"url":"https://attacker.example.com","linkToken":"attacker-token-0001"}"""),
		("models.refresh", "{}"),
		("settings.get", """{"section":"general"}"""),
		("settings.update", """{"section":"general","baseVersion":1,"patch":{}}"""),
	];

	private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "TessynJ3bRelayTests_" + Guid.NewGuid().ToString("N"));
	private readonly LoopbackLinkServer _server = new();
	private readonly RemoteEventBus _bus = new();
	private readonly SettingsAdminEnv _env = new();

	public void Dispose()
	{
		_server.Dispose();
		_bus.Dispose();
		_env.Dispose();
		try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort cleanup */ }
	}

	private RelayConnectorService NewService()
	{
		var store = new RelayConfigStore(Path.Combine(_tempDir, "web-relay.dat"));
		store.Save(new RelayConfigModel { Enabled = true, Url = _server.Url, Token = Token });
		var timings = new RelayConnectorTimings(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(120), TimeSpan.FromMinutes(1), [TimeSpan.FromMilliseconds(50)]);
		return new RelayConnectorService(store, _env.Dispatcher, _bus, new RelayHttpClientFactory(), timings);
	}

	[Fact]
	public async Task EveryJ3bMethod_ArrivingOnTheRelayLink_IsAnsweredWithMethodNotFound_AndChangesNothing()
	{
		var before = (_env.Mcp.List().Count, _env.TelegramTokens.Token, _env.RelayStore.Load()!.Url, _env.ModelCatalog.Refreshes, _env.McpOAuth.Authorized.Count, _env.McpOAuth.Discovered.Count);
		var service = NewService();
		service.Start();
		try
		{
			await WaitAsync(() => service.Status.State == RelayState.Connected);
			for (var i = 0; i < Requests.Length; i++)
				_server.Send("data: " + new JsonObject { ["jsonrpc"] = "2.0", ["id"] = "j3b-" + i, ["method"] = Requests[i].Method, ["params"] = JsonNode.Parse(Requests[i].Params) }.ToJsonString() + "\n\n");
			_server.Send("data: " + """{"jsonrpc":"2.0","id":"hello","method":"client.hello","params":{}}""" + "\n\n");

			await WaitAsync(() => _server.Replies.Count >= Requests.Length + 1);

			var replies = _server.Replies.SelectMany(body => JsonNode.Parse(body)!.AsArray()).ToDictionary(r => r!["id"]!.GetValue<string>(), r => r!);
			for (var i = 0; i < Requests.Length; i++)
				Assert.True(replies["j3b-" + i]["error"]!["code"]!.GetValue<int>() == -32601, Requests[i].Method);

			var hello = replies["hello"]["result"]!;
			Assert.NotNull(hello);
			var capabilities = hello["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
			Assert.DoesNotContain(capabilities, c => c is "mcp.registry" or "telegram.settings" or "relay.settings" or "models.admin");   // capabilities are announced on loopback only

			var after = (_env.Mcp.List().Count, _env.TelegramTokens.Token, _env.RelayStore.Load()!.Url, _env.ModelCatalog.Refreshes, _env.McpOAuth.Authorized.Count, _env.McpOAuth.Discovered.Count);
			Assert.Equal(before, after);
			Assert.Equal(0, _env.Relay.Restarts);
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task TheMcpChangedAndCatalogChangedNotifications_AreNotForwardedToTheWebServer_WhileATreeNotificationIs()
	{
		var service = NewService();
		service.Start();
		try
		{
			await WaitAsync(() => service.Status.State == RelayState.Connected);

			_bus.Publish("mcp.changed", new { id = "mcp1" });
			_bus.Publish("catalog.changed", new { });
			_bus.Publish("tree.changed", new { version = 1 });

			await WaitAsync(() => !_server.Events.IsEmpty);
			await Task.Delay(300);

			var methods = _server.Events.SelectMany(body => JsonNode.Parse(body)!["events"]!.AsArray()).Select(e => e!["method"]!.GetValue<string>()).ToList();
			Assert.Equal(["tree.changed"], methods);
		}
		finally
		{
			service.Stop();
		}
	}

	private static async Task WaitAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(8);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException("Timed out waiting for the relay link");
			await Task.Delay(20);
		}
	}
}
