using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The host's decisions for the transcript's remote media, through the seam the WebView2 surface calls (review V2/V7): what the page may
/// have served, what a click approves, and the request filter. Removing the broker's checks or the request policy fails these tests.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TranscriptMediaBrokerTests : IDisposable
{
	private const string Allowlisted = "https://cdn.example.com/a.png";
	private const string Foreign = "https://tracker.example.net/p.png?d=SECRET";

	private readonly MediaPreviewPolicy _policy = new();
	private readonly TempAppPaths _temp = new();
	private readonly MediaProxyUrl _proxy = new();
	private readonly FakeHttpHandler _http;
	private readonly TranscriptMediaBroker _broker;
	private readonly PreviewFileStore _store;

	public TranscriptMediaBrokerTests()
	{
		_policy.SetHosts(["cdn.example.com"]);
		_http = new FakeHttpHandler(r => r.RequestUri!.AbsolutePath.EndsWith(".mp4", StringComparison.Ordinal)
			? FakeHttpHandler.Ok([1, 2, 3], "video/mp4") : FakeHttpHandler.Ok([0x89, 0x50], "image/png"));
		_store = new PreviewFileStore(_temp.Paths);
		_broker = new TranscriptMediaBroker(new RemoteMediaFetcher(new HttpClient(_http), _policy, new PrivateAddressCheck()), _store, _proxy, NullLogger<TranscriptMediaBroker>.Instance);
	}

	public void Dispose() => _temp.Dispose();

	private Task<FetchedMedia> Serve(string remote) => _broker.ServeImageAsync(new Uri(_proxy.Build(remote)), CancellationToken.None);

	[Fact]
	public async Task ARequestForAUrlTheHostNeverOffered_IsRefused_WithoutANetworkRequest()
	{
		_broker.Offer(1, [Allowlisted]);

		await Assert.ThrowsAsync<MediaRefusedException>(() => Serve("https://cdn.example.com/other.png"));
		await Assert.ThrowsAsync<MediaRefusedException>(() => _broker.ServeImageAsync(new Uri("https://media.local/x"), CancellationToken.None));

		Assert.Empty(_http.Requests);
	}

	[Fact]
	public async Task AnOfferedAllowlistedImage_IsServedWithoutAClick()
	{
		_broker.Offer(1, [Allowlisted]);

		var image = await Serve(Allowlisted);

		Assert.Equal("image/png", image.ContentType);
		Assert.Single(_http.Requests);
	}

	[Fact]
	public async Task AnOfferedForeignImage_IsRefusedUntilClicked_ThenServed()
	{
		_broker.Offer(2, [Foreign]);

		await Assert.ThrowsAsync<MediaRefusedException>(() => Serve(Foreign));   // nothing is requested before a click
		Assert.Empty(_http.Requests);

		Assert.True(_broker.TryApprove(2, Foreign));
		await Serve(Foreign);

		Assert.Single(_http.Requests);
		Assert.Equal("tracker.example.net", _http.Requests[0].RequestUri!.Host);
	}

	[Fact]
	public void AClickApprovesOnlyWhatTheHostOfferedForThatEntry()
	{
		_broker.Offer(2, [Foreign]);

		Assert.False(_broker.TryApprove(3, Foreign));                          // another entry
		Assert.False(_broker.TryApprove(2, "https://evil.example.net/x.png"));  // another URL
		Assert.False(_broker.TryApprove(2, Foreign + "&x=1"));                  // not the exact string
		Assert.True(_broker.TryApprove(2, Foreign));
	}

	[Fact]
	public async Task WhenAnEntryIsReplaced_AUrlItNoLongerOffersCannotBeApprovedOrServed()
	{
		_broker.Offer(2, [Foreign, "https://second.example.net/b.png"]);
		_broker.Offer(2, ["https://second.example.net/b.png"]);

		Assert.False(_broker.TryApprove(2, Foreign));
		await Assert.ThrowsAsync<MediaRefusedException>(() => Serve(Foreign));
		Assert.Empty(_http.Requests);
	}

	[Fact]
	public async Task Reset_ForgetsOffersAndApprovals()
	{
		_broker.Offer(2, [Foreign]);
		_broker.TryApprove(2, Foreign);

		_broker.Reset();

		await Assert.ThrowsAsync<MediaRefusedException>(() => Serve(Foreign));
		Assert.False(_broker.TryApprove(2, Foreign));
		Assert.Empty(_http.Requests);
	}

	[Fact]
	public async Task AServedImageIsCached_SoARepaintDoesNotFetchAgain()
	{
		_broker.Offer(1, [Allowlisted]);

		await Serve(Allowlisted);
		await Serve(Allowlisted);

		Assert.Single(_http.Requests);
	}

	[Fact]
	public async Task ARedirectAwayFromTheAllowlist_IsRefusedAtTheBrokerToo()
	{
		var handler = new FakeHttpHandler(_ => FakeHttpHandler.Redirect("https://attacker.example.net/steal.png"));
		var broker = new TranscriptMediaBroker(new RemoteMediaFetcher(new HttpClient(handler), _policy, new PrivateAddressCheck()), _store, _proxy, NullLogger<TranscriptMediaBroker>.Instance);
		broker.Offer(1, [Allowlisted]);

		await Assert.ThrowsAsync<MediaRefusedException>(() => broker.ServeImageAsync(new Uri(_proxy.Build(Allowlisted)), CancellationToken.None));

		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task Videos_NeedAClick_EvenOnAnAllowlistedHost_AndAreDownloadedToTheCache()
	{
		const string video = "https://cdn.example.com/clip.mp4";
		_broker.Offer(1, [video]);

		await Assert.ThrowsAsync<MediaRefusedException>(() => _broker.DownloadVideoAsync(video, CancellationToken.None));
		Assert.Empty(_http.Requests);

		Assert.True(_broker.TryApprove(1, video));
		var url = await _broker.DownloadVideoAsync(video, CancellationToken.None);

		Assert.StartsWith("https://preview.local/", url);
		Assert.EndsWith("/content.mp4", url);
		var file = Directory.EnumerateFiles(_temp.Paths.PreviewCacheDirectory, "content.mp4", SearchOption.AllDirectories).Single();
		Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(file));

		_broker.Reset();
		Assert.False(File.Exists(file));      // downloaded videos go away with the session view
	}

	[Fact]
	public async Task AVideoOfAnUnsupportedType_IsRefused()
	{
		var handler = new FakeHttpHandler(_ => FakeHttpHandler.Ok([1], "video/x-matroska"));
		var broker = new TranscriptMediaBroker(new RemoteMediaFetcher(new HttpClient(handler), _policy, new PrivateAddressCheck()), _store, _proxy, NullLogger<TranscriptMediaBroker>.Instance);
		broker.Offer(1, ["https://cdn.example.com/clip.mkv"]);
		broker.TryApprove(1, "https://cdn.example.com/clip.mkv");

		await Assert.ThrowsAsync<MediaRefusedException>(() => broker.DownloadVideoAsync("https://cdn.example.com/clip.mkv", CancellationToken.None));
	}
}

/// <summary>The request filter of both pages and the proxy address form.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WebRequestPolicyTests
{
	private readonly MediaProxyUrl _proxy = new();
	private readonly WebRequestPolicy _policy;

	public WebRequestPolicyTests()
	{
		_policy = new WebRequestPolicy(_proxy);
	}

	[Theory]
	[InlineData("https://app.local/transcript.js", RequestDecision.Allow)]
	[InlineData("https://preview.local/abc/content.mp4", RequestDecision.Allow)]
	[InlineData("https://media.local/i?u=https%3A%2F%2Fcdn.example.com%2Fa.png", RequestDecision.ServeMedia)]
	[InlineData("https://MEDIA.local/i?u=x", RequestDecision.ServeMedia)]
	[InlineData("https://media.local/other?u=x", RequestDecision.Block)]
	[InlineData("https://media.local/i?u=x&v=y", RequestDecision.Block)]
	[InlineData("https://media.local/i", RequestDecision.Block)]
	[InlineData("http://app.local/transcript.js", RequestDecision.Block)]
	[InlineData("https://app.local.evil.example/x", RequestDecision.Block)]
	[InlineData("https://evil.example/?u=https://media.local/i?u=x", RequestDecision.Block)]
	[InlineData("https://cdn.example.com/a.png", RequestDecision.Block)]
	[InlineData("https://tracker.example.net/p.png?d=SECRET", RequestDecision.Block)]
	[InlineData("data:image/png;base64,AAAA", RequestDecision.Block)]
	[InlineData("ws://app.local/socket", RequestDecision.Block)]
	public void TheTranscriptPageReachesOnlyItsOwnVirtualHosts(string url, RequestDecision expected) =>
		Assert.Equal(expected, _policy.ForTranscript(new Uri(url)));

	[Theory]
	[InlineData("https://app.local/preview.js", RequestDecision.Allow)]
	[InlineData("https://preview.local/abc/content.png", RequestDecision.Allow)]
	[InlineData("https://media.local/i?u=x", RequestDecision.Block)]
	[InlineData("https://cdn.example.com/a.png", RequestDecision.Block)]
	[InlineData("http://preview.local/a", RequestDecision.Block)]
	public void ThePreviewPage_NeverReachesTheMediaProxyOrTheNetwork(string url, RequestDecision expected) =>
		Assert.Equal(expected, _policy.ForPreview(new Uri(url)));

	[Theory]
	[InlineData("https://cdn.example.com/a.png")]
	[InlineData("https://Example.com/a b/%7Euser/p.png?x=1&y=%20z#frag")]
	[InlineData("https://example.com/p.png?u=https://media.local/i?u=x")]
	[InlineData("https://xn--bcher-kva.example/d.webp")]
	public void TheProxyAddressRoundTripsAnyUrlExactly(string remote)
	{
		var built = new Uri(_proxy.Build(remote));

		Assert.True(_proxy.TryParse(built, out var parsed));
		Assert.Equal(remote, parsed);
		Assert.Equal(RequestDecision.ServeMedia, _policy.ForTranscript(built));
	}
}

/// <summary>Which addresses the media proxy may reach.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PrivateAddressCheckTests
{
	private readonly PrivateAddressCheck _check = new();

	[Theory]
	[InlineData("cdn.example.com", true)]
	[InlineData("8.8.8.8", true)]
	[InlineData("2606:4700:4700::1111", true)]
	[InlineData("localhost", false)]
	[InlineData("a.localhost", false)]
	[InlineData("printer.local", false)]
	[InlineData("intranet", false)]
	[InlineData("service.internal", false)]
	[InlineData("127.0.0.1", false)]
	[InlineData("0.0.0.0", false)]
	[InlineData("10.0.0.1", false)]
	[InlineData("172.16.0.1", false)]
	[InlineData("172.31.255.255", false)]
	[InlineData("172.32.0.1", true)]
	[InlineData("192.168.0.1", false)]
	[InlineData("169.254.169.254", false)]
	[InlineData("100.64.0.1", false)]
	[InlineData("224.0.0.1", false)]
	[InlineData("::1", false)]
	[InlineData("fe80::1", false)]
	[InlineData("fd00::1", false)]
	[InlineData("::ffff:127.0.0.1", false)]
	[InlineData("::ffff:10.0.0.1", false)]
	[InlineData("[::1]", false)]
	[InlineData("", false)]
	public void PublicVersusLocal(string host, bool expected) => Assert.Equal(expected, _check.IsPublicHost(host));

	[Fact]
	public void ANameThatResolvesToTheLocalNetworkIsStoppedAtConnectTime()
	{
		Assert.False(_check.IsPublic(IPAddress.Parse("192.168.1.10")));
		Assert.True(_check.IsPublic(IPAddress.Parse("93.184.216.34")));
	}
}
