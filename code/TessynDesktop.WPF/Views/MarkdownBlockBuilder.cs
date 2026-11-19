using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TessynDesktop.WPF.Services;
using MdBlock = Markdig.Syntax.Block;
using WBlock = System.Windows.Documents.Block;
using WList = System.Windows.Documents.List;
using WTable = System.Windows.Documents.Table;
using WTableCell = System.Windows.Documents.TableCell;
using WTableRow = System.Windows.Documents.TableRow;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Builds the flow-document blocks of a Focus card body from untrusted markdown (FR.23.25: the blocks go into the card's one document, so a selection runs
/// across all of them). Nothing is HTML: raw HTML is disabled in the parser and shown as literal text, images become the text "[image]", and a link is
/// clickable only when <see cref="UrlPolicy"/> allows it (http and https); the click is handed to the caller, never navigated here. Supports paragraphs,
/// headings, bullet and numbered lists, quotes, code blocks, tables, bold, italic, inline code and links.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class MarkdownBlockBuilder
{
	private const double BlockGap = 6;
	private const double ListIndent = 18;
	private const double TableCellPadding = 4;
	private const string CodeFont = "Consolas";

	private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UsePipeTables().UseEmphasisExtras().UseTaskLists().UseAutoLinks().Build();

	private readonly UrlPolicy _policy;
	private readonly Action<string> _onLink;

	public MarkdownBlockBuilder(UrlPolicy policy, Action<string> onLink)
	{
		_policy = policy;
		_onLink = onLink;
	}

	/// <summary>The blocks of <paramref name="markdown"/>, to add to a <see cref="FlowDocument"/> in order.</summary>
	public IReadOnlyList<WBlock> Build(string? markdown)
	{
		var result = new List<WBlock>();
		if (string.IsNullOrWhiteSpace(markdown))
			return result;

		if (markdown.Length > Constants.Limits.MaxMarkdownChars)
			markdown = markdown[..Constants.Limits.MaxMarkdownChars] + "…";

		try
		{
			foreach (var block in Markdown.Parse(markdown, Pipeline))
				AddBlock(block, result);
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InsufficientExecutionStackException or NotSupportedException)
		{
			// Pathological input (for example thousands of nested quotes) must not make the card body vanish: show the text as it is (V6).
			result.Clear();
			result.Add(new Paragraph(new Run(markdown)) { Margin = new Thickness(0, 0, 0, BlockGap) });
		}

		return result;
	}

	private void AddBlock(MdBlock block, List<WBlock> into)
	{
		switch (block)
		{
			case HeadingBlock heading:
			{
				var paragraph = TextOf(heading.Inline);
				paragraph.FontWeight = FontWeights.SemiBold;
				paragraph.FontSize = HeadingSize(heading.Level);
				into.Add(paragraph);
				break;
			}

			case ParagraphBlock paragraph:
				into.Add(TextOf(paragraph.Inline));
				break;
			case ListBlock list:
				into.Add(ListOf(list));
				break;
			case QuoteBlock quote:
				into.Add(QuoteOf(quote));
				break;
			case CodeBlock code:
				into.Add(CodeOf(string.Join("\n", code.Lines.Lines.Take(code.Lines.Count).Select(l => l.ToString()))));
				break;
			case ThematicBreakBlock:
			{
				var rule = new Paragraph { FontSize = 2, Margin = new Thickness(0, 2, 0, BlockGap), BorderThickness = new Thickness(0, 0, 0, 1) };
				rule.SetResourceReference(WBlock.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
				into.Add(rule);
				break;
			}

			case Markdig.Extensions.Tables.Table table:
				into.Add(TableOf(table));
				break;
			case ContainerBlock container:
				foreach (var child in container)
					AddBlock(child, into);
				break;
			case LeafBlock { Inline: not null } leaf:
				into.Add(TextOf(leaf.Inline));
				break;
		}
	}

	private Section QuoteOf(QuoteBlock quote)
	{
		var section = new Section { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 0, 0, 0) };
		section.SetResourceReference(WBlock.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
		var blocks = new List<WBlock>();
		foreach (var child in quote)
			AddBlock(child, blocks);
		foreach (var block in blocks)
			section.Blocks.Add(block);
		return section;
	}

	private WList ListOf(ListBlock list)
	{
		var result = new WList
		{
			MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
			MarkerOffset = 4,
			Padding = new Thickness(ListIndent, 0, 0, 0),
			Margin = new Thickness(0, 0, 0, BlockGap),
		};
		if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start))
			result.StartIndex = start;

		foreach (var item in list.OfType<ListItemBlock>())
		{
			var blocks = new List<WBlock>();
			foreach (var child in item)
				AddBlock(child, blocks);
			if (blocks.Count == 0)
				blocks.Add(new Paragraph());
			var listItem = new ListItem();
			foreach (var block in blocks)
				listItem.Blocks.Add(block);
			result.ListItems.Add(listItem);
		}

		return result;
	}

	private WTable TableOf(Markdig.Extensions.Tables.Table table)
	{
		var result = new WTable { CellSpacing = 0, Margin = new Thickness(0, 0, 0, BlockGap) };
		var group = new TableRowGroup();
		result.RowGroups.Add(group);
		var columns = 0;
		var texts = new List<IReadOnlyList<string>>();
		foreach (var row in table.OfType<Markdig.Extensions.Tables.TableRow>())
		{
			var wrow = new WTableRow();
			var rowTexts = new List<string>();
			texts.Add(rowTexts);
			foreach (var cell in row.OfType<Markdig.Extensions.Tables.TableCell>())
			{
				rowTexts.Add(PlainTextOf(cell));
				var blocks = new List<WBlock>();
				foreach (var child in cell)
					AddBlock(child, blocks);
				if (blocks.Count == 0)
					blocks.Add(new Paragraph());
				var wcell = new WTableCell { Padding = new Thickness(TableCellPadding), BorderThickness = new Thickness(1) };
				wcell.SetResourceReference(WTableCell.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
				foreach (var block in blocks)
				{
					block.Margin = new Thickness(0);
					if (row.IsHeader)
						block.FontWeight = FontWeights.SemiBold;
					wcell.Blocks.Add(block);
				}

				wrow.Cells.Add(wcell);
			}

			columns = Math.Max(columns, wrow.Cells.Count);
			group.Rows.Add(wrow);
		}

		// Column widths follow the content (FR.2.14): a star weight per column from its text.
		var weights = new TableColumnWeights().Compute(texts);
		for (var i = 0; i < columns; i++)
			result.Columns.Add(new TableColumn { Width = new GridLength(i < weights.Count ? weights[i] : TableColumnWeights.FloorWeight, GridUnitType.Star) });
		return result;
	}

	/// <summary>The plain text of a table cell (literal and code text of its inlines), for the column weights.</summary>
	private static string PlainTextOf(ContainerBlock cell)
	{
		var text = new System.Text.StringBuilder();
		foreach (var leaf in cell.Descendants().OfType<LeafBlock>())
		{
			if (leaf.Inline == null)
				continue;

			foreach (var inline in leaf.Inline.Descendants())
			{
				switch (inline)
				{
					case LiteralInline literal:
						text.Append(literal.Content.ToString());
						break;
					case CodeInline code:
						text.Append(code.Content);
						break;
					case LineBreakInline:
						text.Append(' ');
						break;
				}
			}

			text.Append(' ');
		}

		return text.ToString();
	}

	private Paragraph TextOf(ContainerInline? inline)
	{
		var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, BlockGap) };
		AddInlines(paragraph.Inlines, inline);
		return paragraph;
	}

	private void AddInlines(InlineCollection target, ContainerInline? container)
	{
		if (container == null)
			return;

		foreach (var inline in container)
		{
			switch (inline)
			{
				case LiteralInline literal:
					target.Add(new Run(literal.Content.ToString()));
					break;
				case CodeInline code:
				{
					var run = new Run(code.Content) { FontFamily = new FontFamily(CodeFont) };
					run.SetResourceReference(TextElement.BackgroundProperty, "ControlFillColorSecondaryBrush");
					target.Add(run);
					break;
				}

				case LineBreakInline lineBreak:
					target.Add(lineBreak.IsHard ? new LineBreak() : new Run(" "));
					break;
				case EmphasisInline emphasis:
				{
					Span span = emphasis.DelimiterChar == '~' ? new Span { TextDecorations = TextDecorations.Strikethrough }
						: emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
					AddInlines(span.Inlines, emphasis);
					target.Add(span);
					break;
				}

				case LinkInline { IsImage: true } image:
				{
					var alt = string.Concat(image.Descendants<LiteralInline>().Select(l => l.Content.ToString())).Trim();
					target.Add(new Run(alt.Length > 0 ? alt : "[image]"));
					break;
				}

				case LinkInline link:
					AddLink(target, link.Url, link, fallbackText: null);
					break;
				case AutolinkInline auto:
					AddLink(target, auto.Url, link: null, fallbackText: auto.Url);
					break;
				case ContainerInline nested:
					AddInlines(target, nested);
					break;
				default:
					target.Add(new Run(inline.ToString() ?? string.Empty));
					break;
			}
		}
	}

	private void AddLink(InlineCollection target, string? url, LinkInline? link, string? fallbackText)
	{
		if (!_policy.TryParse(url, out var uri))
		{
			// Not an http(s) link: show its text, inert.
			if (link != null)
				AddInlines(target, link);
			else
				target.Add(new Run(fallbackText ?? string.Empty));
			return;
		}

		var hyperlink = new Hyperlink { ToolTip = uri.AbsoluteUri };
		hyperlink.Click += (_, _) => _onLink(uri.AbsoluteUri);
		target.Add(hyperlink);
		if (link != null && link.FirstChild != null)
			AddInlines(hyperlink.Inlines, link);
		else
			hyperlink.Inlines.Add(new Run(fallbackText ?? url ?? uri.AbsoluteUri));
	}

	/// <summary>A code block is a monospaced paragraph on a tinted background, one line break per source line; it is part of the card's selection like any text.</summary>
	private static Paragraph CodeOf(string code)
	{
		var paragraph = new Paragraph { FontFamily = new FontFamily(CodeFont), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, BlockGap) };
		paragraph.SetResourceReference(TextElement.BackgroundProperty, "ControlFillColorSecondaryBrush");
		var lines = code.Split('\n');
		for (var i = 0; i < lines.Length; i++)
		{
			if (i > 0)
				paragraph.Inlines.Add(new LineBreak());
			paragraph.Inlines.Add(new Run(lines[i]));
		}

		return paragraph;
	}

	private static double HeadingSize(int level) => level switch { 1 => 20, 2 => 17, _ => 15 };
}
