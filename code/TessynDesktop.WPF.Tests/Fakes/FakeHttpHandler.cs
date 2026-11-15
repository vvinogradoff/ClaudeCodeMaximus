using System.Net;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An <see cref="HttpMessageHandler"/> that answers from a function and records every request (nothing touches the network).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeHttpHandler : HttpMessageHandler
{
	private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

	public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
	{
		_respond = respond;
	}

	private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _respondAsync;

	/// <summary>A handler whose answer can wait (for a gate or for cancellation).</summary>
	public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respondAsync)
	{
		_respond = _ => throw new InvalidOperationException();
		_respondAsync = respondAsync;
	}

	public List<HttpRequestMessage> Requests { get; } = [];

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		Requests.Add(request);
		return _respondAsync != null ? _respondAsync(request, cancellationToken) : Task.FromResult(_respond(request));
	}

	public static HttpResponseMessage Ok(byte[] body, string contentType, bool declareLength = true)
	{
		// Without a declared length the body is a forward-only stream, so the size is known only by reading it (a hostile server can do that).
		HttpContent content = declareLength ? new ByteArrayContent(body) : new StreamContent(new ForwardOnlyStream(new MemoryStream(body)));
		content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
	}

	private sealed class ForwardOnlyStream : Stream
	{
		private readonly Stream _inner;

		public ForwardOnlyStream(Stream inner)
		{
			_inner = inner;
		}

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	public static HttpResponseMessage Redirect(string location, HttpStatusCode code = HttpStatusCode.Found)
	{
		var response = new HttpResponseMessage(code);
		response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
		return response;
	}
}
