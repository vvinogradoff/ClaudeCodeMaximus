using System.Text.Json.Nodes;

namespace TessynDesktop.Services;

/// <summary>Typed access to string-array params; a missing, wrongly typed or non-string value throws <see cref="TdcpInvalidParamsException"/> (-32602).</summary>
/// <remarks>Created by Claude</remarks>
public static class TdcpListParams
{
	public static IReadOnlyList<string> RequireStrings(JsonNode? @params, string key) =>
		OptionalStrings(@params, key) ?? throw new TdcpInvalidParamsException($"'{key}' is required");

	public static IReadOnlyList<string>? OptionalStrings(JsonNode? @params, string key)
	{
		var node = TdcpParams.Find(@params, key);
		if (node == null)
			return null;

		if (node is not JsonArray array)
			throw new TdcpInvalidParamsException($"'{key}' must be an array of strings");

		var result = new List<string>(array.Count);
		foreach (var item in array)
		{
			if (item is not JsonValue value || !value.TryGetValue<string>(out var text))
				throw new TdcpInvalidParamsException($"'{key}' must be an array of strings");

			result.Add(text);
		}

		return result;
	}
}
