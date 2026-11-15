using System.Net;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The host-side media proxy (review V2): what it will fetch, how it follows redirects, and what it refuses.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RemoteMediaFetcherTests : IDisposable
{
	private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

	private readonly MediaPreviewPolicy _policy = new();
	private readonly TempAppPaths _temp = new();

	public RemoteMediaFetcherTests()
	{
		_policy.SetHosts(["cdn.example.com", "*.images.example.org"]);
	}

	public void Dispose() => _temp.Dispose();

	private RemoteMediaFetcher Fetcher(FakeHttpHandler handler) => new(new HttpClient(handler), _policy, new PrivateAddressCheck());

	private static FakeHttpHandler ServingPng() => new(_ => FakeHttpHandler.Ok(Png, "image/png"));

	[Fact]
	public async Task AnAllowlistedHost_IsFetched_WithoutCookiesOrReferer()
	{
		var handler = ServingPng();

		var image = await Fetcher(handler).FetchImageAsync(new Uri("https://cdn.example.com/a.png"), approvedByClick: false, CancellationToken.None);

		Assert.Equal(Png, image.Bytes);
		Assert.Equal("image/png", image.ContentType);
		var request = Assert.Single(handler.Requests);
		Assert.Equal(HttpMethod.Get, request.Method);
		Assert.Null(request.Headers.Referrer);
		Assert.False(request.Headers.Contains("Cookie"));
		Assert.False(request.Headers.Contains("Authorization"));
	}

	[Fact]
	public async Task UnapprovedAndNotAllowlisted_IsRefused_WithoutAnyRequest()
	{
		var handler = ServingPng();

		await Assert.ThrowsAsync<MediaRefusedException>(() => Fetcher(handler).FetchImageAsync(new Uri("https://tracker.example.net/p.png?d=SECRET"), false, CancellationToken.None));

		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task ApprovedByAClick_IsFetched_EvenWhenNotAllowlisted()
	{
		var handler = ServingPng();

		var image = await Fetcher(handler).FetchImageAsync(new Uri("https://other.example.net/p.png"), approvedByClick: true, CancellationToken.None);

		Assert.Equal(Png, image.Bytes);
		Assert.Single(handler.Requests);
	}

	[Theory]
	[InlineData("http://cdn.example.com/a.png")]
	[InlineData("https://user:pw@cdn.example.com/a.png")]
	[InlineData("https://cdn.example.com:8443/a.png")]
	[InlineData("ftp://cdn.example.com/a.png")]
	[InlineData("https://localhost/a.png")]
	[InlineData("https://127.0.0.1/a.png")]
	[InlineData("https://10.0.0.5/a.png")]
	[InlineData("https://192.168.1.1/a.png")]
	[InlineData("https://169.254.169.254/latest/meta-data")]
	[InlineData("https://[::1]/a.png")]
	[InlineData("https://intranet/a.png")]
	[InlineData("https://printer.local/a.png")]
	public async Task UnsafeAddresses_AreRefusedEvenWhenClicked(string url)
	{
		var handler = ServingPng();

		await Assert.ThrowsAsync<MediaRefusedException>(() => Fetcher(handler).FetchImageAsync(new Uri(url), approvedByClick: true, CancellationToken.None));

		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task ARedirectToAForeignHost_IsRefused_AndNeverRequested()
	{
		var handler = new FakeHttpHandler(r => r.RequestUri!.Host == "cdn.example.com" ? FakeHttpHandler.Redirect("https://attacker.example.net/steal.png") : FakeHttpHandler.Ok(Png, "image/png"));

		await Assert.ThrowsAsync<MediaRefusedException>(() => Fetcher(handler).FetchImageAsync(new Uri("https://cdn.example.com/a.png"), false, CancellationToken.None));

		var request = Assert.Single(handler.Requests);                      // only the first hop was ever sent
		Assert.Equal("cdn.example.com", request.RequestUri!.Host);
	}

	[Theory]
	[InlineData("http://cdn.example.com/b.png")]
	[InlineData("https://10.1.2.3/b.png")]
	[InlineData("https://cdn.example.com:444/b.png")]
	[InlineData("https://user:pw@cdn.example.com/b.png")]
	public async Task ARedirectToAnUnsafeTarget_IsRefused(string target)
	{
		var handler = new FakeHttpHandler(r => r.RequestUri!.AbsolutePath == "/a.png" ? FakeHttpHandler.Redirect(target) : FakeHttpHandler.Ok(Png, "image/png"));

		await Assert.ThrowsAsync<MediaRefusedException>(() => Fetcher(handler).FetchImageAsync(new Uri("https://cdn.example.com/a.png"), false, CancellationToken.None));

		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task RedirectsWithinTheSameHostOrToAnotherAllowlistedHost_AreFollowed_AtMostThreeTimes()
	{
		var hops = 0;
		var handler = new FakeHttpHandler(r =>
		{
			var step = hops++;
			return step switch
			{
				0 => FakeHttpHandler.Redirect("/b.png"),                                   // relative, same host
				1 => FakeHttpHandler.Redirect("https://cdn2.images.example.org/c.png"),    // another allowlisted host (wildcard)
				_ => FakeHttpHandler.Ok(Png, "image/png"),
			};
		});

		var image = await Fetcher(handler).FetchImageAsync(new Uri("https://cdn.example.com/a.png"), false, CancellationToken.None);

		Assert.Equal(Png, image.Bytes);
		Assert.Equal(3, handler.Requests.Count);

		var endless = new FakeHttpHandler(_ => FakeHttpHandler.Redirect("/again.png"));
		await Assert.ThrowsAsync<MediaRefusedException>(() => Fetcher(endless).FetchImageAsync(new Uri("https://cdn.example.com/a.png"), false, CancellationToken.None));
		Assert.Equal(Constants.Limits.MaxMediaRedirects + 1, endless.Requests.Count);
	}

	[Fact]
	public async Task AClickedUrl_MayRedirectWithinItsOwnHost_ButNotElsewhere()
	{
		var handler = new FakeHttpHandler(r => r.RequestUri!.AbsolutePath == "/a.png" ? FakeHttpHandler.Redirect("https://other.example.net/b.png") : FakeHttpHandler.Redirect("https://third.example.net/c.png"));

		await Assert.ThrowsAsync<MediaRefusedException>(() => Fetcher(handler).FetchImageAsync(new Uri("https://other.example.net/a.png"), true, CancellationToken.None));

		Assert.Equal(2, handler.Requests.Count);   // same host hop followed, the foreign second hop refused
	}

	[Theory]
	[InlineData("text/html")]
	[InlineData("image/svg+xml")]
	[InlineData("application/octet-stream")]
	[InlineData("video/mp4")]
	[InlineData("")]
	public async Task WrongContentTypesForAnImage_AreRefused(string contentType)
	{
		var handler = new FakeHttpHandler(_ => contentType.Length == 0 ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) } : FakeHttpHandler.Ok(Png, contentType));

		await Assert.ThrowsAsync<MediaRefusedException>(() => Fetcher(handler).FetchImageAsync(new Uri("https://cdn.example.com/a.png"), false, CancellationToken.None));
	}

	[Fact]
	public async Task OversizedImages_AreRefused_ByDeclaredLengthAndByTheBytesActuallyRead()
	{
		var big = new byte[(int)Constants.Limits.MaxRemoteImageBytes + 1];

		var declared = new FakeHttpHandler(_ => FakeHttpHandler.Ok(big, "image/png"));
		await Assert.ThrowsAsync<MediaRefusedException>(() => Fetcher(declared).FetchImageAsync(new Uri("https://cdn.example.com/a.png"), false, CancellationToken.None));

		var hidden = new FakeHttpHandler(_ => FakeHttpHandler.Ok(big, "image/png", declareLength: false));
		var ex = await Assert.ThrowsAsync<MediaRefusedException>(() => Fetcher(hidden).FetchImageAsync(new Uri("https://cdn.example.com/a.png"), false, CancellationToken.None));
		Assert.Contains("too large", ex.Message);
	}

	[Fact]
	public async Task ErrorStatuses_AreRefused()
	{
		var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

		await Assert.ThrowsAsync<MediaRefusedException>(() => Fetcher(handler).FetchImageAsync(new Uri("https://cdn.example.com/a.png"), false, CancellationToken.None));
	}

	[Fact]
	public async Task Videos_AreDownloadedToTheChosenFile_OnlyForVideoTypes()
	{
		var video = new byte[] { 0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70 };
		var handler = new FakeHttpHandler(_ => FakeHttpHandler.Ok(video, "video/mp4"));
		var store = new PreviewFileStore(_temp.Paths);

		var path = await Fetcher(handler).DownloadVideoAsync(new Uri("https://cdn.example.com/v.mp4"), false, _ => store.NewFilePath(".mp4"), CancellationToken.None);

		Assert.Equal("content.mp4", Path.GetFileName(path));
		Assert.Equal(video, await File.ReadAllBytesAsync(path));

		var html = new FakeHttpHandler(_ => FakeHttpHandler.Ok(video, "text/html"));
		await Assert.ThrowsAsync<MediaRefusedException>(() => Fetcher(html).DownloadVideoAsync(new Uri("https://cdn.example.com/v.mp4"), false, _ => store.NewFilePath(".mp4"), CancellationToken.None));
		var unapproved = ServingPng();
		await Assert.ThrowsAsync<MediaRefusedException>(() => Fetcher(unapproved).DownloadVideoAsync(new Uri("https://nope.example.net/v.mp4"), false, _ => store.NewFilePath(".mp4"), CancellationToken.None));
		Assert.Empty(unapproved.Requests);
	}

	[Fact]
	public void MayRequest_AppliesTheSameInitialPolicy()
	{
		var fetcher = Fetcher(ServingPng());

		Assert.True(fetcher.MayRequest(new Uri("https://cdn.example.com/a.png"), approvedByClick: false));
		Assert.True(fetcher.MayRequest(new Uri("https://x.images.example.org/a.png"), approvedByClick: false));
		Assert.False(fetcher.MayRequest(new Uri("https://images.example.org/a.png"), approvedByClick: false));   // a wildcard is subdomains only
		Assert.False(fetcher.MayRequest(new Uri("https://other.example.net/a.png"), approvedByClick: false));
		Assert.True(fetcher.MayRequest(new Uri("https://other.example.net/a.png"), approvedByClick: true));
	}
}
