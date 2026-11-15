using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TessynDesktop.Services;

/// <summary>
/// Downloads an uploaded blob from the web server over the relay connection (link token, same HttpClient as the link), for
/// <c>attachment.fetch</c>. Implemented by <see cref="RelayConnectorService"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IRelayBlobClient
{
	/// <summary>
	/// Streams the blob <paramref name="blobId"/> into <paramref name="destination"/>. Throws <see cref="TdcpException"/> with a clear
	/// message when the link is not connected, the blob is gone or the server refuses; the blob is one-time on the server.
	/// </summary>
	Task DownloadBlobAsync(string blobId, Stream destination, CancellationToken cancellationToken);
}
