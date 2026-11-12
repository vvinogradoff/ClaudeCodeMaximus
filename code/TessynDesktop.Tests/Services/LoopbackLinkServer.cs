using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// In-test stand-in for the web server's /link endpoints (contract section 6), on an ephemeral loopback port:
/// serves an SSE stream the test feeds frames into and records the POSTed replies and events.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class LoopbackLinkServer : IDisposable
{
	private readonly HttpListener _listener = new();
	private readonly CancellationTokenSource _cts = new();
	private Channel<string>? _current;
	private int _connections;

	public LoopbackLinkServer()
	{
		var probe = new TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		var port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();

		Url = $"http://127.0.0.1:{port}";
		_listener.Prefixes.Add(Url + "/");
		_listener.Start();
		_ = Task.Run(AcceptLoopAsync);
	}

	public string Url { get; }

	/// <summary>When set, every stream request is answered with this status instead of an SSE stream.</summary>
	public int? StreamStatus { get; set; }

	public int Connections => Volatile.Read(ref _connections);

	public ConcurrentQueue<string> Replies { get; } = new();

	public ConcurrentQueue<string> Events { get; } = new();

	public ConcurrentQueue<(string Authorization, string Daemon, string Protocol)> StreamHeaders { get; } = new();

	/// <summary>Writes one raw SSE text block (e.g. "data: {...}\n\n" or ": ping\n\n") to the current stream.</summary>
	public void Send(string raw) => _current?.Writer.TryWrite(raw);

	/// <summary>Ends the current stream from the server side.</summary>
	public void CloseStream() => _current?.Writer.TryComplete();

	private async Task AcceptLoopAsync()
	{
		while (!_cts.IsCancellationRequested)
		{
			HttpListenerContext ctx;
			try
			{
				ctx = await _listener.GetContextAsync();
			}
			catch
			{
				return;
			}

			_ = Task.Run(() => HandleAsync(ctx));
		}
	}

	private async Task HandleAsync(HttpListenerContext ctx)
	{
		try
		{
			var path = ctx.Request.Url!.AbsolutePath;
			if (ctx.Request.HttpMethod == "GET" && path == "/link/stream")
			{
				await HandleStreamAsync(ctx);
				return;
			}

			using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
			var body = await reader.ReadToEndAsync();
			if (path == "/link/reply")
				Replies.Enqueue(body);
			else if (path == "/link/events")
				Events.Enqueue(body);

			ctx.Response.StatusCode = 204;
			ctx.Response.Close();
		}
		catch
		{
			// Client went away.
		}
	}

	private async Task HandleStreamAsync(HttpListenerContext ctx)
	{
		Interlocked.Increment(ref _connections);
		StreamHeaders.Enqueue((
			ctx.Request.Headers["Authorization"] ?? string.Empty,
			ctx.Request.Headers["X-Tessyn-Daemon"] ?? string.Empty,
			ctx.Request.Headers["X-Tessyn-Protocol"] ?? string.Empty));

		if (StreamStatus is { } status)
		{
			ctx.Response.StatusCode = status;
			ctx.Response.Close();
			return;
		}

		var channel = Channel.CreateUnbounded<string>();
		_current = channel;
		ctx.Response.ContentType = "text/event-stream";
		ctx.Response.SendChunked = true;
		var output = ctx.Response.OutputStream;

		try
		{
			await WriteAsync(output, ": hello\n\n");
			await foreach (var raw in channel.Reader.ReadAllAsync(_cts.Token))
				await WriteAsync(output, raw);
		}
		catch
		{
			// Client disconnected or server disposed.
		}
		finally
		{
			try { ctx.Response.Close(); } catch { /* already closed */ }
		}
	}

	private static async Task WriteAsync(Stream output, string text)
	{
		var bytes = Encoding.UTF8.GetBytes(text);
		await output.WriteAsync(bytes);
		await output.FlushAsync();
	}

	public void Dispose()
	{
		_cts.Cancel();
		try { _listener.Close(); } catch { /* best effort */ }
	}
}
