using Microsoft.Extensions.Options;
using Tessyn.Web.Hosting;
using Tessyn.Web.Link;
using Tessyn.Web.Options;

namespace Tessyn.Web.Endpoints;

/// <summary>GET /api/events: SSE stream of daemon notifications with Last-Event-ID resume.</summary>
/// <remarks>Created by Claude</remarks>
public static class EventsEndpoints
{
	public static void MapEventsEndpoints(this IEndpointRouteBuilder routes)
	{
		routes.MapGet(Constants.Paths.Events, StreamAsync).RequireAuthorization();
	}

	private static async Task StreamAsync(
		HttpContext context,
		DaemonLinkHub hub,
		SseWriter sse,
		IOptions<TessynWebOptions> options)
	{
		long? lastEventId = long.TryParse(context.Request.Headers[Constants.Headers.LastEventId].ToString(), out var parsed) ? parsed : null;
		var cancellation = context.RequestAborted;
		var subscription = hub.Subscribe(lastEventId);
		try
		{
			await sse.OpenAsync(context);
			foreach (var frame in subscription.Initial)
				await sse.WriteFrameAsync(context.Response, frame.Id, frame.Data, cancellation);

			await sse.PumpAsync(
				subscription.Live,
				frame => sse.WriteFrameAsync(context.Response, frame.Id, frame.Data, cancellation),
				() => sse.WritePingAsync(context.Response, cancellation),
				TimeSpan.FromSeconds(options.Value.Link.PingSeconds),
				cancellation);
		}
		catch (OperationCanceledException)
		{
			// Client went away.
		}
		catch (IOException)
		{
			// Connection dropped mid-write.
		}
		finally
		{
			hub.Unsubscribe(subscription);
		}
	}
}
