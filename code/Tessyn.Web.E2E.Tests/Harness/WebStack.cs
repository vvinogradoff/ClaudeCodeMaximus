using System.Net.Http.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>Real Tessyn.Web (in memory) + real RelayConnectorService + the seeded Core daemon, plus browser-like clients.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WebStack : IAsyncDisposable
{
	private int _rpcId;

	private WebStack(E2EWebFactory web, DaemonHarness daemon, RelayLink link)
	{
		Web = web;
		Daemon = daemon;
		Link = link;
	}

	public E2EWebFactory Web { get; }
	public DaemonHarness Daemon { get; }
	public RelayLink Link { get; }
	public SeedModel Seed => Daemon.Seed;

	/// <param name="dispatcher">Replaces the daemon's dispatcher (the not-remote-callable test); null uses the real one.</param>
	/// <param name="token">The bearer token the connector presents; default is the configured one.</param>
	/// <param name="codex">The daemon gets the Codex engine over the in-process fake app-server (TC.CX.42).</param>
	public static async Task<WebStack> StartAsync(TdcpDispatcher? dispatcher = null, string? token = null, bool waitOnline = true, bool codex = false)
	{
		var web = new E2EWebFactory();
		var daemon = await DaemonHarness.CreateAsync(codex);
		web.CreateClient().Dispose();

		var link = new RelayLink(
			dispatcher ?? daemon.Dispatcher,
			daemon.Bus,
			"http://localhost",
			token ?? E2EWebFactory.LinkToken,
			() => new HttpClient(web.Server.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan },
			daemon.Get<RelayBlobGateway>());
		var stack = new WebStack(web, daemon, link);
		link.Start();
		if (waitOnline)
			await link.WaitForStateAsync(RelayState.Connected);

		return stack;
	}

	public async Task<HttpClient> LoginAsync()
	{
		var browser = Web.CreateBrowser();
		var response = await browser.PostAsJsonAsync("/api/auth/login", new { email = E2EWebFactory.Email, password = E2EWebFactory.Password });
		response.EnsureSuccessStatusCode();
		return browser;
	}

	/// <summary>POST /api/rpc and return the whole JSON-RPC response (result or error).</summary>
	public async Task<JsonNode> RpcRawAsync(HttpClient browser, string method, JsonNode? @params = null)
	{
		var request = new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"] = Interlocked.Increment(ref _rpcId),
			["method"] = method,
			["params"] = @params ?? new JsonObject(),
		};
		var response = await browser.PostAsync("/api/rpc", JsonContent.Create(request));
		response.EnsureSuccessStatusCode();
		return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
	}

	/// <summary>Like <see cref="RpcRawAsync"/> but returns <c>result</c> and fails on an error response.</summary>
	public async Task<JsonNode> RpcAsync(HttpClient browser, string method, JsonNode? @params = null)
	{
		var response = await RpcRawAsync(browser, method, @params);
		if (response["error"] is not null)
			throw new InvalidOperationException($"{method} failed: {response["error"]}");

		return response["result"]!;
	}

	public static int ErrorCode(JsonNode response) => response["error"]!["code"]!.GetValue<int>();

	/// <summary>Waits until <paramref name="read"/> returns a non-null value (polling), for effects that are asynchronous by design.</summary>
	public static async Task<T> PollAsync<T>(Func<Task<T?>> read, TimeSpan? timeout = null) where T : class
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
		while (true)
		{
			var value = await read();
			if (value is not null)
				return value;

			if (DateTime.UtcNow > deadline)
				throw new TimeoutException("Condition not reached");

			await Task.Delay(50);
		}
	}

	public async ValueTask DisposeAsync()
	{
		Link.Dispose();
		Daemon.Dispose();
		Web.Dispose();
		await Task.CompletedTask;
	}
}
