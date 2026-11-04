using System;

namespace TessynDesktop.Services;

/// <summary>
/// Decides which tag URLs may be handed to the OS browser (FR.24.7): absolute <c>https</c> URLs whose host is
/// exactly <c>linear.app</c> or <c>github.com</c>, without user info. Anything else must never be shell-executed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class SessionTagUrlPolicy
{
	public static bool IsOpenable(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
			return false;
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
			return false;
		if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0)
			return false;

		return uri.Host == Constants.SessionTags.LinearHost || uri.Host == Constants.SessionTags.GitHubHost;
	}
}
