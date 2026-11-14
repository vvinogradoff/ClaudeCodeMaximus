using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TessynDesktop;

namespace TessynDesktop.Tests.Contract;

/// <summary>One parsed SSE frame: its <c>id:</c> (0 when absent) and the JSON-RPC notification in <c>data:</c>.</summary>
internal sealed record SseTestFrame(long Id, JsonNode Notification)
{
	public string Method => Notification["method"]!.GetValue<string>();
	public JsonNode? Params => Notification["params"];
}

/// <summary>Minimal SSE reader over a real loopback connection to <c>GET /tdcp</c>.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class SseTestClient : IDisposable
{
	private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
	private readonly CancellationTokenSource _cts = new();
	private readonly Channel<SseTestFrame> _frames = Channel.CreateUnbounded<SseTestFrame>();

	private SseTestClient()
	{
	}

	/// <summary>The HTTP status of the stream request.</summary>
	public int StatusCode { get; private set; }

	/// <summary>Opens the stream and waits for the response headers (the server answers 200 right away with a comment line).</summary>
	public static async Task<SseTestClient> ConnectAsync(
		string baseUrl, string? token, string query = "", string? lastEventId = null, string? origin = null)
	{
		var client = new SseTestClient();
		var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + Constants.Agent.TdcpEndpointPath + query);
		request.Headers.Add("Accept", "text/event-stream");
		if (token != null)
			request.Headers.Add(Constants.Agent.TokenHeader, token);
		if (lastEventId != null)
			request.Headers.Add(Constants.Remote.Sse.LastEventIdHeader, lastEventId);
		if (origin != null)
			request.Headers.Add("Origin", origin);

		var response = await client._http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, client._cts.Token);
		client.StatusCode = (int)response.StatusCode;
		if (client.StatusCode == 200)
			_ = client.PumpAsync(response);
		return client;
	}

	/// <summary>The next frame, or a <see cref="TimeoutException"/>.</summary>
	public async Task<SseTestFrame> NextAsync(TimeSpan? timeout = null)
	{
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
		cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
		try
		{
			return await _frames.Reader.ReadAsync(cts.Token);
		}
		catch (OperationCanceledException)
		{
			throw new TimeoutException("No SSE frame arrived in time");
		}
	}

	/// <summary>True when no frame arrives within <paramref name="quiet"/>.</summary>
	public async Task<bool> StaysQuietAsync(TimeSpan quiet)
	{
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
		cts.CancelAfter(quiet);
		try
		{
			await _frames.Reader.WaitToReadAsync(cts.Token);
			return false;
		}
		catch (OperationCanceledException)
		{
			return true;
		}
	}

	private async Task PumpAsync(HttpResponseMessage response)
	{
		try
		{
			using var stream = await response.Content.ReadAsStreamAsync(_cts.Token);
			using var reader = new StreamReader(stream, Encoding.UTF8);
			long id = 0;
			while (await reader.ReadLineAsync(_cts.Token) is { } line)
			{
				if (line.StartsWith("id: ", StringComparison.Ordinal))
					id = long.Parse(line["id: ".Length..]);
				else if (line.StartsWith("data: ", StringComparison.Ordinal))
				{
					await _frames.Writer.WriteAsync(new SseTestFrame(id, JsonNode.Parse(line["data: ".Length..])!), _cts.Token);
					id = 0;
				}
			}
		}
		catch (Exception)
		{
			// Disposed or the server closed the stream.
		}
		finally
		{
			_frames.Writer.TryComplete();
		}
	}

	public void Dispose()
	{
		_cts.Cancel();
		_http.Dispose();
	}
}
