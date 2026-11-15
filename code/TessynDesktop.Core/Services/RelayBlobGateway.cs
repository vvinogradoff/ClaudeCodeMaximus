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

		HttpResponseMessage response;
		try
		{
			response = await link.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
		}
		catch (HttpRequestException ex)
		{
			throw new TdcpException("Could not reach the web server: " + ex.Message);
		}

		using (response)
		{
			if (response.StatusCode == HttpStatusCode.NotFound)
				throw new TdcpException("The upload is no longer available on the web server");
			if (response.StatusCode == HttpStatusCode.Unauthorized)
				throw new TdcpException(Constants.Remote.Link.TokenRejectedMessage);
			if (!response.IsSuccessStatusCode)
				throw new TdcpException($"The web server answered {(int)response.StatusCode} for the upload");

			await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
			await source.CopyToAsync(destination, Constants.Attachments.CopyBufferBytes, cancellationToken).ConfigureAwait(false);
		}
	}
}
