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

	private static string Html(string urls, string hosts, string loaded = "new Set()") =>
		Eval($"g.previewHtml({urls},{hosts},7,{loaded})").GetString()!;

	[Fact]
	public void AnAllowlistedHost_AutoLoadsItsPreview_WithReferrerPolicyAndLazyLoading()
	{
		var html = Html("['https://annielartens.online/r/1.png']", "['annielartens.online']");

		Assert.Contains("<img", html);
		Assert.Contains("src=\"https://annielartens.online/r/1.png\"", html);
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
		var html = Html("['https://example.com/a.png','https://example.com/b.png']", "[]", "new Set(['7:1'])");

		Assert.Contains("src=\"https://example.com/b.png\"", html);
		Assert.DoesNotContain("src=\"https://example.com/a.png\"", html);
		Assert.Contains("Load preview example.com", html);
	}

	[Fact]
	public void VideoLinksGetAVideoElement()
	{
		var html = Html("['https://annielartens.online/clip.webm']", "['annielartens.online']");

		Assert.Contains("<video", html);
		Assert.Contains("controls", html);
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
	public void PageCspAllowsRemoteMediaOnlyAsImagesAndMedia_NothingElse()
	{
		var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, Constants.Paths.TranscriptPageFileName));

		Assert.Contains("default-src 'none'", html);
		Assert.Contains("script-src 'self'", html);
		Assert.Contains("img-src https:", html);
		Assert.Contains("media-src https:", html);
		Assert.Contains("connect-src 'none'", html);
		Assert.Contains("frame-src 'none'", html);
		Assert.Contains("object-src 'none'", html);
		Assert.DoesNotContain("script-src 'self' 'unsafe", html);
		Assert.DoesNotContain("script-src https:", html);

		var preview = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, Constants.Paths.PreviewPageFileName));
		Assert.Contains("img-src https://preview.local", preview);
		Assert.Contains("media-src https://preview.local", preview);
		Assert.DoesNotContain("img-src https:;", preview);
		Assert.Contains("connect-src 'none'", preview);
	}
}
