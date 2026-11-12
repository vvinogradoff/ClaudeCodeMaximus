using Tessyn.Web.Link;
using Tessyn.Web.Models;

namespace Tessyn.Web.Endpoints;

/// <summary>GET /healthz (no auth).</summary>
/// <remarks>Created by Claude</remarks>
public static class HealthEndpoints
{
	public static void MapHealthEndpoints(this IEndpointRouteBuilder routes)
	{
		routes.MapGet(Constants.Paths.Health, (DaemonLinkHub hub) => Results.Json(new HealthResponse(true, hub.IsOnline)));
	}
}
