using System;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ClaudeMaximus.Views;

/// <summary>
/// Renders a markdown string using Markdig for parsing and native Avalonia
/// controls for display. Supports headings, paragraphs, bold/italic, inline
/// code, fenced code blocks, and unordered/ordered lists.
/// Does NOT use Markdown.Avalonia (incompatible with Avalonia 11.3.x).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class MarkdownView : ContentControl
{
	public static readonly StyledProperty<string?> MarkdownProperty =
		AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

	private static readonly MarkdownPipeline Pipeline =
		new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

	public string? Markdown
	{
		get => GetValue(MarkdownProperty);
		set => SetValue(MarkdownProperty, value);
	}

	static MarkdownView()
	{
		MarkdownProperty.Changed.AddClassHandler<MarkdownView>((v, _) => v.Rebuild());
	}

	private void Rebuild()
	{
		var text = Markdown;
		if (string.IsNullOrWhiteSpace(text))
		{
			Content = null;
			return;
		}

		var doc   = Markdig.Markdown.Parse(text, Pipeline);
		var panel = new StackPanel { Spacing = 6 };

		foreach (var block in doc)
			panel.Children.Add(BuildBlock(block));

		Content = panel;
	}

	// ── Block-level ──────────────────────────────────────────────────────────

	private static Control BuildBlock(Block block) => block switch
	{
		HeadingBlock h        => BuildHeading(h),
		FencedCodeBlock code  => BuildCodeBlock(code),
		CodeBlock code        => BuildCodeBlock(code),
		QuoteBlock quote      => BuildQuote(quote),
		ListBlock list        => BuildList(list),
		ThematicBreakBlock    => new Separator { Margin = new Thickness(0, 4) },
		ParagraphBlock p      => BuildParagraph(p),
		_                     => new TextBlock { Text = block.ToString(), TextWrapping = TextWrapping.Wrap },
	};

	private static Control BuildHeading(HeadingBlock h)
	{
		var (size, weight) = h.Level switch
		{
			1 => (20.0, FontWeight.Bold),
			2 => (17.0, FontWeight.Bold),
			3 => (15.0, FontWeight.SemiBold),
			_ => (13.0, FontWeight.SemiBold),
		};

		var tb = new TextBlock
		{
			FontSize   = size,
			FontWeight = weight,
			TextWrapping = TextWrapping.Wrap,
			Margin     = new Thickness(0, h.Level == 1 ? 8 : 4, 0, 2),
		};

		if (h.Inline != null)
			foreach (var inline in h.Inline)
				AppendInline(tb.Inlines!, inline);

		return tb;
	}

	private static Control BuildCodeBlock(LeafBlock code)
	{
		var text = code.Lines.ToString().TrimEnd();
		return new Border
		{
			Background    = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
			CornerRadius  = new CornerRadius(4),
			Padding       = new Thickness(10, 8),
			Margin        = new Thickness(0, 2),
			Child         = new SelectableTextBlock
			{
				Text        = text,
				TextWrapping = TextWrapping.NoWrap,
				FontFamily  = new FontFamily("Cascadia Code,Consolas,monospace"),
				FontSize    = 12,
			},
		};
	}

	private static Control BuildQuote(QuoteBlock quote)
	{
		var inner = new StackPanel { Spacing = 4 };
		foreach (var b in quote)
			inner.Children.Add(BuildBlock(b));

		return new Border
		{
			BorderBrush     = new SolidColorBrush(Color.FromRgb(80, 100, 130)),
			BorderThickness = new Thickness(3, 0, 0, 0),
			Padding         = new Thickness(10, 4, 4, 4),
			Child           = inner,
		};
	}

	private static Control BuildList(ListBlock list)
	{
		var panel  = new StackPanel { Spacing = 3 };
		var number = 1;

		foreach (var item in list)
		{
			if (item is not ListItemBlock li) continue;

			var bullet = list.IsOrdered ? $"{number++}." : "•";
			var row    = new Grid();
			row.ColumnDefinitions.Add(new ColumnDefinition(22, GridUnitType.Pixel));
			row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

			var bulletBlock = new TextBlock
			{
				Text                = bullet,
				VerticalAlignment   = VerticalAlignment.Top,
				HorizontalAlignment = HorizontalAlignment.Right,
				Margin              = new Thickness(0, 0, 6, 0),
			};

			var contentPanel = new StackPanel { Spacing = 3 };
			foreach (var b in li)
				contentPanel.Children.Add(BuildBlock(b));

			Grid.SetColumn(bulletBlock,  0);
			Grid.SetColumn(contentPanel, 1);
			row.Children.Add(bulletBlock);
			row.Children.Add(contentPanel);
			panel.Children.Add(row);
		}

		return panel;
	}

	private static Control BuildParagraph(ParagraphBlock p)
	{
		var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };

		if (p.Inline != null)
			foreach (var inline in p.Inline)
				AppendInline(tb.Inlines!, inline);

		return tb;
	}

	// ── Inline-level ─────────────────────────────────────────────────────────

	private static void AppendInline(InlineCollection col, Markdig.Syntax.Inlines.Inline inline)
	{
		switch (inline)
		{
			case LiteralInline lit:
				col.Add(new Run { Text = lit.Content.ToString() });
				break;

			case EmphasisInline em:
				var span = new Span();
				foreach (var child in em)
					AppendInline(span.Inlines, child);

				if (em.DelimiterCount == 2)
					span.FontWeight = FontWeight.Bold;
				else
					span.FontStyle = FontStyle.Italic;

				col.Add(span);
				break;

			case CodeInline code:
				col.Add(new Run
				{
					Text        = code.Content,
					FontFamily  = new FontFamily("Cascadia Code,Consolas,monospace"),
					Background  = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
					FontSize    = 12,
				});
				break;

			case LineBreakInline lb:
				col.Add(lb.IsHard ? new LineBreak() : new Run { Text = " " });
				break;

			case LinkInline link:
				// Render link text without making it clickable for now
				var linkSpan = new Span { TextDecorations = TextDecorations.Underline };
				foreach (var child in link)
					AppendInline(linkSpan.Inlines, child);
				col.Add(linkSpan);
				break;

			case ContainerInline container:
				foreach (var child in container)
					AppendInline(col, child);
				break;

			default:
				// Fallback: extract raw text via ToString
				var raw = inline.ToString();
				if (!string.IsNullOrEmpty(raw))
					col.Add(new Run { Text = raw });
				break;
		}
	}
}
