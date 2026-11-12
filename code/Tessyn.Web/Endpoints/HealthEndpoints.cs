using System.Net;
using Tessyn.Web.Link;
using Tessyn.Web.Models;

namespace Tessyn.Web.Endpoints;

/// <summary>GET /healthz (no auth): {ok} for everyone, plus linkOnline for loopback callers.</summary>
/// <remarks>Created by Claude</remarks>
public static class HealthEndpoints
{
	public static void MapHealthEndpoints(this IEndpointRouteBuilder routes)
	{
		routes.MapGet(Constants.Paths.Health, (HttpContext context, DaemonLinkHub hub) =>
		{
			// Whether the desktop is online is only disclosed to callers on the box itself (the public sees liveness only).
			var remote = context.Connection.RemoteIpAddress;
			return remote is not null && IPAddress.IsLoopback(remote)
				? Results.Json(new HealthResponse(true, hub.IsOnline))
				: Results.Json(new PublicHealthResponse(true));
		});
	}
}
