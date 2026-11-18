namespace TessynDesktop.WPF.Services;

/// <summary>
/// Which top-level navigations the embedded browser accepts (FR.27.2): http, https and <c>about:blank</c>. Everything else
/// (<c>slack://</c>, <c>mailto:</c>, <c>file:</c>, <c>javascript:</c>, ...) is cancelled; nothing is handed to the operating system.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class BrowserNavigationPolicy
{
	/// <summary>True for an absolute http or https URI, or exactly <c>about:blank</c>.</summary>
	public bool IsAllowedTopLevel(string? uri)
	{
		if (string.IsNullOrWhiteSpace(uri))
			return false;

		var text = uri.Trim();
		if (string.Equals(text, Constants.Browser.BlankUri, StringComparison.OrdinalIgnoreCase))
			return true;

		if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed))
			return false;

		return parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps;
	}
}
