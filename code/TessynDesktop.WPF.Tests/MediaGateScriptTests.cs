using System.Diagnostics;
using System.Text.Json;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The transcript page's remote-media gating (<c>Web/media-gate.js</c>, FR.26.4, decision A13), run under Node: the same pure
/// functions the page uses. Needs <c>node</c> on PATH; the test fails loudly when it is missing rather than passing without checking.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class MediaGateScriptTests
{
	private static readonly string GateFile = Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, "media-gate.js");

	/// <summary>Runs <paramref name="expression"/> (JavaScript, with <c>g</c> = the module) and returns its JSON result.</summary>
	private static JsonElement Eval(string expression)
	{
		Assert.True(File.Exists(GateFile), "media-gate.js was not copied next to the test assembly: " + GateFile);
		var script = $"const g=require({JsonSerializer.Serialize(GateFile)});process.stdout.write(JSON.stringify({expression}));";
		var info = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
		info.ArgumentList.Add("-e");
		info.ArgumentList.Add(script);
		using var process = Process.Start(info) ?? throw new InvalidOperationException("node could not be started (it must be on PATH)");
		var output = process.StandardOutput.ReadToEnd();
		var error = process.StandardError.ReadToEnd();
		Assert.True(process.WaitForExit(20_000), "node did not finish");
		Assert.True(process.ExitCode == 0, "node failed: " + error);
		return JsonDocument.Parse(output).RootElement.Clone();
	}

	private static string Html(string urls, string hosts, string loaded = "new Map()") =>
		Eval($"g.previewHtml({urls},{hosts},7,{loaded})").GetString()!;

	[Fact]
	public void AnAllowlistedHost_AutoLoadsItsPreview_WithReferrerPolicyAndLazyLoading()
	{
		var html = Html("['https://annielartens.online/r/1.png']", "['annielartens.online']");

		Assert.Contains("<img", html);
		// The page never names the remote host as an address: the image goes through the host's media proxy.
		Assert.Contains("src=\"https://media.local/i?u=https%3A%2F%2Fannielartens.online%2Fr%2F1.png\"", html);
		Assert.DoesNotContain("src=\"https://annielartens", html);
		Assert.Contains("referrerpolicy=\"no-referrer\"", html);
		Assert.Contains("loading=\"lazy\"", html);
		Assert.DoesNotContain("mload", html);
	}

	[Fact]
	public void AWildcardHost_AutoLoadsSubdomains_ButNotTheApexOrLookalikes()
	{
		Assert.Contains("src=", Html("['https://raw.githubusercontent.com/o/r/x.png']", "['*.githubusercontent.com']"));
		foreach (var url in new[] { "https://githubusercontent.com/x.png", "https://evilgithubusercontent.com/x.png", "https://githubusercontent.com.evil.example/x.png" })
			Assert.DoesNotContain("src=", Html($"['{url}']", "['*.githubusercontent.com']"));
	}

	[Fact]
	public void AnyOtherHost_GetsAButton_AndTheHtmlContainsNoRemoteAddressAtAll()
	{
		var html = Html("['https://tracker.example.com/pixel.png?id=secret-token','https://other.example.org/v.mp4']", "['annielartens.online']");

		Assert.Contains("Load preview tracker.example.com", html);
		Assert.Contains("Load preview other.example.org", html);
		Assert.Equal(2, html.Split("class=\"mload\"").Length - 1);
		// Nothing a browser could request before the click: no src, no URL, no host path or query anywhere in the markup.
		Assert.DoesNotContain("src=", html);
		Assert.DoesNotContain("http", html);
		Assert.DoesNotContain("secret-token", html);
		Assert.DoesNotContain("pixel.png", html);
		Assert.DoesNotContain("<img", html);
		Assert.DoesNotContain("<video", html);
	}

	[Fact]
	public void WithoutAnAllowlist_EverythingIsClickToLoad()
	{
		var html = Html("['https://annielartens.online/a.png','https://example.com/b.png']", "[]");

		Assert.DoesNotContain("src=", html);
		Assert.Equal(2, html.Split("class=\"mload\"").Length - 1);
	}

	[Fact]
	public void AfterTheClick_TheLoadedSlotBecomesAnElement()
	{
		// "loaded" is keyed by URL, not by position: reordering the entry's media cannot load a different URL.
		var html = Html("['https://example.com/a.png','https://example.com/b.png']", "[]", "new Map([['https://example.com/b.png','']])");

		Assert.Contains("src=\"https://media.local/i?u=https%3A%2F%2Fexample.com%2Fb.png\"", html);
		Assert.DoesNotContain("a.png", html.Replace("data-j", string.Empty)); // a.png is still only a button
		Assert.Contains("Load preview example.com", html);

		var reordered = Html("['https://example.com/b.png','https://example.com/a.png']", "[]", "new Map([['https://example.com/b.png','']])");
		Assert.Contains("%2Fb.png", reordered);
		Assert.DoesNotContain("%2Fa.png", reordered);
	}

	[Fact]
	public void Videos_AreAlwaysClickToLoad_EvenOnAnAllowlistedHost_AndPlayOnlyFromThePreviewCache()
	{
		var onAllowlisted = Html("['https://annielartens.online/clip.webm']", "['annielartens.online']");
		Assert.DoesNotContain("<video", onAllowlisted);
		Assert.Contains("Load preview annielartens.online", onAllowlisted);

		var loaded = Html("['https://annielartens.online/clip.webm']", "['annielartens.online']", "new Map([['https://annielartens.online/clip.webm','https://preview.local/abc/content.webm']])");
		Assert.Contains("<video", loaded);
		Assert.Contains("src=\"https://preview.local/abc/content.webm\"", loaded);
		Assert.Contains("controls", loaded);

		var forged = Html("['https://annielartens.online/clip.webm']", "[]", "new Map([['https://annielartens.online/clip.webm','https://attacker.example/x.mp4']])");
		Assert.DoesNotContain("<video", forged);
		Assert.DoesNotContain("attacker.example", forged);
	}

	[Fact]
	public void AFailedLoad_ShowsANote_NotAnElement()
	{
		var html = Html("['https://example.com/a.png']", "[]", "new Map([['https://example.com/a.png',null]])");

		Assert.Contains("Preview unavailable", html);
		Assert.DoesNotContain("<img", html);
	}

	[Fact]
	public void HostAndPage_UseTheSameUrlForm_EveryOfferedUrlComesBackUnchanged()
	{
		// The host approves and serves exactly the strings it offered (V7): the page must not re-normalise them.
		var detector = new TessynDesktop.WPF.Services.MediaLinkDetector(new TessynDesktop.WPF.Services.MediaPreviewPolicy());
		var offered = detector.Detect("https://Example.COM/a b/%7Euser/p.png?x=1&y=%20z#frag https://example.com:443/c.jpg https://xn--bcher-kva.example/d.webp https://example.com/a/../e.gif");
		Assert.NotEmpty(offered);

		var items = Eval($"g.plan({JsonSerializer.Serialize(offered)}, [])");

		Assert.Equal(offered, items.EnumerateArray().Select(i => i.GetProperty("url").GetString()).ToList());
	}

	[Fact]
	public void OnlyHttpsUrlsWithoutCredentialsBecomePreviews()
	{
		var items = Eval("g.plan(['http://example.com/a.png','javascript:alert(1)','data:image/png;base64,AAAA','file:///C:/a.png','https://user:pw@example.com/a.png','ftp://example.com/a.png','not a url','https://ok.example.com/a.png'],['ok.example.com'])");

		var only = Assert.Single(items.EnumerateArray().ToList());
		Assert.Equal("https://ok.example.com/a.png", only.GetProperty("url").GetString());
		Assert.True(only.GetProperty("auto").GetBoolean());
	}

	[Fact]
	public void AtMostSixPreviewsPerEntry_WithoutDuplicates()
	{
		var items = Eval("g.plan(Array.from({length:20},(_, i)=>'https://example.com/'+(i%9)+'.png'),[])");

		Assert.Equal(Constants.Limits.MaxMediaPreviewsPerEntry, items.GetArrayLength());
		Assert.Equal(6, items.EnumerateArray().Select(i => i.GetProperty("url").GetString()).Distinct().Count());
	}

	[Fact]
	public void HostsAreEscapedInTheButtonText()
	{
		var html = Html("['https://a.example.com/x.png']", "[]");
		Assert.DoesNotContain("<script", html);

		var escaped = Eval("g.esc('<img src=x onerror=alert(1)>\"&\\'')").GetString();
		Assert.Equal("&lt;img src=x onerror=alert(1)&gt;&quot;&amp;&#39;", escaped);
	}

	[Fact]
	public void NeitherPageCspNamesARemoteHost_OnlyTheAppsOwnVirtualHosts()
	{
		var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, Constants.Paths.TranscriptPageFileName));
		var preview = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, Constants.Paths.PreviewPageFileName));

		Assert.Contains("default-src 'none'", html);
		Assert.Contains("script-src 'self'", html);
		Assert.Contains("img-src https://media.local https://preview.local;", html);   // the media proxy, and the cache that holds an inline local image (FR.26.5)
		Assert.Contains("media-src https://preview.local;", html);
		Assert.Contains("connect-src 'none'", html);
		Assert.Contains("frame-src 'none'", html);
		Assert.Contains("object-src 'none'", html);
		Assert.DoesNotContain("script-src 'self' 'unsafe", html);
		Assert.Contains("img-src https://preview.local data:;", preview);   // data: images of a rendered html file (FR.26.8); the transcript's policy has none
		Assert.Contains("frame-src about:;", preview);                     // the html preview's srcdoc frame only; the frame is sandboxed without scripts
		Assert.DoesNotContain("data:", html);
		Assert.Contains("media-src https://preview.local", preview);
		Assert.Contains("connect-src 'none'", preview);

		// No source in either policy is a scheme wildcard (https:, http:, data:, blob:, *) or any host other than the three virtual ones.
		foreach (var page in new[] { html, preview })
		{
			var policy = System.Text.RegularExpressions.Regex.Match(page, "Content-Security-Policy\" content=\"([^\"]*)\"").Groups[1].Value;
			if (page == preview)
				policy = policy.Replace("img-src https://preview.local data:;", "img-src https://preview.local;").Replace("frame-src about:;", "frame-src 'none';");
			Assert.DoesNotMatch(@"(^|[\s;])(https?|data|blob|filesystem|ws|wss):(?!//)", policy);
			Assert.DoesNotContain("*", policy);
			var hosts = System.Text.RegularExpressions.Regex.Matches(policy, @"https?://([^\s;/]+)").Select(m => m.Groups[1].Value).Distinct().Order().ToList();
			Assert.All(hosts, h => Assert.Contains(h, new[] { "media.local", "preview.local" }));
		}
	}
}
