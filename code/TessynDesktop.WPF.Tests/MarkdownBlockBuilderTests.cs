using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The native markdown of Focus cards: no HTML ever, images inert, only http(s) links clickable.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class MarkdownBlockBuilderTests
{
	private static IReadOnlyList<UIElement> Build(string markdown, List<string> clicks)
	{
		IReadOnlyList<UIElement> result = [];
		StaTestRunner.Run(() => result = new MarkdownBlockBuilder(new UrlPolicy(), clicks.Add).Build(markdown));
		return result;
	}

	private static string TextOf(UIElement element) => element switch
	{
		TextBlock block => new TextRange(block.ContentStart ?? throw new InvalidOperationException(), block.ContentEnd).Text,
		Panel panel => string.Concat(panel.Children.Cast<UIElement>().Select(TextOf)),
		Border { Child: { } child } => TextOf(child),
		ScrollViewer { Content: UIElement content } => TextOf(content),
		_ => string.Empty,
	};

	[Fact]
	public void EmptyOrBlank_GivesNoElements()
	{
		var clicks = new List<string>();
		Assert.Empty(Build("", clicks));
		Assert.Empty(Build("   \n ", clicks));
	}

	[Fact]
	public void RawHtml_IsShownAsLiteralText_NeverInterpreted()
	{
		var texts = new List<string>();
		StaTestRunner.Run(() =>
		{
			var blocks = new MarkdownBlockBuilder(new UrlPolicy(), _ => { }).Build("Hello <b onclick=\"x()\">bold</b> <script>alert(1)</script>\n\n<div>block</div>");
			texts.AddRange(blocks.Select(TextOf));
		});

		var all = string.Concat(texts);
		Assert.Contains("<script>alert(1)</script>", all);
		Assert.Contains("<b onclick=\"x()\">", all);
		Assert.Contains("<div>block</div>", all);
	}

	[Fact]
	public void Images_BecomeText_AndLinksAreClickableOnlyForHttp()
	{
		var clicks = new List<string>();
		string text = string.Empty;
		var hyperlinks = new List<(string Text, string? Tooltip)>();
		StaTestRunner.Run(() =>
		{
			var blocks = new MarkdownBlockBuilder(new UrlPolicy(), clicks.Add).Build(
				"![pic](https://evil.example/x.png) [ok](https://example.com/a) [bad](javascript:alert(1)) [file](file:///C:/Windows/System32/cmd.exe) <https://auto.example/z>");
			text = string.Concat(blocks.Select(TextOf));
			foreach (var tb in blocks.OfType<TextBlock>())
				foreach (var link in Descendants(tb.Inlines).OfType<Hyperlink>())
				{
					hyperlinks.Add((string.Concat(Descendants(link.Inlines).OfType<Run>().Select(r => r.Text)), link.ToolTip as string));
					link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
				}
		});

		Assert.Contains("pic", text);          // the alt text stays
		Assert.DoesNotContain("evil.example", text);
		Assert.Equal(["ok", "https://auto.example/z"], hyperlinks.Select(h => h.Text));
		Assert.Equal(["https://example.com/a", "https://auto.example/z"], clicks);
		Assert.Contains("bad", text);          // the text of a refused link stays, inert
		Assert.Contains("file", text);
	}

	[Fact]
	public void HeadingsListsQuotesAndCode_AreBuilt()
	{
		var all = string.Empty;
		var count = 0;
		StaTestRunner.Run(() =>
		{
			var elements = new MarkdownBlockBuilder(new UrlPolicy(), _ => { })
				.Build("# Head\n\n- one\n- two\n\n1. first\n2. second\n\n> quoted\n\n```\ncode line\n```\n\nPlain with `inline` and **bold**.");
			all = string.Concat(elements.Select(TextOf));
			count = elements.Count;
		});

		foreach (var expected in new[] { "Head", "one", "two", "first", "second", "quoted", "code line", "inline", "bold" })
			Assert.Contains(expected, all);
		Assert.True(count >= 6);
	}

	private static IEnumerable<Inline> Descendants(InlineCollection inlines)
	{
		foreach (var inline in inlines)
		{
			yield return inline;
			if (inline is Span span)
				foreach (var child in Descendants(span.Inlines))
					yield return child;
		}
	}
}
