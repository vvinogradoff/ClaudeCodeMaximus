using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.26.4 on the host side: which https links become media previews, and the host allowlist behind auto-loading.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class MediaPreviewTests
{
	private readonly MediaPreviewPolicy _policy = new();

	private MediaLinkDetector Detector() => new(_policy);

	[Fact]
	public void WithoutAnAllowlist_NoHostIsAllowed()
	{
		Assert.Empty(_policy.Hosts);
		Assert.False(_policy.IsAllowedHost("annielartens.online"));
		Assert.False(_policy.IsAllowedHost(null));
		Assert.False(_policy.IsAllowedHost(""));
	}

	[Fact]
	public void ExactAndWildcardPatterns()
	{
		_policy.SetHosts(["annielartens.online", "*.githubusercontent.com"]);

		Assert.True(_policy.IsAllowedHost("annielartens.online"));
		Assert.True(_policy.IsAllowedHost("ANNIELARTENS.online"));
		Assert.True(_policy.IsAllowedHost("raw.githubusercontent.com"));
		Assert.True(_policy.IsAllowedHost("a.b.githubusercontent.com"));
		Assert.False(_policy.IsAllowedHost("githubusercontent.com"));          // a wildcard covers subdomains only
		Assert.False(_policy.IsAllowedHost("evilgithubusercontent.com"));
		Assert.False(_policy.IsAllowedHost("githubusercontent.com.evil.example"));
		Assert.False(_policy.IsAllowedHost("www.annielartens.online"));        // an exact host is exact
		Assert.False(_policy.IsAllowedHost("annielartens.online.evil.example"));
	}

	[Fact]
	public void MalformedPatternsAreDropped_AndChangeIsReportedOnce()
	{
		var changes = 0;
		_policy.Changed += (_, _) => changes++;

		_policy.SetHosts(["", "  ", "nodots", "*.", "*.*.com", "http://example.com", "ok.example.com", "ok.example.com"]);
		_policy.SetHosts(["ok.example.com"]);

		Assert.Equal(["ok.example.com"], _policy.Hosts);
		Assert.Equal(1, changes);
	}

	[Fact]
	public async Task RefreshReadsTheCatalog_AndAnAbsentFieldOrAFailureLeavesNothingAllowlisted()
	{
		var client = new FakeTessynClient { Catalog = new CatalogResult([], [], []) { MediaPreviewHosts = ["a.example.com"] } };
		await _policy.RefreshAsync(client);
		Assert.Equal(["a.example.com"], _policy.Hosts);

		client.Catalog = new CatalogResult([], [], []);   // a daemon that does not send the field
		await _policy.RefreshAsync(client);
		Assert.Empty(_policy.Hosts);

		_policy.SetHosts(["a.example.com"]);
		client.CatalogException = new InvalidOperationException("daemon unavailable");
		await _policy.RefreshAsync(client);
		Assert.Empty(_policy.Hosts);
	}

	[Theory]
	[InlineData("see https://example.com/a/b.png now", "https://example.com/a/b.png")]
	[InlineData("![x](https://example.com/pic.JPG)", "https://example.com/pic.JPG")]
	[InlineData("https://example.com/a.webp?size=large#frag", "https://example.com/a.webp?size=large#frag")]
	[InlineData("clip https://cdn.example.com/v/clip.mp4.", "https://cdn.example.com/v/clip.mp4")]
	[InlineData("<https://example.com/a.gif>", "https://example.com/a.gif")]
	public void MediaByExtension(string text, string expected) => Assert.Equal([expected], Detector().Detect(text));

	[Theory]
	[InlineData("http://example.com/a.png")]
	[InlineData("ftp://example.com/a.png")]
	[InlineData("javascript:alert(1)//https://x")]
	[InlineData("https://example.com/page")]
	[InlineData("https://example.com/a.png.html")]
	[InlineData("https://example.com/file.svg")]
	[InlineData("https://user:pw@example.com/a.png")]
	[InlineData("plain text without links")]
	[InlineData("")]
	public void NotMedia_NoPreview(string text) => Assert.Empty(Detector().Detect(text));

	[Fact]
	public void AllowlistedHostsPreviewAnyLink_OthersOnlyByExtension()
	{
		_policy.SetHosts(["annielartens.online"]);

		var found = Detector().Detect("https://annielartens.online/results/42 and https://other.example/results/42");

		Assert.Equal(["https://annielartens.online/results/42"], found);
	}

	[Fact]
	public void AtMostSixPerEntry_WithoutDuplicates()
	{
		var text = string.Join(" ", Enumerable.Range(0, 12).Select(i => $"https://example.com/{i % 8}.png"));

		var found = Detector().Detect(text);

		Assert.Equal(Constants.Limits.MaxMediaPreviewsPerEntry, found.Count);
		Assert.Equal(found.Count, found.Distinct().Count());
	}
}
