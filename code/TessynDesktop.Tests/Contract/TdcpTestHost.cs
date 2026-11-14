using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// A real <see cref="TdcpServer"/> on an ephemeral loopback port, wired to a bus, an event log and the given dispatcher.
/// Shared by the SSE and contract tests.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TdcpTestHost : IDisposable
{
	public const string Token = "contract-test-token";

	private readonly HttpClient _http = new();

	public TdcpTestHost(TdcpDispatcher dispatcher, int logCapacity = Constants.Remote.EventLog.DefaultCapacity, RemoteEventBus? bus = null, DaemonRunInfo? runInfo = null)
	{
		Bus      = bus ?? new RemoteEventBus();
		Log      = new EventLog(Bus, capacity: logCapacity);
		RunInfo  = runInfo ?? new DaemonRunInfo();
		Client   = new WireFakeDaemonClient();
		Dispatcher = dispatcher;
		Server   = new TdcpServer(Client, dispatcher, Token, Log, RunInfo);

		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();

		BaseUrl = $"http://127.0.0.1:{port}";
		Server.Start(port);
	}

	public RemoteEventBus Bus { get; }
	public EventLog Log { get; }
	public DaemonRunInfo RunInfo { get; }
	internal WireFakeDaemonClient Client { get; }
	public TdcpDispatcher Dispatcher { get; }
	public TdcpServer Server { get; }
	public string BaseUrl { get; }

	/// <summary>Publishes <paramref name="count"/> numbered <c>session.changed</c> notifications on the bus.</summary>
	public void Publish(int count, int first = 0)
	{
		for (var i = 0; i < count; i++)
			Bus.Publish("session.changed", new SamplePayload("n" + (first + i), null, first + i));
	}

	/// <summary>POSTs a raw body to <c>/tdcp</c> with the given token and optional Origin.</summary>
	public async Task<HttpResponseMessage> PostAsync(string body, string? token = Token, string? origin = null)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + Constants.Agent.TdcpEndpointPath)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};
		if (token != null)
			request.Headers.Add(Constants.Agent.TokenHeader, token);
		if (origin != null)
			request.Headers.Add("Origin", origin);
		return await _http.SendAsync(request);
	}

	/// <summary>Calls one method over HTTP and returns the whole JSON-RPC response.</summary>
	public async Task<JsonNode> CallAsync(string method, JsonNode? @params = null)
	{
		var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method };
		if (@params != null)
			request["params"] = @params.DeepClone();
		using var response = await PostAsync(request.ToJsonString());
		return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
	}

	public Task<SseTestClient> OpenStreamAsync(string query = "", string? lastEventId = null) =>
		SseTestClient.ConnectAsync(BaseUrl, Token, query, lastEventId);

	public void Dispose()
	{
		Server.Dispose();
		Log.Dispose();
		Bus.Dispose();
		_http.Dispose();
	}
}
