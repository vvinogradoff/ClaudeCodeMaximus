using System.Text.Json;
using Tessyn.Web.Link;

namespace Tessyn.Web.Endpoints;

/// <summary>POST /api/rpc: validates one JSON-RPC request from the browser and relays it to the daemon.</summary>
/// <remarks>Created by Claude</remarks>
public static class RpcEndpoints
{
	public static void MapRpcEndpoints(this IEndpointRouteBuilder routes)
	{
		routes.MapPost(Constants.Paths.Rpc, (Delegate)RelayAsync).RequireAuthorization();
	}

	private static async Task<IResult> RelayAsync(
		HttpContext context,
		DaemonLinkHub hub,
		RemoteMethodAllowlist allowlist,
		WireJson json)
	{
		JsonDocument document;
		try
		{
			document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
		}
		catch (JsonException)
		{
			return Reply(json.BuildNullIdErrorFrame(Constants.JsonRpc.ParseError, Constants.JsonRpc.ParseErrorMessage));
		}

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
				return Reply(json.BuildNullIdErrorFrame(Constants.JsonRpc.InvalidRequest, Constants.JsonRpc.InvalidRequestMessage));

			if (!root.TryGetProperty("id", out var idElement)
				|| idElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
				return Reply(json.BuildNullIdErrorFrame(Constants.JsonRpc.InvalidRequest, Constants.JsonRpc.InvalidRequestMessage));

			var id = idElement.Clone();

			var versionOk = !root.TryGetProperty("jsonrpc", out var version)
				|| (version.ValueKind == JsonValueKind.String && version.GetString() == Constants.JsonRpc.Version);
			if (!versionOk
				|| !root.TryGetProperty("method", out var methodElement)
				|| methodElement.ValueKind != JsonValueKind.String)
				return Reply(json.BuildResponseFrame(id, json.ErrorOutcome(Constants.JsonRpc.InvalidRequest, Constants.JsonRpc.InvalidRequestMessage)));

			var method = methodElement.GetString()!;
			if (!allowlist.IsAllowed(method))
				return Reply(json.BuildResponseFrame(id, json.ErrorOutcome(Constants.JsonRpc.MethodNotFound, Constants.JsonRpc.MethodNotFoundMessage)));

			JsonElement? paramsElement = null;
			if (root.TryGetProperty("params", out var p) && p.ValueKind != JsonValueKind.Null)
			{
				if (p.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
					return Reply(json.BuildResponseFrame(id, json.ErrorOutcome(Constants.JsonRpc.InvalidParams, Constants.JsonRpc.InvalidParamsMessage)));

				paramsElement = p;
			}

			var outcome = await hub.SendAsync(method, paramsElement, context.RequestAborted);
			return Reply(json.BuildResponseFrame(id, outcome));
		}
	}

	private static IResult Reply(string body)
	{
		return Results.Text(body, Constants.ContentTypes.Json);
	}
}
