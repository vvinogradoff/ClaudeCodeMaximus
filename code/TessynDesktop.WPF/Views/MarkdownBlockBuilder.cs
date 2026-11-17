using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TessynDesktop.WPF.Services;
using Block = Markdig.Syntax.Block;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Builds native WPF elements from untrusted markdown for the Focus cards. Nothing is HTML: raw HTML is disabled in the parser and
/// shown as literal text, images become the text "[image]", and a link is clickable only when <see cref="UrlPolicy"/> allows it
/// (http and https); the click is handed to the caller, never navigated here. Supports paragraphs, headings, bullet and numbered
/// lists, quotes, code blocks, bold, italic, inline code and links; anything else (tables, ...) falls back to its text.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class MarkdownBlockBuilder
{
	private const double BlockGap = 6;
	private const double ListIndent = 18;
	private const string CodeFont = "Consolas";

	private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UsePipeTables().UseEmphasisExtras().UseTaskLists().UseAutoLinks().Build();

	private readonly UrlPolicy _policy;
	private readonly Action<string> _onLink;

	public MarkdownBlockBuilder(UrlPolicy policy, Action<string> onLink)
	{
		_policy = policy;
		_onLink = onLink;
	}

	/// <summary>The blocks of <paramref name="markdown"/> as elements to stack vertically.</summary>
	public IReadOnlyList<UIElement> Build(string? markdown)
	{
		var result = new List<UIElement>();
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
			result.Add(new SelectableText { Text = markdown });
		}

		return result;
	}

	private void AddBlock(Block block, List<UIElement> into)
	{
		switch (block)
		{
			case HeadingBlock heading:
				into.Add(TextOf(heading.Inline, FontWeights.SemiBold, HeadingSize(heading.Level)));
				break;
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
				into.Add(CodeBox(string.Join("\n", code.Lines.Lines.Take(code.Lines.Count).Select(l => l.ToString()))));
				break;
			case ThematicBreakBlock:
			{
				var rule = new Border { Height = 1, Margin = new Thickness(0, 2, 0, BlockGap) };
				rule.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
				into.Add(rule);
				break;
			}

			case ContainerBlock container:
				foreach (var child in container)
					AddBlock(child, into);
				break;
			case LeafBlock { Inline: not null } leaf:
				into.Add(TextOf(leaf.Inline));
				break;
		}
	}

	private Border QuoteOf(QuoteBlock quote)
	{
		var inner = new StackPanel();
		var blocks = new List<UIElement>();
		foreach (var child in quote)
			AddBlock(child, blocks);
		foreach (var element in blocks)
			inner.Children.Add(element);

		var border = new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 0, 0, 0), Child = inner };
		border.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
		return border;
	}

	private StackPanel ListOf(ListBlock list)
	{
		var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
		var panel = new StackPanel { Margin = new Thickness(0, 0, 0, BlockGap) };
		foreach (var item in list.OfType<ListItemBlock>())
		{
			var row = new Grid();
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ListIndent) });
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			var marker = new TextBlock { Text = list.IsOrdered ? $"{number++}." : "•" };
			var content = new StackPanel();
			var blocks = new List<UIElement>();
			foreach (var child in item)
				AddBlock(child, blocks);
			foreach (var element in blocks)
				content.Children.Add(element);
			Grid.SetColumn(content, 1);
			row.Children.Add(marker);
			row.Children.Add(content);
			panel.Children.Add(row);
		}

		return panel;
	}

	private SelectableText TextOf(ContainerInline? inline, FontWeight? fontWeight = null, double? fontSize = null)
	{
		var text = new SelectableText { Margin = new Thickness(0, 0, 0, BlockGap) };
		if (fontWeight != null)
			text.FontWeight = fontWeight.Value;
		if (fontSize != null)
			text.FontSize = fontSize.Value;
		AddInlines(text.Inlines, inline);
		return text;
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

	private static Border CodeBox(string code)
	{
		var text = new TextBox { Text = code, FontFamily = new FontFamily(CodeFont), IsReadOnly = true, BorderThickness = new Thickness(0), Padding = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, TextWrapping = TextWrapping.NoWrap };
		var scroll = new ScrollViewer { Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
		var border = new Border { Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(4), Child = scroll, Margin = new Thickness(0, 0, 0, BlockGap) };
		border.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
		return border;
	}

	private static double HeadingSize(int level) => level switch { 1 => 20, 2 => 17, _ => 15 };
}
