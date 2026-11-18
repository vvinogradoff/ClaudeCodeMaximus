using System.Text.RegularExpressions;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Turns what the user typed into the browser bar's address box into the address to open (FR.27.10): trimmed, <c>https://</c> added when there is no scheme
/// (<c>github.com/x</c>, <c>localhost:8080/x</c>), and accepted only when <see cref="UrlPolicy"/> allows it (http and https). <c>ftp://x</c> and
/// <c>javascript:x</c> have a scheme of their own, so they are refused, never rewritten.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static partial class BrowserAddressInput
{
	private const string DefaultScheme = "https://";

	/// <summary>A scheme followed by something that is not a port number (so <c>localhost:8080</c> is a host with a port, not a scheme).</summary>
	[GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:(?!\d)", RegexOptions.CultureInvariant)]
	private static partial Regex SchemePrefix();

	/// <summary>The normalized absolute http or https address, or false when <paramref name="typed"/> is empty or is not an http or https address.</summary>
	public static bool TryNormalize(string? typed, UrlPolicy policy, out Uri uri)
	{
		uri = null!;
		var text = typed?.Trim();
		if (string.IsNullOrEmpty(text))
			return false;

		if (!SchemePrefix().IsMatch(text) && !text.StartsWith("//", StringComparison.Ordinal))
			text = DefaultScheme + text;
		else if (text.StartsWith("//", StringComparison.Ordinal))
			text = "https:" + text;

		return policy.TryParse(text, out uri);
	}
}
