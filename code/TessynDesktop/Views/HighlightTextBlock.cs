using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace TessynDesktop.Views;

/// <summary>
/// A SelectableTextBlock that highlights occurrences of a search term with a yellow background.
/// When IsCurrentMatch is true, uses orange background instead to distinguish the current match.
/// When HighlightTerm is null or empty, it displays plain text as normal.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class HighlightTextBlock : SelectableTextBlock
{
	public static readonly StyledProperty<string?> HighlightTermProperty =
		AvaloniaProperty.Register<HighlightTextBlock, string?>(nameof(HighlightTerm));

	public static readonly StyledProperty<bool> IsCurrentMatchProperty =
		AvaloniaProperty.Register<HighlightTextBlock, bool>(nameof(IsCurrentMatch));

	private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 0));
	private static readonly IBrush CurrentMatchBrush = new SolidColorBrush(Color.FromArgb(220, 255, 165, 0));
	private static readonly IBrush SelectionHighlightBrush = new SolidColorBrush(Color.FromArgb(128, 0, 120, 215));

	public string? HighlightTerm
	{
		get => GetValue(HighlightTermProperty);
		set => SetValue(HighlightTermProperty, value);
	}

	public bool IsCurrentMatch
	{
		get => GetValue(IsCurrentMatchProperty);
		set => SetValue(IsCurrentMatchProperty, value);
	}

	static HighlightTextBlock()
	{
		CursorProperty.OverrideDefaultValue<HighlightTextBlock>(new Cursor(StandardCursorType.Ibeam));
		SelectionBrushProperty.OverrideDefaultValue<HighlightTextBlock>(SelectionHighlightBrush);
		HighlightTermProperty.Changed.AddClassHandler<HighlightTextBlock>((tb, _) => tb.RebuildInlines());
		TextProperty.Changed.AddClassHandler<HighlightTextBlock>((tb, _) => tb.RebuildInlines());
		IsCurrentMatchProperty.Changed.AddClassHandler<HighlightTextBlock>((tb, _) => tb.RebuildInlines());
	}

	private void RebuildInlines()
	{
		var text = Text;
		var term = HighlightTerm;

		if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
			return; // Let the base class render via the normal Text property

		// Clear existing inlines and build highlighted ones
		Inlines?.Clear();
		if (Inlines == null)
			return;

		var brush = IsCurrentMatch ? CurrentMatchBrush : HighlightBrush;
		BuildHighlightedInlines(Inlines, text, term, brush);
	}

	internal static void BuildHighlightedInlines(InlineCollection inlines, string text, string term, IBrush? highlightBrush = null)
	{
		highlightBrush ??= HighlightBrush;

		var pos = 0;
		while (pos < text.Length)
		{
			var matchIndex = text.IndexOf(term, pos, StringComparison.OrdinalIgnoreCase);
			if (matchIndex < 0)
			{
				// No more matches — add remaining text
				inlines.Add(new Run { Text = text.Substring(pos) });
				break;
			}

			// Add text before match
			if (matchIndex > pos)
				inlines.Add(new Run { Text = text.Substring(pos, matchIndex - pos) });

			// Add highlighted match
			inlines.Add(new Run
			{
				Text = text.Substring(matchIndex, term.Length),
				Background = highlightBrush,
			});

			pos = matchIndex + term.Length;
		}
	}

	/// <summary>The default yellow highlight brush for non-current matches.</summary>
	internal static IBrush DefaultHighlightBrush => HighlightBrush;

	/// <summary>The orange highlight brush for the current match.</summary>
	internal static IBrush DefaultCurrentMatchBrush => CurrentMatchBrush;
}
