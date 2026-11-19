using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Marks the local paths in blocks of a Focus card's document (FR.23.30): each occurrence of a detected path becomes a <see cref="FocusPathSpan"/> with a dotted
/// underline. Text inside a link is left alone (a link keeps its own behaviour); the longest path wins where two overlap.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusCardPathMarker
{
	private const double UnderlineThickness = 1;
	private static readonly double[] DotPattern = [1, 2];

	/// <summary>Marks every occurrence of <paramref name="paths"/> in <paramref name="block"/> (and its nested blocks); returns the spans it made.</summary>
	public IReadOnlyList<FocusPathSpan> Mark(Block block, IReadOnlyList<string> paths, Brush underline)
	{
		if (paths.Count == 0)
			return [];

		var ranges = new List<(TextPointer Start, TextPointer End, string Path)>();
		Collect(block.ContentStart, block.ContentEnd, paths, ranges);

		var spans = new List<FocusPathSpan>();
		for (var i = ranges.Count - 1; i >= 0; i--)   // the last first: wrapping an earlier range cannot move a later one
		{
			var (start, end, path) = ranges[i];
			var span = new FocusPathSpan(start, end, path) { TextDecorations = Underline(underline) };
			spans.Insert(0, span);
		}

		return spans;
	}

	private static TextDecorationCollection Underline(Brush brush)
	{
		var pen = new Pen(brush, UnderlineThickness) { DashStyle = new DashStyle(DotPattern, 0), DashCap = PenLineCap.Round };
		var decoration = new TextDecoration(TextDecorationLocation.Underline, pen, 0, TextDecorationUnit.FontRecommended, TextDecorationUnit.FontRecommended);
		return [decoration];
	}

	private static void Collect(TextPointer from, TextPointer to, IReadOnlyList<string> paths, List<(TextPointer, TextPointer, string)> ranges)
	{
		for (var p = from; p != null && p.CompareTo(to) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
		{
			if (p.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text || IsInside<Hyperlink>(p) || IsInside<FocusPathSpan>(p))
				continue;

			var text = p.GetTextInRun(LogicalDirection.Forward);
			foreach (var (index, length, path) in Matches(text, paths))
			{
				var start = p.GetPositionAtOffset(index)?.GetInsertionPosition(LogicalDirection.Forward);
				var end = p.GetPositionAtOffset(index + length)?.GetInsertionPosition(LogicalDirection.Backward);
				if (start != null && end != null && start.CompareTo(end) < 0)
					ranges.Add((start, end, path));
			}
		}
	}

	private static bool IsInside<T>(TextPointer pointer) where T : TextElement
	{
		for (var parent = pointer.Parent as DependencyObject; parent != null; parent = (parent as FrameworkContentElement)?.Parent)
		{
			if (parent is T)
				return true;
		}

		return false;
	}

	/// <summary>The non-overlapping occurrences in <paramref name="text"/>, earliest first (longest first at one place); an occurrence inside a longer word is no occurrence.</summary>
	private static List<(int Index, int Length, string Path)> Matches(string text, IReadOnlyList<string> paths)
	{
		var all = new List<(int Index, int Length, string Path)>();
		foreach (var path in paths)
		{
			for (var at = text.IndexOf(path, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(path, at + 1, StringComparison.OrdinalIgnoreCase))
			{
				if (IsWholeWord(text, at, path.Length))
					all.Add((at, path.Length, path));
			}
		}

		var chosen = new List<(int Index, int Length, string Path)>();
		var covered = 0;
		foreach (var match in all.OrderBy(m => m.Index).ThenByDescending(m => m.Length))
		{
			if (match.Index < covered)
				continue;

			chosen.Add(match);
			covered = match.Index + match.Length;
		}

		return chosen;
	}

	private static bool IsWholeWord(string text, int index, int length)
	{
		if (index > 0 && IsPathChar(text[index - 1]))
			return false;

		var after = index + length;
		return after >= text.Length || !char.IsLetterOrDigit(text[after]);
	}

	private static bool IsPathChar(char c) => char.IsLetterOrDigit(c) || c is '\\' or '/' or '_' or '-' or '.' or ':';
}
