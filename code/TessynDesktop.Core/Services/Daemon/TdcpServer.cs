using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Hosts the Tessyn Daemon Control Protocol (TDCP) over Streamable HTTP (JSON-RPC 2.0).
/// Handles HTTP and token auth only; every JSON-RPC request is handed to the shared <see cref="TdcpDispatcher"/>
/// (loopback transport). The SSE stream is fed from the injected <see cref="IDaemonClient"/> events.
/// Suitable for in-process hosting behind AgentMcpServer's existing HttpListener and for
/// standalone self-hosting in tests via <see cref="Start"/>/<see cref="Stop"/>.
/// POST → single JSON-RPC response; GET → SSE push stream for tree.changed/schedule.changed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TdcpServer : IDisposable
{
	private static readonly ILogger _log = Log.ForContext<TdcpServer>();

	private readonly IDaemonClient           _client;
	private readonly TdcpDispatcher          _dispatcher;
	private readonly string                  _token;
	private readonly CancellationTokenSource _cts = new();

	private HttpListener? _listener;

	public TdcpServer(IDaemonClient client, TdcpDispatcher dispatcher, string token)
	{
		_client     = client;
		_dispatcher = dispatcher;
		_token      = token;
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
		var token = ctx.Request.Headers[Constants.Agent.TokenHeader];
		if (string.IsNullOrEmpty(token) || token != _token)
		{
			await WriteJsonResponseAsync(ctx, InvalidTokenResponse().ToJsonString(), 200);
			return;
		}

		using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
		var body = await reader.ReadToEndAsync();

		var response = await _dispatcher.HandleTextAsync(body, TdcpTransport.Loopback, CancellationToken.None);
		await WriteJsonResponseAsync(ctx, response.ToJsonString(), 200);
	}

	private static JsonNode InvalidTokenResponse() =>
		new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"]      = null,
			["error"]   = new JsonObject { ["code"] = -32600, ["message"] = "Invalid or missing X-CMX-Token" },
		};

	// ── GET SSE handler ───────────────────────────────────────────────────────────

	private async Task HandleGetSseAsync(HttpListenerContext ctx)
	{
		var token = ctx.Request.Headers[Constants.Agent.TokenHeader];
		if (string.IsNullOrEmpty(token) || token != _token)
		{
			ctx.Response.StatusCode = 401;
			ctx.Response.Close();
			return;
		}

		ctx.Response.StatusCode  = 200;
		ctx.Response.ContentType = "text/event-stream";
		ctx.Response.Headers.Add("Cache-Control", "no-cache");
		ctx.Response.Headers.Add("Connection",    "keep-alive");

		// Bounded channel buffers events while writing; DropOldest prevents unbounded growth.
		var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
		});

		// Use a per-connection CT linked to the server-lifetime CT so either disconnect or
		// server Stop() terminates the stream cleanly.
		using var connectionCts = new CancellationTokenSource();
		using var linkedCts     = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, connectionCts.Token);
		var ct = linkedCts.Token;

		void OnTreeChanged(object? sender, TreeChangedEventArgs e)
		{
			var notification = new JsonObject
			{
				["jsonrpc"] = "2.0",
				["method"]  = "tree.changed",
				["params"]  = JsonNode.Parse(JsonSerializer.Serialize(e, TdcpJsonOptions.Default)),
			};
			channel.Writer.TryWrite(notification.ToJsonString());
		}

		void OnScheduleChanged(object? sender, EventArgs e)
		{
			var notification = new JsonObject
			{
				["jsonrpc"] = "2.0",
				["method"]  = "schedule.changed",
				["params"]  = new JsonObject(),
			};
			channel.Writer.TryWrite(notification.ToJsonString());
		}

		_client.TreeChanged     += OnTreeChanged;
		_client.ScheduleChanged += OnScheduleChanged;

		try
		{
			var writer = new StreamWriter(ctx.Response.OutputStream, Encoding.UTF8, leaveOpen: true);
			await using (writer.ConfigureAwait(false))
			{
				await foreach (var json in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
				{
					await writer.WriteAsync($"data: {json}\n\n").ConfigureAwait(false);
					await writer.FlushAsync().ConfigureAwait(false);
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
			_client.TreeChanged     -= OnTreeChanged;
			_client.ScheduleChanged -= OnScheduleChanged;
			channel.Writer.TryComplete();
			try { ctx.Response.Close(); } catch { /* best effort */ }
		}
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
