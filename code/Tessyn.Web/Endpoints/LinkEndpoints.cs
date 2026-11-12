using System.Text.Json;
using Microsoft.Extensions.Options;
using Tessyn.Web.Auth;
using Tessyn.Web.Hosting;
using Tessyn.Web.Link;
using Tessyn.Web.Models;
using Tessyn.Web.Options;

namespace Tessyn.Web.Endpoints;

/// <summary>The daemon-facing relay link: /link/stream (requests down), /link/reply and /link/events (up).</summary>
/// <remarks>Created by Claude</remarks>
public static class LinkEndpoints
{
	public static void MapLinkEndpoints(this IEndpointRouteBuilder routes)
	{
		routes.MapGet(Constants.Paths.LinkStream, StreamAsync);
		routes.MapPost(Constants.Paths.LinkReply, (Delegate)ReplyAsync);
		routes.MapPost(Constants.Paths.LinkEvents, (Delegate)EventsAsync);
	}

	private static async Task StreamAsync(
		HttpContext context,
		LinkTokenValidator tokens,
		DaemonLinkHub hub,
		SseWriter sse,
		IOptions<TessynWebOptions> options)
	{
		if (!tokens.IsAuthorized(context.Request))
		{
			context.Response.StatusCode = StatusCodes.Status401Unauthorized;
			return;
		}

		var protocol = context.Request.Headers[Constants.Headers.Protocol].ToString();
		if (protocol.Length > 0 && protocol != Constants.HeaderValues.ProtocolVersion)
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}

		var cancellation = context.RequestAborted;
		var link = hub.Connect(context.Request.Headers[Constants.Headers.Daemon].ToString());
		try
		{
			await sse.OpenAsync(context);
			await sse.PumpAsync(
				link.Frames,
				frame => sse.WriteFrameAsync(context.Response, null, frame, cancellation),
				() => sse.WritePingAsync(context.Response, cancellation),
				TimeSpan.FromSeconds(options.Value.Link.PingSeconds),
				cancellation);
		}
		catch (OperationCanceledException)
		{
			// Daemon went away.
		}
		catch (IOException)
		{
			// Connection dropped mid-write.
		}
		finally
		{
			hub.Disconnect(link);
		}
	}

	private static async Task<IResult> ReplyAsync(HttpContext context, LinkTokenValidator tokens, DaemonLinkHub hub)
	{
		if (!tokens.IsAuthorized(context.Request))
			return Results.Unauthorized();

		JsonDocument document;
		try
		{
			document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
		}
		catch (JsonException)
		{
			return Results.BadRequest();
		}

		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Array)
				return Results.BadRequest();

			foreach (var response in document.RootElement.EnumerateArray())
			{
				if (response.ValueKind != JsonValueKind.Object
					|| !response.TryGetProperty("id", out var id)
					|| id.ValueKind != JsonValueKind.String)
					continue;

				RpcOutcome outcome;
				if (response.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
					outcome = new RpcOutcome(null, error.GetRawText());
				else if (response.TryGetProperty("result", out var result))
					outcome = new RpcOutcome(result.GetRawText(), null);
				else
					outcome = new RpcOutcome("null", null);

				hub.CompleteReply(id.GetString()!, outcome);
			}
		}

		return Results.NoContent();
	}

	private static async Task<IResult> EventsAsync(HttpContext context, LinkTokenValidator tokens, DaemonLinkHub hub, WireJson json)
	{
		if (!tokens.IsAuthorized(context.Request))
			return Results.Unauthorized();

		LinkEventBatch? batch;
		try
		{
			batch = await context.Request.ReadFromJsonAsync<LinkEventBatch>(json.Options, context.RequestAborted);
		}
		catch (JsonException)
		{
			return Results.BadRequest();
		}

		foreach (var item in batch?.Events ?? [])
		{
			var method = item.Method;
			if (string.IsNullOrWhiteSpace(method)
				|| method == Constants.Notifications.LinkStatus
				|| method == Constants.Notifications.Resync)
				continue;

			hub.Publish(method, item.Params);
		}

		return Results.NoContent();
	}
}
