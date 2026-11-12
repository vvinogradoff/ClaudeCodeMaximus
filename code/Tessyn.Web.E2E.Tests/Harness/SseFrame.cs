using System.Text.Json.Nodes;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>One SSE frame of <c>/api/events</c>: <c>{"method","params"}</c> plus the server sequence id.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SseFrame(string? Id, string? Data, bool IsPing)
{
	public string Method => Json["method"]!.GetValue<string>();
	public JsonNode Params => Json["params"]!;
	private JsonNode Json => JsonNode.Parse(Data!)!;
}
