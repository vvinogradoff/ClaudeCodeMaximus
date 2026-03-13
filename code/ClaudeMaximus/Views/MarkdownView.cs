using System;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Tables;
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

	// Light-theme code colours — always readable regardless of app accent theme
	private static readonly IBrush CodeBlockBackground  = new SolidColorBrush(Color.FromRgb(245, 245, 245));
	private static readonly IBrush CodeBlockForeground  = new SolidColorBrush(Color.FromRgb(32,  32,  32));
	private static readonly IBrush InlineCodeBackground = new SolidColorBrush(Color.FromRgb(232, 232, 232));
	private static readonly IBrush InlineCodeForeground = new SolidColorBrush(Color.FromRgb(32,  32,  32));
	private static readonly IBrush QuoteBorderBrush     = new SolidColorBrush(Color.FromRgb(80,  100, 130));

	public string? Markdown
	{
		get => GetValue(MarkdownProperty);
		set => SetValue(MarkdownProperty, value);
	}

	static MarkdownView()
	{
		MarkdownProperty.Changed.AddClassHandler<MarkdownView>((v, _) => v.Rebuild());
	}

	// Rebuild when FontSize changes so all text scales with Ctrl+scroll
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property.Name == "FontSize")
			Rebuild();
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

	private Control BuildBlock(Block block) => block switch
	{
		HeadingBlock h        => BuildHeading(h),
		FencedCodeBlock code  => BuildCodeBlock(code),
		CodeBlock code        => BuildCodeBlock(code),
		QuoteBlock quote      => BuildQuote(quote),
		ListBlock list        => BuildList(list),
		Table table           => BuildTable(table),
		ThematicBreakBlock    => new Separator { Margin = new Thickness(0, 4) },
		ParagraphBlock p      => BuildParagraph(p),
		LinkReferenceDefinitionGroup => new Panel(),
		_                     => new TextBlock { Text = block.ToString(), TextWrapping = TextWrapping.Wrap, FontSize = FontSize },
	};

	private Control BuildHeading(HeadingBlock h)
	{
		var baseSize = FontSize;
		var (size, weight) = h.Level switch
		{
			1 => (baseSize + 7, FontWeight.Bold),
			2 => (baseSize + 4, FontWeight.Bold),
			3 => (baseSize + 2, FontWeight.SemiBold),
			_ => (baseSize,     FontWeight.SemiBold),
		};

		var tb = new TextBlock
		{
			FontSize     = size,
			FontWeight   = weight,
			TextWrapping = TextWrapping.Wrap,
			Margin       = new Thickness(0, h.Level == 1 ? 8 : 4, 0, 2),
		};

		if (h.Inline != null)
			foreach (var inline in h.Inline)
				AppendInline(tb.Inlines!, inline);

		return tb;
	}

	private Control BuildCodeBlock(LeafBlock code)
	{
		var text     = code.Lines.ToString().TrimEnd();
		var fontSize = Math.Max(FontSize - 1, 10);
		return new Border
		{
			Background   = CodeBlockBackground,
			CornerRadius = new CornerRadius(4),
			Padding      = new Thickness(10, 8),
			Margin       = new Thickness(0, 2),
			Child        = new SelectableTextBlock
			{
				Text         = text,
				TextWrapping = TextWrapping.NoWrap,
				FontFamily   = new FontFamily("Cascadia Code,Consolas,monospace"),
				FontSize     = fontSize,
				Foreground   = CodeBlockForeground,
			},
		};
	}

	private Control BuildQuote(QuoteBlock quote)
	{
		var inner = new StackPanel { Spacing = 4 };
		foreach (var b in quote)
			inner.Children.Add(BuildBlock(b));

		return new Border
		{
			BorderBrush     = QuoteBorderBrush,
			BorderThickness = new Thickness(3, 0, 0, 0),
			Padding         = new Thickness(10, 4, 4, 4),
			Child           = inner,
		};
	}

	private Control BuildList(ListBlock list)
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
				FontSize            = FontSize,
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

	private Control BuildParagraph(ParagraphBlock p)
	{
		var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = FontSize };

		if (p.Inline != null)
			foreach (var inline in p.Inline)
				AppendInline(tb.Inlines!, inline);

		return tb;
	}

	private static readonly IBrush TableHeaderBackground = new SolidColorBrush(Color.FromRgb(240, 240, 240));
	private static readonly IBrush TableBorderBrush      = new SolidColorBrush(Color.FromRgb(210, 210, 210));

	private Control BuildTable(Table table)
	{
		// Collect rows
		var rows = new System.Collections.Generic.List<TableRow>();
		foreach (var b in table)
			if (b is TableRow tr) rows.Add(tr);

		if (rows.Count == 0) return new Panel();

		int colCount = 0;
		foreach (var r in rows)
			if (r.Count > colCount) colCount = r.Count;

		var grid = new Grid();
		for (var c = 0; c < colCount; c++)
			grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

		for (var rowIdx = 0; rowIdx < rows.Count; rowIdx++)
		{
			grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			var row = rows[rowIdx];

			for (var colIdx = 0; colIdx < row.Count && colIdx < colCount; colIdx++)
			{
				if (row[colIdx] is not TableCell cell) continue;

				// Build cell text from its paragraph children
				var tb = new SelectableTextBlock
				{
					TextWrapping = TextWrapping.Wrap,
					FontSize     = FontSize,
				};
				if (row.IsHeader) tb.FontWeight = FontWeight.SemiBold;

				foreach (var b in cell)
					if (b is ParagraphBlock p && p.Inline != null)
						foreach (var inl in p.Inline)
							AppendInline(tb.Inlines!, inl);

				var cellBorder = new Border
				{
					Padding         = new Thickness(8, 5),
					BorderBrush     = TableBorderBrush,
					// Each cell draws its right and bottom border;
					// outer wrapper draws the top and left.
					BorderThickness = new Thickness(0, 0, 1, 1),
					Child           = tb,
				};
				if (row.IsHeader)
					cellBorder.Background = TableHeaderBackground;

				Grid.SetRow(cellBorder, rowIdx);
				Grid.SetColumn(cellBorder, colIdx);
				grid.Children.Add(cellBorder);
			}
		}

		// Outer border provides the top and left edges of the table
		return new Border
		{
			BorderBrush     = TableBorderBrush,
			BorderThickness = new Thickness(1, 1, 0, 0),
			Margin          = new Thickness(0, 4),
			Child           = grid,
		};
	}

	// ── Inline-level ─────────────────────────────────────────────────────────

	private void AppendInline(InlineCollection col, Markdig.Syntax.Inlines.Inline inline)
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
					Text       = code.Content,
					FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
					Background = InlineCodeBackground,
					Foreground = InlineCodeForeground,
					FontSize   = Math.Max(FontSize - 1, 10),
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
