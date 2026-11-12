namespace Tessyn.Web.Hosting;

/// <summary>Adds the security headers to every response and no-store caching to API, link and health paths.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SecurityHeadersMiddleware
{
	private readonly RequestDelegate _next;

	public SecurityHeadersMiddleware(RequestDelegate next)
	{
		_next = next;
	}

	public Task InvokeAsync(HttpContext context)
	{
		var headers = context.Response.Headers;
		headers[Constants.Headers.ContentSecurityPolicy] = Constants.HeaderValues.ContentSecurityPolicy;
		headers[Constants.Headers.ContentTypeOptions] = Constants.HeaderValues.NoSniff;
		headers[Constants.Headers.ReferrerPolicy] = Constants.HeaderValues.NoReferrer;
		headers[Constants.Headers.FrameOptions] = Constants.HeaderValues.FrameDeny;

		var path = context.Request.Path;
		if (path.StartsWithSegments(Constants.Paths.ApiPrefix)
			|| path.StartsWithSegments(Constants.Paths.LinkPrefix)
			|| path.StartsWithSegments(Constants.Paths.Health))
		{
			headers.CacheControl = Constants.HeaderValues.NoStore;
		}

		return _next(context);
	}
}
