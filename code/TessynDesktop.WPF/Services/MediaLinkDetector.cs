using System.Text.RegularExpressions;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Finds https links to media in an entry's text (FR.26.4): the path ends in an image or video extension, or the host is on the
/// <see cref="MediaPreviewPolicy"/> allowlist. At most <see cref="Constants.Limits.MaxMediaPreviewsPerEntry"/> per entry. Detection only
/// reads text; whether a preview loads on its own or after a click is decided later (page and surface), never here.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class MediaLinkDetector
{
	private readonly MediaPreviewPolicy _policy;

	public MediaLinkDetector(MediaPreviewPolicy policy)
	{
		_policy = policy;
	}

	/// <summary>The https media URLs of <paramref name="content"/>, in order of appearance, without duplicates.</summary>
	public IReadOnlyList<string> Detect(string content)
	{
		if (string.IsNullOrEmpty(content) || !content.Contains("https://", StringComparison.OrdinalIgnoreCase))
			return [];

		var found = new List<string>();
		foreach (Match match in UrlPattern().Matches(content))
		{
			var text = match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
			if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || uri.Host.Length == 0)
				continue;

			if (!IsMedia(uri) && !_policy.IsAllowedHost(uri.Host))
				continue;

			var url = uri.AbsoluteUri;
			if (found.Contains(url, StringComparer.Ordinal))
				continue;

			found.Add(url);
			if (found.Count == Constants.Limits.MaxMediaPreviewsPerEntry)
				break;
		}

		return found;
	}

	private static bool IsMedia(Uri uri)
	{
		var path = uri.AbsolutePath;
		var dot = path.LastIndexOf('.');
		if (dot < 0 || path.IndexOf('/', dot) >= 0)
			return false;

		var extension = path[dot..];
		return Constants.FileTypes.RemoteImage.Contains(extension) || Constants.FileTypes.Video.Contains(extension);
	}

	[GeneratedRegex(@"https://[^\s<>""'`\)\]]+", RegexOptions.IgnoreCase)]
	private static partial Regex UrlPattern();
}
