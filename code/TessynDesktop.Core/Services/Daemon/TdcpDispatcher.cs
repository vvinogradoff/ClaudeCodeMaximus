using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Transport-neutral JSON-RPC 2.0 dispatcher shared by the loopback /tdcp endpoint and the relay link.
/// Builds its method table from every registered <see cref="IRemoteRpcModule"/> and turns every input — including
/// malformed ones and any exception a handler throws — into a complete JSON-RPC response.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TdcpDispatcher
{
	private const int ParseError = -32700;
	private const int InvalidRequest = -32600;
	private const int MethodNotFound = -32601;
	private const int InvalidParams = -32602;
	private const int DaemonError = -32000;
	private const int InternalError = -32603;

	private const string InternalErrorMessage = "Internal error";
	private const string InvalidParamsMessage = "Invalid params";

	private static readonly ILogger _log = Log.ForContext<TdcpDispatcher>();

	private readonly TdcpMethodRegistry _registry = new();

	public TdcpDispatcher(IEnumerable<IRemoteRpcModule> modules)
	{
		foreach (var module in modules)
		{
			try
			{
				module.Register(_registry);
			}
			catch (Exception ex)
			{
				// A broken module must not stop the app from starting; its methods are simply missing.
				_log.Error(ex, "TdcpDispatcher: module {Module} failed to register its methods", module.GetType().Name);
			}
		}
	}

	/// <summary>True when <paramref name="method"/> exists and may be called over the relay.</summary>
	public bool IsRemoteCallable(string method) => _registry.TryGet(method, out var entry) && entry.RemoteCallable;

	/// <summary>Names of the methods the relay may call.</summary>
	public IReadOnlyList<string> RemoteMethodNames
	{
		get
		{
			var names = new List<string>();
			foreach (var entry in _registry.Entries)
			{
				if (entry.RemoteCallable)
					names.Add(entry.Name);
			}
			return names;
		}
	}

	/// <summary>Parses <paramref name="body"/> as one JSON-RPC request and handles it; unparseable text yields -32700.</summary>
	public Task<JsonNode> HandleTextAsync(string body, TdcpTransport transport, CancellationToken ct)
	{
		JsonNode? request;
		try
		{
			request = JsonNode.Parse(body);
		}
		catch (JsonException)
		{
			return Task.FromResult(Error(null, ParseError, "Parse error"));
		}

		return HandleRequestAsync(request!, transport, ct);
	}

	/// <summary>Handles one parsed JSON-RPC request. Never throws; always returns a full response object.</summary>
	public async Task<JsonNode> HandleRequestAsync(JsonNode request, TdcpTransport transport, CancellationToken ct)
	{
		JsonNode? id = null;
		try
		{
			if (request is not JsonObject obj)
				return Error(null, InvalidRequest, "Invalid request");

			id = obj["id"];
			if (id is not null && id is not JsonValue)
				return Error(null, InvalidRequest, "Invalid request");

			if (obj["method"] is not JsonValue methodValue
			    || !methodValue.TryGetValue<string>(out var method)
			    || string.IsNullOrEmpty(method))
				return Error(id, InvalidRequest, "Invalid request");

			var @params = obj["params"];
			if (@params is not null && @params is not JsonObject)
				return Error(id, InvalidParams, InvalidParamsMessage);

			if (!_registry.TryGet(method, out var entry) || (transport == TdcpTransport.Relay && !entry.RemoteCallable))
				throw new TdcpMethodNotFoundException(method);

			var result = await entry.Handler(@params, ct).ConfigureAwait(false);
			return Result(id, Serialize(result));
		}
		catch (TdcpMethodNotFoundException ex)
		{
			return Error(id, MethodNotFound, $"Method not found: {ex.Message}");
		}
		catch (TdcpInvalidParamsException ex)
		{
			return Error(id, InvalidParams, ex.Message);
		}
		catch (TdcpException ex)
		{
			return Error(id, DaemonError, ex.Message);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "TdcpDispatcher: unhandled error in a method handler");
			return Error(id, InternalError, InternalErrorMessage);
		}
	}

	private static JsonNode? Serialize(object? result) =>
		result == null ? null : JsonSerializer.SerializeToNode(result, result.GetType(), TdcpJsonOptions.Default);

	private static JsonNode Result(JsonNode? id, JsonNode? result) =>
		new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"]      = id?.DeepClone(),
			["result"]  = result,
		};

	private static JsonNode Error(JsonNode? id, int code, string message) =>
		new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"]      = id?.DeepClone(),
			["error"]   = new JsonObject { ["code"] = code, ["message"] = message },
		};
}
