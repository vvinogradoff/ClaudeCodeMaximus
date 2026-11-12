using Tessyn.Web.Hosting;

namespace Tessyn.Web.Endpoints;

/// <summary>SPA fallback: non-API GETs get index.html, or a small built-in page when the SPA is not built.</summary>
/// <remarks>Created by Claude</remarks>
public static class SpaEndpoints
{
	public static void MapSpaFallback(this IEndpointRouteBuilder routes)
	{
		routes.MapFallback(ServeAsync);
	}

	private static async Task ServeAsync(HttpContext context, SpaFiles spa)
	{
		var request = context.Request;
		var path = request.Path;
		var isReadMethod = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);
		if (!isReadMethod
			|| path.StartsWithSegments(Constants.Paths.ApiPrefix)
			|| path.StartsWithSegments(Constants.Paths.LinkPrefix)
			|| path.StartsWithSegments(Constants.Paths.Health))
		{
			context.Response.StatusCode = StatusCodes.Status404NotFound;
			return;
		}

		context.Response.ContentType = Constants.ContentTypes.Html;
		context.Response.Headers.CacheControl = Constants.HeaderValues.NoCache;

		var index = spa.Provider.GetFileInfo(Constants.Paths.IndexFile);
		if (!index.Exists || index.IsDirectory)
		{
			await context.Response.WriteAsync(Constants.SpaFallback.NotBuiltHtml);
			return;
		}

		await using var stream = index.CreateReadStream();
		context.Response.ContentLength = index.Length;
		if (HttpMethods.IsHead(request.Method))
			return;

		await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
	}
}
