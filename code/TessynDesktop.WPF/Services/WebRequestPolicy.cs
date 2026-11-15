namespace TessynDesktop.WPF.Services;

/// <summary>
/// Which requests the WebView2 pages may make. Neither page ever reaches a real remote host: the transcript page gets its own two virtual
/// hosts, its media host (answered by the media proxy) and the preview cache (a local folder); the preview page gets only its page and the
/// preview cache. Everything else, including every real remote address, is blocked (review V2).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class WebRequestPolicy
{
	private readonly MediaProxyUrl _proxy;

	public WebRequestPolicy(MediaProxyUrl proxy)
	{
		_proxy = proxy;
	}

	/// <summary>Decision for a request of the transcript page.</summary>
	public RequestDecision ForTranscript(Uri request)
	{
		if (request.Scheme != Uri.UriSchemeHttps)
			return RequestDecision.Block;

		if (IsHost(request, Constants.Hosts.AppHost) || IsHost(request, Constants.Hosts.PreviewHost))
			return RequestDecision.Allow;

		return IsHost(request, Constants.Hosts.MediaHost) && _proxy.TryParse(request, out _) ? RequestDecision.ServeMedia : RequestDecision.Block;
	}

	/// <summary>Decision for a request of the preview page.</summary>
	public RequestDecision ForPreview(Uri request) =>
		request.Scheme == Uri.UriSchemeHttps && (IsHost(request, Constants.Hosts.AppHost) || IsHost(request, Constants.Hosts.PreviewHost)) ? RequestDecision.Allow : RequestDecision.Block;

	private static bool IsHost(Uri request, string host) => string.Equals(request.Host, host, StringComparison.OrdinalIgnoreCase);
}
