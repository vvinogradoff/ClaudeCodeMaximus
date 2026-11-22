using System;
using System.Text.Json;

namespace TessynDesktop.Codex;

/// <summary>The app-server answered a request with a JSON-RPC error.</summary>
/// <remarks>Created by Claude</remarks>
public class CodexRpcException : Exception
{
	public string Method { get; }
	public int Code { get; }
	public JsonElement? ErrorData { get; }

	public CodexRpcException(string method, int code, string message, JsonElement? data = null, Exception? inner = null)
		: base(message, inner)
	{
		Method    = method;
		Code      = code;
		ErrorData = data;
	}
}
