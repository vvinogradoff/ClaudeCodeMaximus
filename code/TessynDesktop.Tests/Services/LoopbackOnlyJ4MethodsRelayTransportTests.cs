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
/// The loopback-only guarantee of package J4 through the real relay transport (the way J1 review T8 does it for J1). The J4 modules sit in the
/// dispatcher of a real <see cref="RelayConnectorService"/>; the "web server" is the loopback link server of the connector tests. Every J4 request
/// that arrives on the link is answered with -32601, while a remote method (<c>client.hello</c>) on the same link gets its result, and the
/// <c>schedule.changed</c> notification is not forwarded to the web server.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LoopbackOnlyJ4MethodsRelayTransportTests : IDisposable
{
	private const string Token = "link-token-for-tests";

	private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "TessynJ4RelayTests_" + Guid.NewGuid().ToString("N"));
	private readonly LoopbackLinkServer _server = new();
	private readonly RemoteEventBus _bus = new();
	private readonly ImportWireEnv _env = new();

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
	public async Task EveryJ4Method_ArrivingOnTheRelayLink_IsAnsweredWithMethodNotFound()
	{
		var service = NewService();
		service.Start();
		try
		{
			await WaitAsync(() => service.Status.State == RelayState.Connected);
			var methods = ImportContractTests.J4Methods;
			for (var i = 0; i < methods.Length; i++)
				_server.Send("data: " + new JsonObject { ["jsonrpc"] = "2.0", ["id"] = "j4-" + i, ["method"] = methods[i], ["params"] = new JsonObject { ["nodeId"] = _env.Session.NodeId, ["query"] = "x" } }.ToJsonString() + "\n\n");
			_server.Send("data: " + """{"jsonrpc":"2.0","id":"hello","method":"client.hello","params":{}}""" + "\n\n");

			await WaitAsync(() => _server.Replies.Count >= methods.Length + 1);

			var replies = _server.Replies.SelectMany(body => JsonNode.Parse(body)!.AsArray()).ToDictionary(r => r!["id"]!.GetValue<string>(), r => r!);
			for (var i = 0; i < methods.Length; i++)
				Assert.Equal(-32601, replies["j4-" + i]["error"]!["code"]!.GetValue<int>());
			Assert.NotNull(replies["hello"]["result"]);
		}
		finally
		{
			service.Stop();
		}
	}

	[Fact]
	public async Task TheScheduleChangedNotification_IsNotForwardedToTheWebServer_WhileATreeNotificationIs()
	{
		var service = NewService();
		service.Start();
		try
		{
			await WaitAsync(() => service.Status.State == RelayState.Connected);

			_bus.Publish("schedule.changed", new { nodeId = "n1" });
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
