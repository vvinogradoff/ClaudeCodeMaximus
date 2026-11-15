using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TessynDesktop.Services;

/// <summary>
/// Holds the live relay connection (HttpClient with the link token, and the server's base URL) so <c>attachment.fetch</c> can download
/// an uploaded blob with the same auth. <see cref="RelayConnectorService"/> attaches on connect and detaches when the connection ends.
/// A separate object keeps the dispatcher out of a dependency cycle (dispatcher, attachment module, blob client, connector, dispatcher).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RelayBlobGateway : IRelayBlobClient
{
	private sealed record Link(HttpClient Client, string BaseUrl);

	private volatile Link? _link;

	/// <summary>Called by the connector when the link stream is up.</summary>
	public void Attach(HttpClient client, string baseUrl) => _link = new Link(client, baseUrl);

	/// <summary>Called by the connector when the connection ends; a newer connection's attachment is left alone.</summary>
	public void Detach(HttpClient client)
	{
		var current = _link;
		if (current != null && ReferenceEquals(current.Client, client))
			Interlocked.CompareExchange(ref _link, null, current);
	}

	/// <inheritdoc />
	public async Task DownloadBlobAsync(string blobId, Stream destination, CancellationToken cancellationToken)
	{
		var link = _link ?? throw new TdcpException(Constants.Attachments.NotConnectedMessage);
		var url  = link.BaseUrl + Constants.Remote.Link.BlobPath + Uri.EscapeDataString(blobId);

		const string Unreachable = "Could not reach the web server: ";
		try
		{
			using var response = await link.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
			if (response.StatusCode == HttpStatusCode.NotFound)
				throw new TdcpException("The upload is no longer available on the web server");
			if (response.StatusCode == HttpStatusCode.Unauthorized)
				throw new TdcpException(Constants.Remote.Link.TokenRejectedMessage);
			if (!response.IsSuccessStatusCode)
				throw new TdcpException($"The web server answered {(int)response.StatusCode} for the upload");

			await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
			var buffer = new byte[Constants.Attachments.CopyBufferBytes];
			while (true)
			{
				var read = await ReadAsync(source, buffer, cancellationToken, Unreachable).ConfigureAwait(false);
				if (read == 0)
					break;

				// A failed write is the daemon's own (disk), not the link's, so it is not mapped to "could not reach".
				await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
			}
		}
		catch (Exception ex) when (ex is HttpRequestException or ObjectDisposedException)
		{
			// A refused connection, or a client disposed by a reconnect.
			throw new TdcpException(Unreachable + ex.Message);
		}
	}

	/// <summary>Reads from the link; a drop in the middle of the body is the link's failure.</summary>
	private static async Task<int> ReadAsync(Stream source, byte[] buffer, CancellationToken cancellationToken, string message)
	{
		try
		{
			return await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or HttpRequestException or ObjectDisposedException)
		{
			throw new TdcpException(message + ex.Message);
		}
	}
}
