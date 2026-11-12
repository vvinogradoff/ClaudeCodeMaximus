namespace Tessyn.Web.Link;

/// <summary>Raw JSON of either a JSON-RPC result or a JSON-RPC error object (exactly one is set).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record RpcOutcome(string? ResultJson, string? ErrorJson)
{
	public bool IsError => ErrorJson is not null;
}
