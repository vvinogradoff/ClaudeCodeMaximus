using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Hosts the Tessyn Daemon Control Protocol (TDCP) over Streamable HTTP (JSON-RPC 2.0).
/// Delegates every method to an injected <see cref="IDaemonClient"/>.
/// Suitable for in-process hosting behind AgentMcpServer's existing HttpListener and for
/// standalone self-hosting in tests via <see cref="Start"/>/<see cref="Stop"/>.
/// POST → single JSON-RPC response; GET → SSE push stream for tree.changed/schedule.changed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TdcpServer : IDisposable
{
	private static readonly ILogger _log = Log.ForContext<TdcpServer>();

	internal static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
		Converters                  = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
	};

	private readonly IDaemonClient           _client;
	private readonly string                  _token;
	private readonly CancellationTokenSource _cts = new();

	private HttpListener? _listener;

	public TdcpServer(IDaemonClient client, string token)
	{
		_client = client;
		_token  = token;
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
			await SendJsonRpcErrorAsync(ctx, null, -32600, "Invalid or missing X-CMX-Token");
			return;
		}

		using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
		var body = await reader.ReadToEndAsync();
		JsonNode? request;
		try { request = JsonNode.Parse(body); }
		catch
		{
			await SendJsonRpcErrorAsync(ctx, null, -32700, "Parse error");
			return;
		}

		var id      = request?["id"];
		var method  = request?["method"]?.GetValue<string>();
		var @params = request?["params"];

		JsonNode result;
		try
		{
			result = await DispatchAsync(method ?? string.Empty, @params);
		}
		catch (TdcpMethodNotFoundException ex)
		{
			await SendJsonRpcErrorAsync(ctx, id, -32601, $"Method not found: {ex.Message}");
			return;
		}
		catch (TdcpException ex)
		{
			await SendJsonRpcErrorAsync(ctx, id, -32000, ex.Message);
			return;
		}

		await SendJsonRpcResultAsync(ctx, id, result);
	}

	// ── Dispatch ──────────────────────────────────────────────────────────────────

	private async Task<JsonNode> DispatchAsync(string method, JsonNode? @params) =>
		method switch
		{
			"tree.list"            => TreeList(),
			"tree.getSelected"     => TreeGetSelected(),
			"tree.setSelected"     => TreeSetSelected(@params),
			"tree.findByNodeId"    => TreeFindByNodeId(@params),
			"tree.findByToken"     => TreeFindByToken(@params),
			"tree.location"        => TreeLocation(@params),
			"turn.isRunning"       => TurnIsRunning(@params),
			"turn.setRunning"      => TurnSetRunning(@params),
			"turn.run"             => await TurnRunAsync(@params),
			"turn.cancel"          => TurnCancel(@params),
			"turn.resolveProfile"  => TurnResolveProfile(@params),
			"schedule.list"        => ScheduleList(@params),
			"schedule.add"         => ScheduleAdd(@params),
			"schedule.remove"      => ScheduleRemove(@params),
			"content.read"         => ContentRead(@params),
			"content.exists"       => ContentExists(@params),
			"content.path"         => ContentPath(@params),
			_                      => throw new TdcpMethodNotFoundException(method),
		};

	// ── Tree methods ──────────────────────────────────────────────────────────────

	private JsonNode TreeList() =>
		Serialize<IReadOnlyList<DirectoryNodeModel>>(_client.Directories);

	private JsonNode TreeGetSelected() =>
		Serialize(new SelectedNodeIdResult(_client.SelectedNodeId));

	private JsonNode TreeSetSelected(JsonNode? @params)
	{
		_client.SelectedNodeId = @params?["nodeId"]?.GetValue<string>();
		return EmptyResult();
	}

	private JsonNode TreeFindByNodeId(JsonNode? @params)
	{
		var nodeId = RequireString(@params, "nodeId");
		var node   = _client.FindSessionByNodeId(nodeId);
		return node != null ? Serialize(node) : JsonValue.Create<object?>(null)!;
	}

	private JsonNode TreeFindByToken(JsonNode? @params)
	{
		var token = RequireString(@params, "token");
		var node  = _client.FindSessionByAgentToken(token);
		return node != null ? Serialize(node) : JsonValue.Create<object?>(null)!;
	}

	private JsonNode TreeLocation(JsonNode? @params)
	{
		var nodeId = RequireString(@params, "nodeId");
		var node   = _client.FindSessionByNodeId(nodeId)
		             ?? throw new TdcpException($"Session not found: {nodeId}");
		var (label, path) = _client.GetSessionLocation(node);
		return Serialize(new LocationResult(label, path));
	}

	// ── Running state methods ─────────────────────────────────────────────────────

	private JsonNode TurnIsRunning(JsonNode? @params)
	{
		var nodeId  = RequireString(@params, "nodeId");
		var running = _client.IsSessionRunning(nodeId);
		return Serialize(new IsRunningResult(running));
	}

	private JsonNode TurnSetRunning(JsonNode? @params)
	{
		var nodeId  = RequireString(@params, "nodeId");
		var running = @params?["running"]?.GetValue<bool>()
		              ?? throw new TdcpException("'running' is required");
		_client.SetSessionRunning(nodeId, running);
		return EmptyResult();
	}

	// ── Turn methods ──────────────────────────────────────────────────────────────

	private async Task<JsonNode> TurnRunAsync(JsonNode? @params)
	{
		var nodeId    = RequireString(@params, "nodeId");
		var prompt    = RequireString(@params, "prompt");
		var sourceStr = @params?["source"]?.GetValue<string>() ?? "user";
		if (!Enum.TryParse<TurnSource>(sourceStr, ignoreCase: true, out var source))
			source = TurnSource.User;

		var node = _client.FindSessionByNodeId(nodeId)
		           ?? throw new TdcpException($"Session not found: {nodeId}");

		var result = await _client.RunTurnAsync(node, prompt, source);
		return Serialize(result);
	}

	private JsonNode TurnCancel(JsonNode? @params)
	{
		var nodeId    = RequireString(@params, "nodeId");
		var cancelled = _client.CancelTurn(nodeId);
		return Serialize(new CancelledResult(cancelled));
	}

	private JsonNode TurnResolveProfile(JsonNode? @params)
	{
		var nodeId = RequireString(@params, "nodeId");
		var node   = _client.FindSessionByNodeId(nodeId)
		             ?? throw new TdcpException($"Session not found: {nodeId}");
		var name = _client.ResolveProfileName(node);
		return Serialize(new ProfileNameResult(name));
	}

	// ── Schedule methods ──────────────────────────────────────────────────────────

	private JsonNode ScheduleList(JsonNode? @params)
	{
		var targetNodeId = @params?["targetNodeId"]?.GetValue<string>();
		var schedules    = _client.GetSchedules(targetNodeId);
		return Serialize<IReadOnlyList<ScheduleModel>>(schedules);
	}

	private JsonNode ScheduleAdd(JsonNode? @params)
	{
		var json     = @params?.ToJsonString() ?? throw new TdcpException("'params' is required for schedule.add");
		var schedule = JsonSerializer.Deserialize<ScheduleModel>(json, JsonOpts)
		               ?? throw new TdcpException("Could not parse ScheduleModel from params");
		_client.AddSchedule(schedule);
		return EmptyResult();
	}

	private JsonNode ScheduleRemove(JsonNode? @params)
	{
		var scheduleId = RequireString(@params, "scheduleId");
		var removed    = _client.RemoveSchedule(scheduleId);
		return Serialize(new RemovedResult(removed));
	}

	// ── Content methods ───────────────────────────────────────────────────────────

	private JsonNode ContentRead(JsonNode? @params)
	{
		var fileName = RequireString(@params, "fileName");
		var entries  = _client.ReadEntries(fileName);
		return Serialize<IReadOnlyList<SessionEntryModel>>(entries);
	}

	private JsonNode ContentExists(JsonNode? @params)
	{
		var fileName = RequireString(@params, "fileName");
		var exists   = _client.SessionFileExists(fileName);
		return Serialize(new ExistsResult(exists));
	}

	private JsonNode ContentPath(JsonNode? @params)
	{
		var fileName = RequireString(@params, "fileName");
		var path     = _client.GetSessionFilePath(fileName);
		return Serialize(new FilePathResult(path));
	}

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
				["params"]  = JsonNode.Parse(JsonSerializer.Serialize(e, JsonOpts)),
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

	private static async Task SendJsonRpcResultAsync(HttpListenerContext ctx, JsonNode? id, JsonNode result)
	{
		var response = new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"]      = id?.DeepClone(),
			["result"]  = result,
		};
		await WriteJsonResponseAsync(ctx, response.ToJsonString(), 200);
	}

	private static async Task SendJsonRpcErrorAsync(HttpListenerContext ctx, JsonNode? id, int code, string message)
	{
		var response = new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"]      = id?.DeepClone(),
			["error"]   = new JsonObject { ["code"] = code, ["message"] = message },
		};
		await WriteJsonResponseAsync(ctx, response.ToJsonString(), 200);
	}

	private static async Task WriteJsonResponseAsync(HttpListenerContext ctx, string json, int statusCode)
	{
		var bytes                    = Encoding.UTF8.GetBytes(json);
		ctx.Response.StatusCode      = statusCode;
		ctx.Response.ContentType     = "application/json";
		ctx.Response.ContentLength64 = bytes.Length;
		await ctx.Response.OutputStream.WriteAsync(bytes);
		ctx.Response.Close();
	}

	private static JsonNode Serialize<T>(T value) =>
		JsonNode.Parse(JsonSerializer.Serialize(value, JsonOpts))!;

	private static JsonNode EmptyResult() => new JsonObject();

	private static string RequireString(JsonNode? @params, string key) =>
		@params?[key]?.GetValue<string>()
		?? throw new TdcpException($"'{key}' is required");

	// ── Private result records ────────────────────────────────────────────────────
	// These are private nested types only used within TdcpServer.

	private sealed record SelectedNodeIdResult(string? NodeId);
	private sealed record LocationResult(string DirectoryLabel, string TreePath);
	private sealed record IsRunningResult(bool Running);
	private sealed record CancelledResult(bool Cancelled);
	private sealed record ProfileNameResult(string? ProfileName);
	private sealed record RemovedResult(bool Removed);
	private sealed record ExistsResult(bool Exists);
	private sealed record FilePathResult(string Path);
}

file sealed class TdcpMethodNotFoundException(string method) : Exception(method);
file sealed class TdcpException(string message) : Exception(message);
