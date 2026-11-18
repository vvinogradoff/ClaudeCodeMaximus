using System.Text.RegularExpressions;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Turns what the user typed into the browser bar's address box into the address to open (FR.27.10): trimmed, <c>https://</c> added when there is no scheme
/// (<c>github.com/x</c>, <c>localhost:8080/x</c>), and accepted only when <see cref="UrlPolicy"/> allows it (http and https). Text that starts with a scheme of its own
/// (<c>ftp://x</c>, <c>javascript:x</c>) is refused, never rewritten; so is text with a space inside, and so is a scheme from <see cref="Constants.Browser.RefusedSchemes"/> even when it looks like a host and a port
/// (<c>javascript:1</c>, <c>mailto:5</c>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class BrowserAddressInput
{
	private const string DefaultScheme = "https://";
	private const string ProtocolRelativePrefix = "//";

	private readonly UrlPolicy _policy;

	public BrowserAddressInput(UrlPolicy policy)
	{
		_policy = policy;
	}

	/// <summary>A scheme followed by something that is not a port number (so <c>localhost:8080</c> is a host with a port, not a scheme).</summary>
	[GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:(?!\d)", RegexOptions.CultureInvariant)]
	private static partial Regex SchemePrefix();

	/// <summary>The normalized absolute http or https address, or false when <paramref name="typed"/> is empty or is not an http or https address.</summary>
	public bool TryNormalize(string? typed, out Uri uri)
	{
		uri = null!;
		var text = typed?.Trim();
		if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace) || StartsWithRefusedScheme(text))
			return false;

		if (text.StartsWith(ProtocolRelativePrefix, StringComparison.Ordinal))
			text = "https:" + text;
		else if (!SchemePrefix().IsMatch(text))
			text = DefaultScheme + text;

		return _policy.TryParse(text, out uri);
	}

	private static bool StartsWithRefusedScheme(string text) =>
		Constants.Browser.RefusedSchemes.Any(scheme => text.StartsWith(scheme + ":", StringComparison.OrdinalIgnoreCase));
}
