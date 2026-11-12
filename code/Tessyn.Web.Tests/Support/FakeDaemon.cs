using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Tessyn.Web.Tests.Support;

/// <summary>Plays the desktop connector: holds /link/stream open, reads requests, posts replies and events.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeDaemon : IAsyncDisposable
{
	private readonly HttpClient _http;
	private readonly SseClient _stream;

	private FakeDaemon(HttpClient http, SseClient stream)
	{
		_http = http;
		_stream = stream;
	}

	public SseClient Stream => _stream;

	public static async Task<FakeDaemon> ConnectAsync(TessynWebFactory factory, string token = TessynWebFactory.LinkToken)
	{
		var http = factory.CreateDaemonHttp();
		var stream = await SseClient.OpenAsync(http, Constants.Paths.LinkStream, request =>
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			request.Headers.Add(Constants.Headers.Daemon, TessynWebFactory.DaemonName);
			request.Headers.Add(Constants.Headers.Protocol, "1");
		});
		var daemon = new FakeDaemon(http, stream);
		return daemon;
	}

	public async Task<JsonDocument> ReadRequestAsync(TimeSpan? timeout = null)
	{
		var frame = await _stream.NextDataAsync(timeout);
		return JsonDocument.Parse(frame.Data!);
	}

	public Task<HttpResponseMessage> ReplyAsync(string id, string resultJson)
	{
		return PostAsync(Constants.Paths.LinkReply, $"[{{\"jsonrpc\":\"2.0\",\"id\":\"{id}\",\"result\":{resultJson}}}]");
	}

	public Task<HttpResponseMessage> ReplyErrorAsync(string id, int code, string message)
	{
		return PostAsync(Constants.Paths.LinkReply, $"[{{\"jsonrpc\":\"2.0\",\"id\":\"{id}\",\"error\":{{\"code\":{code},\"message\":\"{message}\"}}}}]");
	}

	public Task<HttpResponseMessage> PostEventsAsync(string eventsJson)
	{
		return PostAsync(Constants.Paths.LinkEvents, $"{{\"events\":{eventsJson}}}");
	}

	public async Task<HttpResponseMessage> PostAsync(string path, string json, string token = TessynWebFactory.LinkToken)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, path)
		{
			Content = new StringContent(json, Encoding.UTF8, Constants.ContentTypes.Json),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		return await _http.SendAsync(request);
	}

	public async ValueTask DisposeAsync()
	{
		await _stream.DisposeAsync();
		_http.Dispose();
	}
}
