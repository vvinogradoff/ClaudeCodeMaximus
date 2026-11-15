using TessynDesktop.Client;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The allowlist of hosts whose https media previews load without a click (FR.26.4, decision A13): the <c>mediaPreviewHosts</c> of
/// <c>catalog.get</c>. The list is optional on the wire; while it is absent or empty every host is click-to-load. A pattern is an
/// exact host (<c>example.com</c>) or <c>*.example.com</c>, which matches subdomains only. The transcript page applies the same
/// rule itself (<c>media-gate.js</c>), and the surface refuses any request the user did not approve, so a compromised page still
/// cannot fetch from a host that is neither allowlisted nor clicked.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class MediaPreviewPolicy
{
	private const string WildcardPrefix = "*.";

	private volatile IReadOnlyList<string> _hosts = [];

	/// <summary>Raised after the allowlist changed.</summary>
	public event EventHandler? Changed;

	/// <summary>The patterns in force (lower case).</summary>
	public IReadOnlyList<string> Hosts => _hosts;

	/// <summary>Replaces the allowlist; blank and malformed patterns are dropped.</summary>
	public void SetHosts(IEnumerable<string>? hosts)
	{
		var clean = (hosts ?? [])
			.Select(h => h?.Trim().ToLowerInvariant() ?? string.Empty)
			.Where(IsValidPattern)
			.Distinct()
			.ToList();

		if (clean.SequenceEqual(_hosts))
			return;

		_hosts = clean;
		Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>True when <paramref name="host"/> is on the allowlist.</summary>
	public bool IsAllowedHost(string? host)
	{
		if (string.IsNullOrWhiteSpace(host))
			return false;

		var candidate = host.Trim().TrimEnd('.').ToLowerInvariant();
		foreach (var pattern in _hosts)
		{
			if (pattern.StartsWith(WildcardPrefix, StringComparison.Ordinal))
			{
				var suffix = pattern[1..]; // ".example.com"
				if (candidate.Length > suffix.Length && candidate.EndsWith(suffix, StringComparison.Ordinal))
					return true;
			}
			else if (candidate == pattern)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Reads the allowlist from <c>catalog.get</c>; a daemon that does not send it (or a failed call) leaves nothing allowlisted.</summary>
	public async Task RefreshAsync(ITessynClient client, CancellationToken cancellationToken = default)
	{
		try
		{
			var catalog = await client.CatalogGetAsync(cancellationToken);
			SetHosts(catalog.MediaPreviewHosts);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			SetHosts(null);
		}
	}

	private static bool IsValidPattern(string pattern)
	{
		var name = pattern.StartsWith(WildcardPrefix, StringComparison.Ordinal) ? pattern[WildcardPrefix.Length..] : pattern;
		return name.Length > 0 && name.Contains('.') && Uri.CheckHostName(name) == UriHostNameType.Dns;
	}
}
