using System.Text;
using System.Threading.Channels;

namespace Tessyn.Web.Hosting;

/// <summary>Writes Server-Sent Events frames, flushing after every frame so nothing sits in a buffer.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SseWriter
{
	private static readonly byte[] PingBytes = Encoding.UTF8.GetBytes(": ping\n\n");

	public async Task OpenAsync(HttpContext context)
	{
		var response = context.Response;
		response.StatusCode = StatusCodes.Status200OK;
		response.ContentType = Constants.ContentTypes.EventStream;
		response.Headers.CacheControl = Constants.HeaderValues.NoStore;
		response.Headers[Constants.Headers.AccelBuffering] = Constants.HeaderValues.AccelBufferingOff;
		context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
		await response.StartAsync(context.RequestAborted).ConfigureAwait(false);
		await response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
	}

	public async Task WriteFrameAsync(HttpResponse response, long? id, string data, CancellationToken cancellationToken)
	{
		var text = id.HasValue ? $"id: {id.Value}\ndata: {data}\n\n" : $"data: {data}\n\n";
		await response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken).ConfigureAwait(false);
		await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task WritePingAsync(HttpResponse response, CancellationToken cancellationToken)
	{
		await response.Body.WriteAsync(PingBytes, cancellationToken).ConfigureAwait(false);
		await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Writes every item from the channel; sends a ping whenever the channel is idle for the interval.</summary>
	public async Task PumpAsync<T>(
		ChannelReader<T> reader,
		Func<T, Task> writeItem,
		Func<Task> writePing,
		TimeSpan pingInterval,
		CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			idle.CancelAfter(pingInterval);

			bool more;
			try
			{
				more = await reader.WaitToReadAsync(idle.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				await writePing().ConfigureAwait(false);
				continue;
			}

			if (!more)
				return;

			while (reader.TryRead(out var item))
				await writeItem(item).ConfigureAwait(false);
		}
	}
}
