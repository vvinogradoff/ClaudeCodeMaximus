namespace Tessyn.Web.Tests.Support;

/// <summary>Minimal SSE reader over an HttpClient (works with the in-memory test server).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SseClient : IAsyncDisposable
{
	private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

	private readonly CancellationTokenSource _lifetime = new();
	private HttpResponseMessage? _response;
	private StreamReader? _reader;

	public HttpResponseMessage Response => _response ?? throw new InvalidOperationException("Not open");

	public static async Task<SseClient> OpenAsync(HttpClient http, string url, Action<HttpRequestMessage>? configure = null)
	{
		var client = new SseClient();
		var request = new HttpRequestMessage(HttpMethod.Get, url);
		configure?.Invoke(request);
		client._response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, client._lifetime.Token);
		if (client._response.IsSuccessStatusCode)
			client._reader = new StreamReader(await client._response.Content.ReadAsStreamAsync(client._lifetime.Token));

		return client;
	}

	/// <summary>Reads the next data frame, skipping pings.</summary>
	public async Task<SseFrame> NextDataAsync(TimeSpan? timeout = null)
	{
		while (true)
		{
			var frame = await NextAsync(timeout);
			if (!frame.IsPing)
				return frame;
		}
	}

	/// <summary>Reads the next frame including ping comments.</summary>
	public async Task<SseFrame> NextAsync(TimeSpan? timeout = null)
	{
		var reader = _reader ?? throw new InvalidOperationException("Stream not open");
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		cts.CancelAfter(timeout ?? DefaultTimeout);

		string? id = null;
		string? data = null;
		while (true)
		{
			var line = await reader.ReadLineAsync(cts.Token) ?? throw new EndOfStreamException("SSE stream ended");
			if (line.StartsWith(": ping", StringComparison.Ordinal))
				return new SseFrame(null, null, true);
			if (line.StartsWith("id: ", StringComparison.Ordinal))
				id = line[4..];
			else if (line.StartsWith("data: ", StringComparison.Ordinal))
				data = line[6..];
			else if (line.Length == 0 && data is not null)
				return new SseFrame(id, data, false);
		}
	}

	/// <summary>True when the server ended the stream within the timeout.</summary>
	public async Task<bool> EndsAsync(TimeSpan? timeout = null)
	{
		var reader = _reader ?? throw new InvalidOperationException("Stream not open");
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		cts.CancelAfter(timeout ?? DefaultTimeout);
		try
		{
			while (await reader.ReadLineAsync(cts.Token) is not null)
			{
			}

			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _lifetime.CancelAsync();
		_response?.Dispose();
		_lifetime.Dispose();
	}
}
