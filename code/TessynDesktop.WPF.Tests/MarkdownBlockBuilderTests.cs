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
	private static IReadOnlyList<Block> Build(string markdown, List<string> clicks)
	{
		IReadOnlyList<Block> result = [];
		StaTestRunner.Run(() => result = new MarkdownBlockBuilder(new UrlPolicy(), clicks.Add).Build(markdown));
		return result;
	}

	private static string TextOf(Block block) => new TextRange(block.ContentStart, block.ContentEnd).Text;

	private static IEnumerable<Paragraph> Paragraphs(IEnumerable<Block> blocks)
	{
		foreach (var block in blocks)
			switch (block)
			{
				case Paragraph p:
					yield return p;
					break;
				case Section s:
					foreach (var inner in Paragraphs(s.Blocks))
						yield return inner;
					break;
				case List l:
					foreach (var inner in l.ListItems.SelectMany(i => Paragraphs(i.Blocks)))
						yield return inner;
					break;
				case Table t:
					foreach (var inner in t.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells).SelectMany(c => Paragraphs(c.Blocks)))
						yield return inner;
					break;
			}
	}

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
			foreach (var tb in Paragraphs(blocks))
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
		bool hasBullets = false, hasNumbers = false, hasQuote = false;
		IReadOnlyList<Block> blocks = [];
		StaTestRunner.Run(() =>
		{
			blocks = new MarkdownBlockBuilder(new UrlPolicy(), _ => { })
				.Build("# Head\n\n- one\n- two\n\n1. first\n2. second\n\n> quoted\n\n```\ncode line\n```\n\nPlain with `inline` and **bold**.");
			all = string.Concat(blocks.Select(TextOf));
			count = blocks.Count;
			hasBullets = blocks.Any(b => b is List { MarkerStyle: TextMarkerStyle.Disc });
			hasNumbers = blocks.Any(b => b is List { MarkerStyle: TextMarkerStyle.Decimal });
			hasQuote = blocks.Any(b => b is Section);
		});

		foreach (var expected in new[] { "Head", "one", "two", "first", "second", "quoted", "code line", "inline", "bold" })
			Assert.Contains(expected, all);
		Assert.True(count >= 6);
		Assert.True(hasBullets);
		Assert.True(hasNumbers);
		Assert.True(hasQuote);
	}

	[Fact]
	public void ATable_BecomesATableOfCells_WithTheHeaderRowBold()
	{
		var columns = 0;
		var rowCount = 0;
		var cells = new List<string>();
		var headerWeight = FontWeights.Normal;
		var tables = 0;
		StaTestRunner.Run(() =>
		{
			var blocks = new MarkdownBlockBuilder(new UrlPolicy(), _ => { }).Build("| Name | Value |\n|---|---|\n| cell-a | cell-b |");
			tables = blocks.OfType<Table>().Count();
			var table = (Table)blocks.Single();
			columns = table.Columns.Count;
			var rows = table.RowGroups.Single().Rows;
			rowCount = rows.Count;
			cells.AddRange(rows[1].Cells.Select(c => TextOf(c.Blocks.Single()).Trim()));
			headerWeight = rows[0].Cells[0].Blocks.Single().FontWeight;
		});

		Assert.Equal(1, tables);
		Assert.Equal(2, columns);
		Assert.Equal(2, rowCount);
		Assert.Equal(["cell-a", "cell-b"], cells);
		Assert.Equal(FontWeights.SemiBold, headerWeight);
	}

	[Fact]
	public void ATable_GivesEachColumnAStarWeightFromItsText()
	{
		var widths = new List<GridLength>();
		StaTestRunner.Run(() =>
		{
			var blocks = new MarkdownBlockBuilder(new UrlPolicy(), _ => { }).Build("| # | Notes |\n|---|---|\n| 1 | a long sentence of ordinary words that wraps in its cell |");
			widths.AddRange(((Table)blocks.Single()).Columns.Select(c => c.Width));
		});

		Assert.Equal(2, widths.Count);
		Assert.All(widths, w => Assert.True(w.IsStar));
		Assert.True(widths[1].Value > widths[0].Value);
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
