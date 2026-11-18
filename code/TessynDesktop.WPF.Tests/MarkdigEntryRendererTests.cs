using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>Transcript text is untrusted: the renderer must neutralize raw HTML, scripts, images and non-http links.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class MarkdigEntryRendererTests
{
	private readonly MarkdigEntryRenderer _renderer = new(new UrlPolicy());

	private string Html(string markdown) => _renderer.RenderMarkdown(markdown);

	[Fact]
	public void RawHtml_IsEscapedNotRendered()
	{
		var html = Html("<script>alert(1)</script> and <img src=x onerror=alert(1)> and <b onclick=\"x()\">bold</b>");

		Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("<b ", html, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("&lt;script&gt;", html);
	}

	[Fact]
	public void HtmlBlock_IsEscaped()
	{
		var html = Html("<div onmouseover=\"steal()\">\nhello\n</div>");

		Assert.DoesNotContain("<div", html, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("&lt;div", html);
	}

	[Theory]
	[InlineData("[click](javascript:alert(1))")]
	[InlineData("[click](JaVaScRiPt:alert(1))")]
	[InlineData("[click](data:text/html;base64,PHNjcmlwdD4=)")]
	[InlineData("[click](file:///c:/windows/system32/calc.exe)")]
	[InlineData("[click](vbscript:msgbox(1))")]
	[InlineData("[click](app.local/x)")]
	public void NonHttpLinks_BecomeInert(string markdown)
	{
		var html = Html(markdown);

		Assert.Contains("class=\"blocked\"", html);
		Assert.Contains("href=\"#\"", html);
		Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("file:", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("data:", html, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData("[docs](https://example.com/a?b=c&d=e)", "https://example.com/a?b=c&amp;d=e")]
	[InlineData("[docs](http://example.com/)", "http://example.com/")]
	[InlineData("see https://github.com/tryhardstop/windows-app/pull/196 now", "https://github.com/tryhardstop/windows-app/pull/196")]
	public void HttpLinks_ArePreserved(string markdown, string expectedHref)
	{
		var html = Html(markdown);

		Assert.Contains($"href=\"{expectedHref}\"", html);
		Assert.DoesNotContain("class=\"blocked\"", html);
	}

	[Fact]
	public void Images_BecomePlainTextSoNothingIsFetched()
	{
		var html = Html("![alt](https://tracker.example/pixel.png) and ![x](data:image/png;base64,AAAA)");

		Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("pixel.png", html);
		Assert.Contains("alt", html);          // the alt text stays, the address does not
		Assert.DoesNotContain("tracker.example", html);
	}

	[Fact]
	public void AngleBracketAutolinks_OnlyForHttp()
	{
		var html = Html("<https://example.com> <javascript:alert(1)>");

		Assert.Contains("href=\"https://example.com\"", html);
		Assert.DoesNotContain("href=\"javascript", html, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void GenericAttributeSyntax_IsNotInterpreted()
	{
		var html = Html("text {onclick=\"x()\"}\n\n# Heading {#id .cls onmouseover=\"y()\"}");

		Assert.DoesNotContain("onclick=", html.Replace("{onclick=", string.Empty), StringComparison.Ordinal);
		Assert.DoesNotContain("<h1 id=", html);
		Assert.DoesNotContain("onmouseover=\"y()\">", html);
	}

	[Fact]
	public void CommonMarkdown_RendersCodeTablesAndEmphasis()
	{
		var html = Html("# T\n\n**bold** `code`\n\n```\nvar x = 1;\n```\n\n| a | b |\n|---|---|\n| 1 | 2 |\n");

		Assert.Contains("<h1", html);
		Assert.Contains("<strong>bold</strong>", html);
		Assert.Contains("<pre><code>", html);
		Assert.Contains("<table>", html);
	}

	[Fact]
	public void Render_BuildsHeaderRoleAndTruncationInfo()
	{
		var entry = new SessionEntryDto
		{
			Index = 7,
			Role = "assistant",
			Content = "hello",
			Truncated = true,
			FullLength = 5000,
			ModelId = "opus",
			Effort = "high",
			Timestamp = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero),
		};

		var view = _renderer.Render(entry);

		Assert.Equal(7, view.Index);
		Assert.Equal("A", view.Role);
		Assert.True(view.Truncated);
		Assert.Equal(5000, view.FullLength);
		Assert.Contains("opus · high", view.Timestamp);
		Assert.True(view.EstimatedHeight > 0);
	}

	[Fact]
	public void A_user_entry_header_is_the_local_date_and_the_time_to_the_minute()
	{
		var at = new DateTimeOffset(2026, 11, 14, 10, 5, 9, TimeSpan.Zero);

		var view = _renderer.Render(new SessionEntryDto { Index = 1, Role = "user", Content = "hi", Timestamp = at });

		Assert.Equal(at.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), view.Timestamp);
	}

	[Theory]
	[InlineData("user", "U")]
	[InlineData("assistant", "A")]
	[InlineData("system", "S")]
	[InlineData("compaction", "C")]
	[InlineData("weird", "S")]
	public void Render_MapsRolesToCodes(string role, string code)
	{
		var view = _renderer.Render(new SessionEntryDto { Role = role, Content = "x" });

		Assert.Equal(code, view.Role);
	}

	[Fact]
	public void HugeEntry_RendersWithoutThrowing()
	{
		var content = string.Join("\n", Enumerable.Repeat("| a | b | c |", 2000));

		var html = Html("| h1 | h2 | h3 |\n|---|---|---|\n" + content);

		Assert.Contains("<table>", html);
	}

	// FR.2.12 / TC.TR.09: "---" is a rule and "===" a double rule; the text above stays a paragraph.
	[Theory]
	[InlineData("text\n---", "<p>text</p>\n<hr>")]
	[InlineData("text\n===", "<p>text</p>\n<hr class=\"dbl\">")]
	[InlineData("===\ntext", "<hr class=\"dbl\">\n<p>text</p>")]
	[InlineData("---\ntext", "<hr>\n<p>text</p>")]
	[InlineData("text\n=====  ", "<p>text</p>\n<hr class=\"dbl\">")]
	[InlineData("text\n  ===", "<p>text</p>\n<hr class=\"dbl\">")]
	public void RuleLines_AreRulesNotSetextHeadings(string markdown, string expected)
	{
		var html = Html(markdown).Replace("\r\n", "\n").Trim();

		Assert.Equal(expected, html);
	}

	[Theory]
	[InlineData("==")]
	[InlineData("a ===")]
	[InlineData("=== x")]
	[InlineData("    ===")]
	public void NotARuleLine_StaysText(string markdown)
	{
		Assert.DoesNotContain("<hr", Html(markdown));
	}

	[Fact]
	public void UntouchedConstructs_KeepTheirMeaning()
	{
		Assert.Contains("<h1>Title</h1>", Html("# Title"));
		Assert.Contains("<h2>Sub</h2>", Html("## Sub"));
		Assert.Contains("<li>one</li>", Html("- one\n- two"));
		Assert.DoesNotContain("<hr", Html("- one\n- two"));

		var table = Html("| a | b |\n|---|---|\n| 1 | 2 |");
		Assert.Contains("<table>", table);
		Assert.DoesNotContain("<hr", table);

		var code = Html("```\n===\n---\n```");
		Assert.Contains("<pre>", code);
		Assert.DoesNotContain("<hr", code);
		Assert.Contains("===", code);

		Assert.DoesNotContain("<hr", Html("    ===\n    ---"));
	}

	[Fact]
	public void RuleLines_WorkInEveryRowKind()
	{
		foreach (var role in new[] { "user", "assistant", "system" })
		{
			var view = _renderer.Render(new SessionEntryDto { Index = 1, Role = role, Content = "one\n===\ntwo\n---\nthree", Timestamp = DateTimeOffset.UtcNow });

			Assert.Contains("<hr class=\"dbl\">", view.Html);
			Assert.Contains("<hr>", view.Html);
			Assert.DoesNotContain("<h1", view.Html);
			Assert.DoesNotContain("<h2", view.Html);
		}
	}
}
