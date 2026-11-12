using System.Text.Json;

namespace Tessyn.Web.Models;

/// <summary>Wire shape of a notification frame sent to browsers.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class EventNotification
{
	public string Method { get; set; } = string.Empty;
	public JsonElement? Params { get; set; }
}
