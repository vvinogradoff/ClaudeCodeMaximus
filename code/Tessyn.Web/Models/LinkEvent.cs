using System.Text.Json;

namespace Tessyn.Web.Models;

/// <remarks>Created by Claude</remarks>
public sealed class LinkEvent
{
	public string? Method { get; set; }
	public JsonElement? Params { get; set; }
}
