namespace TessynDesktop.WPF.Services;

/// <summary>
/// The address form the transcript page uses for a remote image: <c>https://media.local/i?u=&lt;percent-encoded remote URL&gt;</c>. The name is
/// virtual; the host answers it itself (<see cref="TranscriptMediaBroker"/>), so the page never resolves or contacts a remote host.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class MediaProxyUrl
{
	private const string Path = "/i";
	private const string QueryPrefix = "?u=";

	/// <summary>The proxy address of <paramref name="remote"/>.</summary>
	public string Build(string remote) => $"https://{Constants.Hosts.MediaHost}{Path}{QueryPrefix}{Uri.EscapeDataString(remote)}";

	/// <summary>True for a proxy request; <paramref name="remote"/> is the decoded remote URL text.</summary>
	public bool TryParse(Uri request, out string remote)
	{
		remote = string.Empty;
		if (request.Scheme != Uri.UriSchemeHttps || !string.Equals(request.Host, Constants.Hosts.MediaHost, StringComparison.OrdinalIgnoreCase)
			|| request.AbsolutePath != Path || !request.Query.StartsWith(QueryPrefix, StringComparison.Ordinal) || request.Query.Contains('&'))
			return false;

		remote = Uri.UnescapeDataString(request.Query[QueryPrefix.Length..]);
		return remote.Length > 0;
	}
}
