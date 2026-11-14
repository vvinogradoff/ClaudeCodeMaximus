namespace TessynDesktop.WPF.Services;

/// <summary>
/// The only place that decides which URLs may leave the app: absolute http and https only. Transcript text is untrusted,
/// so every link click, navigation and new-window request is checked here before the system browser opens it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class UrlPolicy
{
	/// <summary>True for an absolute http or https URL.</summary>
	public bool IsAllowed(string? url) => TryParse(url, out _);

	/// <summary>Parses <paramref name="url"/>; false unless it is an absolute http or https URL.</summary>
	public bool TryParse(string? url, out Uri uri)
	{
		uri = null!;
		if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
			return false;

		if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
			return false;

		uri = parsed;
		return true;
	}
}
