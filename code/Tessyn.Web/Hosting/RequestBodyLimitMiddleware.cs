using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Tessyn.Web.Link;
using Tessyn.Web.Models;
using Tessyn.Web.Options;

namespace Tessyn.Web.Hosting;

/// <summary>Per-endpoint request body limits: tiny for login, modest for rpc, larger for the daemon link.</summary>
/// <remarks>Kestrel enforces the limit while reading (also for chunked bodies); a declared Content-Length over the limit is refused up front. Created by Claude</remarks>
public sealed class RequestBodyLimitMiddleware
{
	private readonly RequestDelegate _next;
	private readonly WireJson _json;
	private readonly long _uploadLimit;

	public RequestBodyLimitMiddleware(RequestDelegate next, WireJson json, IOptions<TessynWebOptions> options)
	{
		_next = next;
		_json = json;
		_uploadLimit = options.Value.Attachments.MaxUploadBytes + Constants.Limits.MultipartOverheadBytes;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		var limit = LimitFor(context.Request.Path, _uploadLimit);
		var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
		if (feature is { IsReadOnly: false })
			feature.MaxRequestBodySize = limit;

		if (context.Request.ContentLength > limit)
		{
			context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
			context.Response.ContentType = Constants.ContentTypes.Json;
			await context.Response.WriteAsync(_json.Serialize(new ErrorResponse(Constants.Auth.PayloadTooLargeMessage)));
			return;
		}

		await _next(context);
	}

	public static long LimitFor(PathString path) => LimitFor(path, Constants.Limits.MaxUploadBytes + Constants.Limits.MultipartOverheadBytes);

	public static long LimitFor(PathString path, long uploadLimit)
	{
		if (path.Equals(Constants.Paths.Attachments, StringComparison.OrdinalIgnoreCase))
			return uploadLimit;
		if (path.Equals(Constants.Paths.Login, StringComparison.OrdinalIgnoreCase))
			return Constants.Limits.LoginBodyBytes;
		if (path.Equals(Constants.Paths.Rpc, StringComparison.OrdinalIgnoreCase))
			return Constants.Limits.RpcBodyBytes;
		if (path.Equals(Constants.Paths.LinkReply, StringComparison.OrdinalIgnoreCase)
			|| path.Equals(Constants.Paths.LinkEvents, StringComparison.OrdinalIgnoreCase))
			return Constants.Limits.LinkBodyBytes;
		return Constants.Limits.MaxBodyBytes;
	}
}
