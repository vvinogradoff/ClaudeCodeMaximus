using System.Text.Json;
using System.Text.Json.Serialization;

namespace TessynDesktop.Services;

/// <summary>
/// The one set of JSON options for every TDCP/relay wire payload (contract section 2): camelCase properties,
/// case-insensitive reads, nulls omitted, enums as camelCase strings.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class TdcpJsonOptions
{
	public static readonly JsonSerializerOptions Default = new()
	{
		PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
		Converters                  = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
	};
}
