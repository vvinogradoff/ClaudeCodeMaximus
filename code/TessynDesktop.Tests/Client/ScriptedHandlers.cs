using System.Net;
using System.Text;

namespace TessynDesktop.Tests.Client;

/// <summary>A response body that never produces data until its read is cancelled: an idle but open SSE stream.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class HangingStream : Stream
{
	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override void Flush() { }
	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		await Task.Delay(Timeout.Infinite, cancellationToken);
		return 0;
	}
}

/// <summary>
/// A scripted daemon in place of the network: answers <c>client.hello</c> POSTs and serves SSE GETs from a script, and records the
/// GETs. Used for server behaviour the real <c>TdcpServer</c> never shows (malformed frames, instant closes, slow answers).
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ScriptedDaemonHandler : HttpMessageHandler
{
	public const string HelloBody =
		"""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":1,"daemonVersion":"t","machineName":"m","runId":"r1","serverTimeUtc":"2026-11-14T00:00:00Z","capabilities":["events.local","events.resume"],"minProtocolVersion":1,"eventSeq":0}}""";

	private readonly List<string?> _lastEventIds = [];
	private readonly List<DateTime> _streamRequestTimes = [];

	/// <summary>Called for each GET with its 1-based number; returns the stream body (null = an idle open stream).</summary>
	public Func<int, string?> StreamBody { get; set; } = _ => null;

	/// <summary>Delay before a POST is answered (a slow daemon).</summary>
	public TimeSpan PostDelay { get; set; } = TimeSpan.Zero;

	/// <summary>When set, a POST throws this instead of answering.</summary>
	public Exception? PostFailure { get; set; }

	/// <summary>The Last-Event-ID header of each stream GET, in order (null when absent).</summary>
	public IReadOnlyList<string?> LastEventIds
	{
		get
		{
			lock (_lastEventIds)
				return [.. _lastEventIds];
		}
	}

	public IReadOnlyList<DateTime> StreamRequestTimes
	{
		get
		{
			lock (_lastEventIds)
				return [.. _streamRequestTimes];
		}
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.Method == HttpMethod.Post)
		{
			if (PostFailure != null)
				throw PostFailure;
			if (PostDelay > TimeSpan.Zero)
				await Task.Delay(PostDelay, cancellationToken);
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(HelloBody, Encoding.UTF8, "application/json") };
		}

		int number;
		lock (_lastEventIds)
		{
			_lastEventIds.Add(request.Headers.TryGetValues("Last-Event-ID", out var values) ? values.Single() : null);
			_streamRequestTimes.Add(DateTime.UtcNow);
			number = _lastEventIds.Count;
		}

		var body = StreamBody(number);
		HttpContent content = body == null
			? new StreamContent(new HangingStream())
			: new StringContent(body, Encoding.UTF8, "text/event-stream");
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
	}
}

/// <summary>Passes everything through but can answer the next <c>client.hello</c> POSTs with the previous answer (a stale view of the daemon).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class StaleHelloHandler : DelegatingHandler
{
	private string? _lastHelloBody;

	public StaleHelloHandler() : base(new HttpClientHandler())
	{
	}

	/// <summary>How many of the next hello POSTs are answered with the previous hello result.</summary>
	public int StaleHellos { get; set; }

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.Method != HttpMethod.Post)
			return await base.SendAsync(request, cancellationToken);

		if (StaleHellos > 0 && _lastHelloBody != null)
		{
			StaleHellos--;
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_lastHelloBody, Encoding.UTF8, "application/json") };
		}

		var response = await base.SendAsync(request, cancellationToken);
		_lastHelloBody = await response.Content.ReadAsStringAsync(cancellationToken);
		response.Content = new StringContent(_lastHelloBody, Encoding.UTF8, "application/json");
		return response;
	}
}
