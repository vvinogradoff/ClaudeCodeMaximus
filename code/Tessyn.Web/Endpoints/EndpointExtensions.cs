namespace Tessyn.Web.Endpoints;

/// <summary>Maps every Tessyn.Web endpoint group.</summary>
/// <remarks>Created by Claude</remarks>
public static class EndpointExtensions
{
	public static WebApplication MapTessynEndpoints(this WebApplication app)
	{
		app.MapHealthEndpoints();
		app.MapAuthEndpoints();
		app.MapRpcEndpoints();
		app.MapEventsEndpoints();
		app.MapLinkEndpoints();
		app.MapAttachmentEndpoints();
		app.MapSpaFallback();
		return app;
	}
}
