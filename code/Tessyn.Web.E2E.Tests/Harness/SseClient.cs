namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>
/// SSE reader over an HttpClient. A background pump buffers every frame, so waiting for one notification never loses
/// the others that arrived meanwhile (a wait removes only the frame it matched).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SseClient : IAsyncDisposable
{
	private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

	private readonly CancellationTokenSource _lifetime = new();
	private readonly object _gate = new();
	private readonly List<SseFrame> _frames = new();
	private HttpResponseMessage? _response;
	private Task _pump = Task.CompletedTask;

	public HttpResponseMessage Response => _response ?? throw new InvalidOperationException("Not open");

	/// <summary>True once the server ended the stream.</summary>
	public bool Ended { get; private set; }

	public static async Task<SseClient> OpenAsync(HttpClient http, string url)
	{
		var client = new SseClient();
		var request = new HttpRequestMessage(HttpMethod.Get, url);
		client._response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, client._lifetime.Token);
		client._response.EnsureSuccessStatusCode();
		var reader = new StreamReader(await client._response.Content.ReadAsStreamAsync(client._lifetime.Token));
		client._pump = Task.Run(() => client.PumpAsync(reader));
		return client;
	}

	/// <summary>Waits for a data frame satisfying <paramref name="match"/> and consumes it; non-matching frames stay buffered.</summary>
	public async Task<SseFrame> WaitForAsync(Func<SseFrame, bool> match, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
		while (true)
		{
			lock (_gate)
			{
				var found = _frames.FindIndex(f => match(f));
				if (found >= 0)
				{
					var frame = _frames[found];
					_frames.RemoveAt(found);
					return frame;
				}
			}

			if (DateTime.UtcNow > deadline)
				throw new TimeoutException("Expected SSE frame did not arrive. Buffered: " + Describe());

			await Task.Delay(20);
		}
	}

	public Task<SseFrame> WaitForMethodAsync(string method, TimeSpan? timeout = null) =>
		WaitForAsync(f => f.Method == method, timeout);

	/// <summary>The next frame of any kind.</summary>
	public Task<SseFrame> NextAsync(TimeSpan? timeout = null) => WaitForAsync(_ => true, timeout);

	public string Describe()
	{
		lock (_gate)
			return string.Join(", ", _frames.Select(f => f.Method));
	}

	private async Task PumpAsync(StreamReader reader)
	{
		string? id = null;
		string? data = null;
		try
		{
			while (!_lifetime.IsCancellationRequested)
			{
				var line = await reader.ReadLineAsync(_lifetime.Token);
				if (line is null)
					break;

				if (line.StartsWith("id: ", StringComparison.Ordinal))
				{
					id = line[4..];
				}
				else if (line.StartsWith("data: ", StringComparison.Ordinal))
				{
					data = line[6..];
				}
				else if (line.Length == 0 && data is not null)
				{
					lock (_gate)
						_frames.Add(new SseFrame(id, data, false));
					id = null;
					data = null;
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Disposed.
		}
		catch (IOException)
		{
			// Connection closed.
		}
		finally
		{
			Ended = true;
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _lifetime.CancelAsync();
		_response?.Dispose();
		try
		{
			await _pump;
		}
		catch (Exception)
		{
			// Pump errors are not interesting after dispose.
		}

		_lifetime.Dispose();
	}
}
