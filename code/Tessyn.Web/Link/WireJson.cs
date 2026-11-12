using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tessyn.Web.Link;

/// <summary>Shared JSON conventions (contract section 2) plus builders for the JSON-RPC frames the server makes.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WireJson
{
	public WireJson()
	{
		Options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			PropertyNameCaseInsensitive = true,
		};
		Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
	}

	public JsonSerializerOptions Options { get; }

	public string Serialize<T>(T value)
	{
		return JsonSerializer.Serialize(value, Options);
	}

	public RpcOutcome ErrorOutcome(int code, string message)
	{
		return new RpcOutcome(null, Serialize(new Models.JsonRpcErrorBody(code, message)));
	}

	public string BuildRequestFrame(string id, string method, JsonElement? paramsElement)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			writer.WriteString("jsonrpc", Constants.JsonRpc.Version);
			writer.WriteString("id", id);
			writer.WriteString("method", method);
			if (paramsElement is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } p)
			{
				writer.WritePropertyName("params");
				p.WriteTo(writer);
			}

			writer.WriteEndObject();
		}

		return System.Text.Encoding.UTF8.GetString(stream.ToArray());
	}

	public string BuildNotificationFrame(string method, string? paramsJson)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			writer.WriteString("method", method);
			if (paramsJson is not null)
			{
				writer.WritePropertyName("params");
				writer.WriteRawValue(paramsJson);
			}

			writer.WriteEndObject();
		}

		return System.Text.Encoding.UTF8.GetString(stream.ToArray());
	}

	public string BuildResponseFrame(JsonElement id, RpcOutcome outcome)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			writer.WriteString("jsonrpc", Constants.JsonRpc.Version);
			writer.WritePropertyName("id");
			id.WriteTo(writer);
			if (outcome.IsError)
			{
				writer.WritePropertyName("error");
				writer.WriteRawValue(outcome.ErrorJson!);
			}
			else
			{
				writer.WritePropertyName("result");
				writer.WriteRawValue(outcome.ResultJson ?? "null");
			}

			writer.WriteEndObject();
		}

		return System.Text.Encoding.UTF8.GetString(stream.ToArray());
	}

	public string BuildNullIdErrorFrame(int code, string message)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			writer.WriteString("jsonrpc", Constants.JsonRpc.Version);
			writer.WriteNull("id");
			writer.WritePropertyName("error");
			writer.WriteRawValue(Serialize(new Models.JsonRpcErrorBody(code, message)));
			writer.WriteEndObject();
		}

		return System.Text.Encoding.UTF8.GetString(stream.ToArray());
	}
}
