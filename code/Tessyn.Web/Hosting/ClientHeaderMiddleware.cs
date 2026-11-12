using Tessyn.Web.Link;
using Tessyn.Web.Models;

namespace Tessyn.Web.Hosting;

/// <summary>CSRF defence: every state-changing request under /api must carry X-Tessyn-Client: web.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClientHeaderMiddleware
{
	private readonly RequestDelegate _next;
	private readonly WireJson _json;

	public ClientHeaderMiddleware(RequestDelegate next, WireJson json)
	{
		_next = next;
		_json = json;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		var request = context.Request;
		var unsafeMethod = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method);
		if (unsafeMethod && request.Path.StartsWithSegments(Constants.Paths.ApiPrefix))
		{
			var value = request.Headers[Constants.Headers.Client].ToString();
			if (!string.Equals(value, Constants.Headers.ClientValue, StringComparison.OrdinalIgnoreCase))
			{
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				context.Response.ContentType = Constants.ContentTypes.Json;
				await context.Response.WriteAsync(_json.Serialize(new ErrorResponse(Constants.Auth.MissingClientHeaderMessage)));
				return;
			}
		}

		await _next(context);
	}
}
