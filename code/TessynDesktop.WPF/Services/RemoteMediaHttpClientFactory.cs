using System.Net;
using System.Net.Sockets;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Builds the <see cref="HttpClient"/> of <see cref="RemoteMediaFetcher"/>: no redirects (the fetcher re-checks every hop itself), no cookies,
/// no credentials, and a connect callback that resolves the host name itself and refuses non-public addresses, so a public-looking name that
/// resolves to the local network is not reached either.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RemoteMediaHttpClientFactory
{
	private readonly PrivateAddressCheck _addresses;

	public RemoteMediaHttpClientFactory(PrivateAddressCheck addresses)
	{
		_addresses = addresses;
	}

	public HttpClient Create()
	{
		var handler = new SocketsHttpHandler
		{
			AllowAutoRedirect = false,
			UseCookies = false,
			UseProxy = true,
			Credentials = null,
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
			ConnectTimeout = Constants.Timeouts.RemoteConnect,
			ConnectCallback = ConnectPublicAsync,
		};
		return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
	}

	private async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
	{
		var addresses = (await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken)).Where(_addresses.IsPublic).ToArray();
		if (addresses.Length == 0)
			throw new HttpRequestException("The host does not resolve to a public address.");

		var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
		try
		{
			await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
			return new NetworkStream(socket, ownsSocket: true);
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}
}
