using System.Text.Json.Nodes;

namespace TessynDesktop.Services;

/// <summary>
/// Typed access to JSON-RPC params for method handlers. A missing or wrongly typed value throws
/// <see cref="TdcpInvalidParamsException"/>, which the dispatcher reports as -32602.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class TdcpParams
{
	public static string RequireString(JsonNode? @params, string key) =>
		OptionalString(@params, key) ?? throw new TdcpInvalidParamsException($"'{key}' is required");

	public static string? OptionalString(JsonNode? @params, string key)
	{
		var node = Find(@params, key);
		if (node == null)
			return null;

		if (node is JsonValue value && value.TryGetValue<string>(out var text))
			return text;

		throw new TdcpInvalidParamsException($"'{key}' must be a string");
	}

	public static bool RequireBool(JsonNode? @params, string key) =>
		OptionalBool(@params, key) ?? throw new TdcpInvalidParamsException($"'{key}' is required");

	public static bool? OptionalBool(JsonNode? @params, string key)
	{
		var node = Find(@params, key);
		if (node == null)
			return null;

		if (node is JsonValue value && value.TryGetValue<bool>(out var flag))
			return flag;

		throw new TdcpInvalidParamsException($"'{key}' must be a boolean");
	}

	public static int? OptionalInt(JsonNode? @params, string key)
	{
		var node = Find(@params, key);
		if (node == null)
			return null;

		if (node is JsonValue value && value.TryGetValue<int>(out var number))
			return number;

		throw new TdcpInvalidParamsException($"'{key}' must be an integer");
	}

	public static long? OptionalLong(JsonNode? @params, string key)
	{
		var node = Find(@params, key);
		if (node == null)
			return null;

		if (node is JsonValue value && value.TryGetValue<long>(out var number))
			return number;

		throw new TdcpInvalidParamsException($"'{key}' must be an integer");
	}

	/// <summary>The raw value of <paramref name="key"/>; null when absent or JSON null.</summary>
	public static JsonNode? Find(JsonNode? @params, string key) =>
		@params is JsonObject obj ? obj[key] : null;
}
