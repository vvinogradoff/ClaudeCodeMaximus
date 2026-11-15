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

	private TranscriptMediaBroker BrokerOver(FakeHttpHandler handler) =>
		new(new RemoteMediaFetcher(new HttpClient(handler), _policy, new PrivateAddressCheck()), _store, _proxy, NullLogger<TranscriptMediaBroker>.Instance);

	private static async Task<HttpResponseMessage> Never(CancellationToken token)
	{
		await Task.Delay(Timeout.Infinite, token);
		return new HttpResponseMessage(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Reset_CancelsRunningDownloads_AndNothingIsLeftOnDisk()
	{
		var started = new TaskCompletionSource();
		var handler = new FakeHttpHandler(async (_, ct) =>
		{
			started.TrySetResult();
			return await Never(ct);
		});
		var broker = BrokerOver(handler);
		const string video = "https://cdn.example.com/clip.mp4";
		broker.Offer(1, [video]);
		broker.TryApprove(1, video);

		var download = broker.DownloadVideoAsync(video, CancellationToken.None);
		await started.Task;
		broker.Reset();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
		Assert.False(Directory.Exists(_temp.Paths.PreviewCacheDirectory) && Directory.EnumerateFiles(_temp.Paths.PreviewCacheDirectory, "*", SearchOption.AllDirectories).Any());
	}

	[Fact]
	public async Task ADownloadThatFinishesInTheRaceWithAReset_IsDeletedNotKept()
	{
		var release = new TaskCompletionSource();
		var handler = new FakeHttpHandler(async (_, _) =>
		{
			await release.Task;                                 // ignores cancellation, like a download that was already past its last check
			return FakeHttpHandler.Ok([1, 2, 3], "video/mp4");
		});
		var broker = BrokerOver(handler);
		const string video = "https://cdn.example.com/clip.mp4";
		broker.Offer(1, [video]);
		broker.TryApprove(1, video);
		var download = broker.DownloadVideoAsync(video, CancellationToken.None);

		broker.Reset();
		release.SetResult();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
		Assert.False(Directory.Exists(_temp.Paths.PreviewCacheDirectory) && Directory.EnumerateFiles(_temp.Paths.PreviewCacheDirectory, "*", SearchOption.AllDirectories).Any());
	}

	[Fact]
	public async Task AtMostTwoVideoDownloadsRunAtOnce()
	{
		var running = 0;
		var peak = 0;
		var gate = new TaskCompletionSource();
		var handler = new FakeHttpHandler(async (_, _) =>
		{
			var now = Interlocked.Increment(ref running);
			peak = Math.Max(peak, now);
			await gate.Task;
			Interlocked.Decrement(ref running);
			return FakeHttpHandler.Ok([1], "video/mp4");
		});
		var broker = BrokerOver(handler);
		var urls = Enumerable.Range(0, 5).Select(i => $"https://cdn.example.com/clip{i}.mp4").ToList();
		broker.Offer(1, urls);
		urls.ForEach(u => broker.TryApprove(1, u));

		var downloads = urls.Select(u => broker.DownloadVideoAsync(u, CancellationToken.None)).ToList();
		await Task.Delay(300);
		Assert.Equal(Constants.Limits.MaxConcurrentVideoDownloads, Volatile.Read(ref running));

		gate.SetResult();
		await Task.WhenAll(downloads);

		Assert.Equal(Constants.Limits.MaxConcurrentVideoDownloads, peak);
	}

	[Fact]
	public async Task TheTotalDownloadedBytesPerLoadAreCapped()
	{
		var handler = new FakeHttpHandler(_ => FakeHttpHandler.Ok(new byte[1000], "video/mp4"));
		var broker = BrokerOver(handler);
		var urls = new[] { "https://cdn.example.com/a.mp4", "https://cdn.example.com/b.mp4" };
		broker.Offer(1, urls);
		foreach (var url in urls)
			broker.TryApprove(1, url);
		broker.ForceDownloadedBytesForTest(Constants.Limits.MaxVideoBytesPerLoad - 500);   // only 500 bytes left

		var ex = await Assert.ThrowsAsync<MediaRefusedException>(() => broker.DownloadVideoAsync(urls[0], CancellationToken.None));   // 1000 bytes exceed what is left
		Assert.Contains("too large", ex.Message);

		broker.ForceDownloadedBytesForTest(Constants.Limits.MaxVideoBytesPerLoad);
		var used = await Assert.ThrowsAsync<MediaRefusedException>(() => broker.DownloadVideoAsync(urls[1], CancellationToken.None));
		Assert.Contains("limit", used.Message);

		broker.Reset();                                             // a new view starts with a fresh budget
		broker.Offer(1, urls);
		broker.TryApprove(1, urls[0]);
		Assert.NotEmpty(await broker.DownloadVideoAsync(urls[0], CancellationToken.None));
	}

	[Fact]
	public async Task TheImageCacheIsBoundedByTotalBytesToo()
	{
		var tenMb = new byte[(int)Constants.Limits.MaxRemoteImageBytes];
		var handler = new FakeHttpHandler(_ => FakeHttpHandler.Ok(tenMb, "image/png"));
		var broker = BrokerOver(handler);
		var urls = Enumerable.Range(0, 14).Select(i => $"https://cdn.example.com/{i}.png").ToList();   // 14 x 10 MB = 140 MB > 100 MB
		broker.Offer(1, urls);

		foreach (var url in urls)
			await broker.ServeImageAsync(new Uri(_proxy.Build(url)), CancellationToken.None);

		Assert.Equal(14, handler.Requests.Count);
		Assert.True(broker.CachedImageBytes <= Constants.Limits.MaxImageCacheBytes);
		var before = handler.Requests.Count;
		await broker.ServeImageAsync(new Uri(_proxy.Build(urls[^1])), CancellationToken.None);   // the newest is still cached
		Assert.Equal(before, handler.Requests.Count);
		await broker.ServeImageAsync(new Uri(_proxy.Build(urls[0])), CancellationToken.None);    // the oldest was evicted
		Assert.Equal(before + 1, handler.Requests.Count);
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

	[Theory]
	[InlineData("::7f00:1", false)]                    // IPv4-compatible ::127.0.0.1
	[InlineData("::a00:5", false)]                     // ::10.0.0.5
	[InlineData("::808:808", true)]                    // ::8.8.8.8 is routable by embedded address
	[InlineData("64:ff9b::7f00:1", false)]             // NAT64 of 127.0.0.1
	[InlineData("64:ff9b::a00:5", false)]              // NAT64 of 10.0.0.5
	[InlineData("64:ff9b::c0a8:101", false)]           // NAT64 of 192.168.1.1
	[InlineData("64:ff9b::808:808", true)]             // NAT64 of 8.8.8.8
	[InlineData("64:ff9b:1::1", false)]                // local-use NAT64 prefix
	[InlineData("2002:7f00:1::1", false)]              // 6to4 of 127.0.0.1
	[InlineData("2002:a00:5::1", false)]               // 6to4 of 10.0.0.5
	[InlineData("2002:c0a8:101::1", false)]            // 6to4 of 192.168.1.1
	[InlineData("2002:808:808::1", true)]              // 6to4 of 8.8.8.8
	[InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2", false)]   // Teredo (2001::/32) is refused
	[InlineData("2001:db8::1", true)]
	[InlineData("198.18.0.1", false)]
	[InlineData("198.19.255.254", false)]
	[InlineData("198.17.0.1", true)]
	[InlineData("198.20.0.1", true)]
	[InlineData("192.0.2.10", false)]
	[InlineData("198.51.100.7", false)]
	[InlineData("203.0.113.9", false)]
	[InlineData("203.0.114.9", true)]
	[InlineData("198.51.101.7", true)]
	public void TransitionRangesAndDocumentationBlocks(string address, bool expected)
	{
		Assert.Equal(expected, _check.IsPublic(IPAddress.Parse(address)));
		Assert.Equal(expected, _check.IsPublicHost("[" + address + "]") || (!address.Contains(':') && _check.IsPublicHost(address)));
	}

	[Fact]
	public void TheMediaHttpClient_NeverUsesASystemProxy_AndFollowsNoRedirectsOrCookies()
	{
		using var handler = new RemoteMediaHttpClientFactory(_check).CreateHandler();

		Assert.False(handler.UseProxy);            // with a proxy the connect-time address check would see the proxy, not the target (N1)
		Assert.False(handler.AllowAutoRedirect);
		Assert.False(handler.UseCookies);
		Assert.Null(handler.Credentials);
		Assert.NotNull(handler.ConnectCallback);
	}

	[Fact]
	public void ANameThatResolvesToTheLocalNetworkIsStoppedAtConnectTime()
	{
		Assert.False(_check.IsPublic(IPAddress.Parse("192.168.1.10")));
		Assert.True(_check.IsPublic(IPAddress.Parse("93.184.216.34")));
	}
}
