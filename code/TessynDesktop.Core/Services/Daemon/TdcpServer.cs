using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Hosts the Tessyn Daemon Control Protocol (TDCP) over Streamable HTTP (JSON-RPC 2.0).
/// Handles HTTP and token auth only; every JSON-RPC request is handed to the shared <see cref="TdcpDispatcher"/>
/// (loopback transport). The SSE stream carries the stamped <see cref="IEventLog"/> notifications (with ids and
/// Last-Event-ID resume) and, only on <c>?legacy=1</c>, the legacy <see cref="IDaemonClient"/> tree/schedule frames.
/// Suitable for in-process hosting behind AgentMcpServer's existing HttpListener and for
/// standalone self-hosting in tests via <see cref="Start"/>/<see cref="Stop"/>.
/// POST → single JSON-RPC response; GET → SSE push stream.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TdcpServer : IDisposable
{
	private static readonly ILogger _log = Log.ForContext<TdcpServer>();

	private readonly IDaemonClient           _client;
	private readonly TdcpDispatcher          _dispatcher;
	private readonly string                  _token;
	private readonly IEventLog?              _eventLog;
	private readonly IDaemonRunInfo?         _runInfo;
	private readonly CancellationTokenSource _cts = new();

	private HttpListener? _listener;

	public TdcpServer(IDaemonClient client, TdcpDispatcher dispatcher, string token, IEventLog? eventLog = null, IDaemonRunInfo? runInfo = null)
	{
		_client     = client;
		_dispatcher = dispatcher;
		_token      = token;
		_eventLog   = eventLog;
		_runInfo    = runInfo;
	}

	/// <summary>Starts a self-hosted HttpListener for standalone use (tests, future daemon host).</summary>
	public void Start(int port)
	{
		if (_listener != null)
			return;

		_listener = new HttpListener();
		_listener.Prefixes.Add($"http://127.0.0.1:{port}/");
		_listener.Start();
		_ = AcceptLoopAsync(_cts.Token);
		_log.Debug("TdcpServer self-hosted on port {Port}", port);
	}

	/// <summary>Stops the server and cancels all active SSE streams.</summary>
	public void Stop()
	{
		_cts.Cancel();
		try { _listener?.Stop(); } catch { /* best effort */ }
		_listener = null;
	}

	/// <inheritdoc />
	public void Dispose() => Stop();

	/// <summary>
	/// Handles one incoming HTTP context routed here from AgentMcpServer (or from the self-hosted accept loop).
	/// POST → single JSON-RPC response; GET with Accept: text/event-stream → SSE push stream.
	/// </summary>
	public async Task HandleAsync(HttpListenerContext ctx)
	{
		try
		{
			switch (ctx.Request.HttpMethod)
			{
				case "POST":
					await HandlePostAsync(ctx);
					break;
				case "GET":
					await HandleGetSseAsync(ctx);
					break;
				default:
					ctx.Response.StatusCode = 405;
					ctx.Response.Close();
					break;
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "TdcpServer: unhandled error on {Method} {Path}",
				ctx.Request.HttpMethod, ctx.Request.Url?.AbsolutePath);
			try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* best effort */ }
		}
	}

	// ── Accept loop (self-hosted mode) ────────────────────────────────────────────

	private async Task AcceptLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				var ctx = await _listener!.GetContextAsync();
				_ = HandleAsync(ctx);
			}
			catch (HttpListenerException) when (ct.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "TdcpServer: error in accept loop");
			}
		}
	}

	// ── POST handler ──────────────────────────────────────────────────────────────

	private async Task HandlePostAsync(HttpListenerContext ctx)
	{
		if (!await AuthorizeAsync(ctx))
			return;

		using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
		var body = await reader.ReadToEndAsync();

		var response = await _dispatcher.HandleTextAsync(body, TdcpTransport.Loopback, CancellationToken.None);
		await WriteJsonResponseAsync(ctx, response.ToJsonString(), 200);
	}

	// ── Request checks ────────────────────────────────────────────────────────────

	/// <summary>
	/// Origin first (a request carrying one comes from a browser context, e.g. the planned in-app browser pane, and is never a
	/// legitimate TDCP client: 403), then the token (401). Both answer with a JSON-RPC error body. Returns false after having
	/// written the rejection.
	/// </summary>
	private async Task<bool> AuthorizeAsync(HttpListenerContext ctx)
	{
		if (!string.IsNullOrEmpty(ctx.Request.Headers["Origin"]))
		{
			await WriteJsonResponseAsync(ctx, ErrorResponse("Requests with an Origin header are not allowed").ToJsonString(), 403);
			return false;
		}

		if (!TokenMatches(ctx.Request.Headers[Constants.Agent.TokenHeader]))
		{
			await WriteJsonResponseAsync(ctx, ErrorResponse("Invalid or missing X-CMX-Token").ToJsonString(), 401);
			return false;
		}

		return true;
	}

	private bool TokenMatches(string? candidate)
	{
		if (string.IsNullOrEmpty(candidate))
			return false;

		// Hashing first gives both sides the same length, so the comparison is constant-time for any input.
		var expected = SHA256.HashData(Encoding.UTF8.GetBytes(_token));
		var actual   = SHA256.HashData(Encoding.UTF8.GetBytes(candidate));
		return CryptographicOperations.FixedTimeEquals(expected, actual);
	}

	private static JsonNode ErrorResponse(string message) =>
		new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"]      = null,
			["error"]   = new JsonObject { ["code"] = -32600, ["message"] = message },
		};

	// ── GET SSE handler ───────────────────────────────────────────────────────────

	/// <summary>One frame waiting to be written; <paramref name="Seq"/> 0 means "no event id" (legacy frames).</summary>
	private readonly record struct SseFrame(long Seq, string Json);

	private async Task HandleGetSseAsync(HttpListenerContext ctx)
	{
		if (!await AuthorizeAsync(ctx))
			return;

		var legacy      = ctx.Request.QueryString[Constants.Remote.Sse.LegacyQuery] == "1";
		var clientRunId = ctx.Request.QueryString[Constants.Remote.Sse.RunIdQuery];
		long? lastEventId = long.TryParse(ctx.Request.Headers[Constants.Remote.Sse.LastEventIdHeader], out var parsed) ? parsed : null;

		ctx.Response.StatusCode  = 200;
		ctx.Response.ContentType = "text/event-stream";
		ctx.Response.Headers.Add("Cache-Control", "no-cache");
		ctx.Response.Headers.Add("Connection",    "keep-alive");

		// Bounded channel buffers frames while writing; DropOldest prevents unbounded growth. A drop shows up as a seq
		// discontinuity in the writer loop, which turns it into a resync frame.
		var channel = Channel.CreateBounded<SseFrame>(new BoundedChannelOptions(Constants.Remote.Sse.ChannelCapacity)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
		});

		// Use a per-connection CT linked to the server-lifetime CT so either disconnect or
		// server Stop() terminates the stream cleanly.
		using var connectionCts = new CancellationTokenSource();
		using var linkedCts     = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, connectionCts.Token);
		var ct = linkedCts.Token;

		void OnAppended(RemoteNotification notification) =>
			channel.Writer.TryWrite(new SseFrame(notification.Seq, NotificationJson(notification)));

		void OnTreeChanged(object? sender, TreeChangedEventArgs e)
		{
			var notification = new JsonObject
			{
				["jsonrpc"] = "2.0",
				["method"]  = "tree.changed",
				["params"]  = JsonNode.Parse(JsonSerializer.Serialize(e, TdcpJsonOptions.Default)),
			};
			channel.Writer.TryWrite(new SseFrame(0, notification.ToJsonString()));
		}

		void OnScheduleChanged(object? sender, EventArgs e)
		{
			var notification = new JsonObject
			{
				["jsonrpc"] = "2.0",
				["method"]  = "schedule.changed",
				["params"]  = new JsonObject(),
			};
			channel.Writer.TryWrite(new SseFrame(0, notification.ToJsonString()));
		}

		// Subscribe before replaying so no event falls between the replay and the live stream; the writer drops duplicates by seq.
		if (_eventLog != null)
			_eventLog.Appended += OnAppended;
		if (legacy)
		{
			_client.TreeChanged     += OnTreeChanged;
			_client.ScheduleChanged += OnScheduleChanged;
		}

		try
		{
			var writer = new StreamWriter(ctx.Response.OutputStream, Encoding.UTF8, leaveOpen: true);
			await using (writer.ConfigureAwait(false))
			{
				// Opens the stream for the client at once, before the first event.
				await writer.WriteAsync(": connected\n\n").ConfigureAwait(false);
				await writer.FlushAsync().ConfigureAwait(false);

				var lastSent = _eventLog?.HeadSeq ?? 0;

				if (_eventLog != null && lastEventId is { } resumeFrom)
				{
					lastSent = resumeFrom;
					var replay = ResolveReplay(resumeFrom, clientRunId);
					if (replay.ResyncReason != null)
					{
						await WriteFrameAsync(writer, replay.HeadSeq, ResyncJson(replay.ResyncReason, replay.HeadSeq)).ConfigureAwait(false);
						lastSent = replay.HeadSeq;
					}
					else
					{
						foreach (var notification in replay.Events)
						{
							await WriteFrameAsync(writer, notification.Seq, NotificationJson(notification)).ConfigureAwait(false);
							lastSent = notification.Seq;
						}
					}
				}

				await foreach (var frame in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
				{
					if (frame.Seq == 0)
					{
						await WriteFrameAsync(writer, 0, frame.Json).ConfigureAwait(false);
						continue;
					}

					if (frame.Seq <= lastSent)
						continue; // Already delivered by the replay (or older than the connect point).

					if (frame.Seq != lastSent + 1)
					{
						// Seq is contiguous for every stream, so a hole means the queue dropped frames.
						var head = _eventLog?.HeadSeq ?? frame.Seq;
						await WriteFrameAsync(writer, frame.Seq - 1, ResyncJson(Constants.Remote.EventLog.ResyncReasons.Overflow, head)).ConfigureAwait(false);
					}

					await WriteFrameAsync(writer, frame.Seq, frame.Json).ConfigureAwait(false);
					lastSent = frame.Seq;
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Server stopped or client disconnected — normal exit.
		}
		catch (IOException)
		{
			// Client disconnected mid-stream — normal exit; signal the connection CT.
			connectionCts.Cancel();
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "TdcpServer: SSE stream closed unexpectedly");
		}
		finally
		{
			if (_eventLog != null)
				_eventLog.Appended -= OnAppended;
			if (legacy)
			{
				_client.TreeChanged     -= OnTreeChanged;
				_client.ScheduleChanged -= OnScheduleChanged;
			}

			channel.Writer.TryComplete();
			try { ctx.Response.Close(); } catch { /* best effort */ }
		}
	}

	private EventReplay ResolveReplay(long lastEventId, string? clientRunId)
	{
		// A seq is only meaningful inside one run; a client that names its run gets a resync after a daemon restart.
		if (!string.IsNullOrEmpty(clientRunId) && _runInfo != null && clientRunId != _runInfo.RunId)
			return new EventReplay([], Constants.Remote.EventLog.ResyncReasons.UnknownSeq, _eventLog!.HeadSeq);

		return _eventLog!.ReadSince(lastEventId);
	}

	private static string NotificationJson(RemoteNotification notification) =>
		new JsonObject
		{
			["jsonrpc"] = "2.0",
			["method"]  = notification.Method,
			["params"]  = notification.Params.DeepClone(),
		}.ToJsonString();

	private static string ResyncJson(string reason, long head) =>
		new JsonObject
		{
			["jsonrpc"] = "2.0",
			["method"]  = Constants.Remote.Events.Resync,
			["params"]  = new JsonObject { ["reason"] = reason, ["eventSeq"] = head },
		}.ToJsonString();

	private static async Task WriteFrameAsync(StreamWriter writer, long id, string json)
	{
		if (id > 0)
			await writer.WriteAsync($"id: {id}\n").ConfigureAwait(false);
		await writer.WriteAsync($"data: {json}\n\n").ConfigureAwait(false);
		await writer.FlushAsync().ConfigureAwait(false);
	}

	// ── JSON-RPC wire helpers ─────────────────────────────────────────────────────

	private static async Task WriteJsonResponseAsync(HttpListenerContext ctx, string json, int statusCode)
	{
		var bytes                    = Encoding.UTF8.GetBytes(json);
		ctx.Response.StatusCode      = statusCode;
		ctx.Response.ContentType     = "application/json";
		ctx.Response.ContentLength64 = bytes.Length;
		await ctx.Response.OutputStream.WriteAsync(bytes);
		ctx.Response.Close();
	}
}
