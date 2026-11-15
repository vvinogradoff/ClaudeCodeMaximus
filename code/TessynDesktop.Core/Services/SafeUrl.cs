using System;

namespace TessynDesktop.Services;

/// <summary>
/// URL rules of the integration methods (package J3 part 2). A URL a client sends must be http or https without user info (a password in a URL would
/// be echoed and logged), and a URL a result shows has its user info, query and fragment cut off, since a query often carries an API key.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class SafeUrl
{
	/// <summary>Parses <paramref name="text"/> as an absolute http or https URL without user info; false for anything else.</summary>
	public static bool TryParse(string text, out Uri uri)
	{
		if (Uri.TryCreate(text, UriKind.Absolute, out var parsed)
			&& (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
			&& string.IsNullOrEmpty(parsed.UserInfo)
			&& !string.IsNullOrEmpty(parsed.Host))
		{
			uri = parsed;
			return true;
		}

		uri = null!;
		return false;
	}

	/// <summary>
	/// The URL as a result may show it: scheme, host, port and path only. Empty when the text is not an http(s) URL, so a stored value that is not
	/// a URL (and might be anything) is never echoed. <paramref name="hadQuery"/> says a query was cut off.
	/// </summary>
	public static string ForDisplay(string? text, out bool hadQuery)
	{
		hadQuery = false;
		if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
			|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || string.IsNullOrEmpty(uri.Host))
			return string.Empty;

		hadQuery = uri.Query.Length > 1;
		return WithoutUserInfo(uri) + (uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath);
	}

	/// <summary><c>scheme://host[:port]</c> of <paramref name="uri"/>, never with user info (<see cref="Uri.GetLeftPart"/> would keep it).</summary>
	private static string WithoutUserInfo(Uri uri) => $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? string.Empty : ":" + uri.Port)}";

	/// <summary>An OAuth endpoint as a result may show it: kept whole (its query is part of the endpoint and is public metadata) unless it holds user info or is not http(s).</summary>
	public static string ForEndpointDisplay(string? text)
	{
		if (string.IsNullOrWhiteSpace(text) || !TryParse(text.Trim(), out var uri))
			return string.Empty;

		return WithoutUserInfo(uri) + uri.AbsolutePath + uri.Query;
	}

	/// <summary>The origin (<c>scheme://host:port</c>, lower case) a secret is bound to, or null for an empty or invalid URL.</summary>
	public static string? Origin(string? text)
	{
		if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
			return null;

		return $"{uri.Scheme}://{uri.Host}:{uri.Port}".ToLowerInvariant();
	}
}
