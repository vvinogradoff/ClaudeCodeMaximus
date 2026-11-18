namespace TessynDesktop.WPF.Services;

/// <summary>
/// Decides which web address of a preview window may be written to the open-window list (FR.29.11): only an address on the same site (scheme, host, port)
/// as the one the item was opened on, and never with sign-in parameters. A query or fragment that carries a code, state, token or the like is dropped whole.
/// Pure: no state, no I/O.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewAddressSanitizer
{
	/// <summary>True when both addresses are absolute and share scheme, host and port.</summary>
	public bool IsSameOrigin(string? first, string? second)
	{
		if (!Uri.TryCreate(first, UriKind.Absolute, out var a) || !Uri.TryCreate(second, UriKind.Absolute, out var b))
			return false;

		return string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
			&& a.Port == b.Port;
	}

	/// <summary><paramref name="address"/> as it may be saved: without query and fragment when either carries a sign-in parameter; unchanged otherwise.</summary>
	public string Sanitize(string address)
	{
		if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
			return address;

		if (!CarriesSignIn(uri.Query) && !CarriesSignIn(uri.Fragment) && !FragmentHasMarker(uri.Fragment))
			return address;

		return new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri;
	}

	private bool FragmentHasMarker(string fragment) =>
		Constants.PreviewWindow.SignInFragmentMarkers.Any(marker => fragment.Contains(marker, StringComparison.OrdinalIgnoreCase));

	private bool CarriesSignIn(string queryOrFragment)
	{
		var text = queryOrFragment.TrimStart('?', '#');
		foreach (var pair in text.Split(Constants.PreviewWindow.QuerySeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			var separator = pair.IndexOf(Constants.PreviewWindow.ParameterValueSeparator);
			var name = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
			if (Constants.PreviewWindow.SignInParameters.Contains(name, StringComparer.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}
}
