using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The text of one Focus card as ONE selectable document (FR.23.25): the item id and title, the markdown body (paragraphs, lists, quotes, tables, code) and,
/// on a closed card, the outcome line. A read-only, borderless <see cref="RichTextBox"/> over a single <see cref="FlowDocument"/>, so a mouse selection or
/// Ctrl+A covers all of it and Ctrl+C keeps the paragraph breaks; two cards are two controls, so a selection never spans them. Links stay clickable.
/// The document is rebuilt only when what it shows changed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusCardText : RichTextBox
{
	private const double TitleGap = 8;
	private const double OutcomeGap = 4;
	private const string IdGap = "  ";

	public static readonly DependencyProperty ItemProperty =
		DependencyProperty.Register(nameof(Item), typeof(FocusItemViewModel), typeof(FocusCardText), new PropertyMetadata(null, OnItemChanged));

	private readonly Paragraph _header = new() { Margin = new Thickness(0) };
	private readonly LocalPathDetector _pathDetector = new();
	private readonly FocusCardPathMarker _pathMarker = new();
	private readonly List<FocusPathSpan> _pathSpans = [];
	private (string Id, string Title, string? Body, string Outcome)? _shown;
	private double _lineSpacing;

	static FocusCardText()
	{
		TemplateProperty.OverrideMetadata(typeof(FocusCardText), new FrameworkPropertyMetadata(SelectableText.ChromelessTemplate(typeof(FocusCardText))));
	}

	public FocusCardText()
	{
		IsReadOnly = true;
		IsDocumentEnabled = true; // hyperlinks respond to the mouse
		IsReadOnlyCaretVisible = false;
		BorderThickness = new Thickness(0);
		Padding = new Thickness(0);
		Background = Brushes.Transparent;
		VerticalAlignment = VerticalAlignment.Top;
		SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
		Document = new FlowDocument { PagePadding = new Thickness(0), PageWidth = double.NaN, LineHeight = double.NaN };
		FocusVisualStyle = null;
		SizeChanged += (_, e) =>
		{
			if (e.WidthChanged && ActualWidth > 0)
				Document.PageWidth = ActualWidth;
		};
	}

	/// <summary>The card whose text this shows.</summary>
	public FocusItemViewModel? Item
	{
		get => (FocusItemViewModel?)GetValue(ItemProperty);
		set => SetValue(ItemProperty, value);
	}

	/// <summary>Opens a clicked link; the application's URL launcher unless a caller sets its own.</summary>
	public Action<string>? OpenLink { get; set; }

	/// <summary>Opens a link in the system browser (FR.26.9: Ctrl+click and the link's menu entry); the application's system launcher unless a caller sets its own.</summary>
	public Action<string>? OpenSystemLink { get; set; }

	/// <summary>The paths marked in the card's title and body, in document order (FR.23.30). For tests and diagnostics.</summary>
	public IReadOnlyList<string> MarkedPaths => _pathSpans.Select(s => s.Path).ToList();

	/// <summary>The path marked at the point (in this control's coordinates), or null.</summary>
	public string? PathAt(Point point)
	{
		var position = GetPositionFromPoint(point, snapToText: false);
		for (var element = position?.Parent as DependencyObject; element != null; element = (element as FrameworkContentElement)?.Parent)
		{
			if (element is FocusPathSpan span)
				return span.Path;
		}

		return null;
	}

	/// <summary>True when the point is over a marked path.</summary>
	public bool IsPathAt(Point point) => PathAt(point) != null;

	/// <summary>The control-coordinate rectangle of the first line of a marked path, or <see cref="Rect.Empty"/> when it is not laid out.</summary>
	public Rect RectOf(FocusPathSpan span)
	{
		var start = span.ContentStart.GetCharacterRect(LogicalDirection.Forward);
		var end = span.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
		if (start.IsEmpty || end.IsEmpty)
			return Rect.Empty;

		return end.Top > start.Top + start.Height / 2 ? start : Rect.Union(start, end);   // a path that wraps shows its first line
	}

	/// <summary>The marked paths with their rectangles, for tests and diagnostics.</summary>
	public IReadOnlyList<(string Path, Rect Bounds)> PathBounds => _pathSpans.Select(s => (s.Path, RectOf(s))).ToList();

	/// <summary>Opens a marked path: the card's item asks the session to preview it (FR.23.30).</summary>
	public void ActivatePath(string path) => Item?.OpenPath(path);

	/// <summary>
	/// A press at <paramref name="point"/> with <paramref name="modifiers"/>: Ctrl+left on a marked path opens it and the press is taken (true); any other
	/// press is left to the text selection (false), so a plain click and a drag select as before (FR.23.25).
	/// </summary>
	public bool TryActivatePathAt(Point point, ModifierKeys modifiers)
	{
		if ((modifiers & ModifierKeys.Control) == 0 || PathAt(point) is not { } path)
			return false;

		ActivatePath(path);
		return true;
	}

	protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		if (TryActivatePathAt(e.GetPosition(this), Keyboard.Modifiers))
		{
			e.Handled = true;
			return;
		}

		base.OnPreviewMouseLeftButtonDown(e);
	}

	protected override void OnPreviewMouseMove(MouseEventArgs e)
	{
		base.OnPreviewMouseMove(e);
		UpdatePathCursor(e.GetPosition(this));
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		base.OnPreviewKeyDown(e);
		RefreshCursorAfterModifier(e.Key);
	}

	protected override void OnPreviewKeyUp(KeyEventArgs e)
	{
		base.OnPreviewKeyUp(e);
		RefreshCursorAfterModifier(e.Key);
	}

	private void RefreshCursorAfterModifier(Key key)
	{
		if (key is Key.LeftCtrl or Key.RightCtrl && IsMouseOver)
			UpdatePathCursor(Mouse.GetPosition(this));
	}

	/// <summary>The pointer shows a hand over a path only while Ctrl is held (FR.23.30); otherwise the text cursor stays.</summary>
	private void UpdatePathCursor(Point point)
	{
		if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && IsPathAt(point))
			Cursor = Cursors.Hand;
		else
			ClearValue(CursorProperty);
	}

	/// <summary>The text as shown, paragraphs separated by line breaks.</summary>
	public string PlainText => new TextRange(Document.ContentStart, Document.ContentEnd).Text.TrimEnd('\r', '\n');

	/// <summary>Every paragraph of the document (also inside lists, quotes and tables): its text, font size and fixed line height. For tests and diagnostics.</summary>
	public IReadOnlyList<(string Text, double FontSize, double LineHeight, LineStackingStrategy Strategy)> ParagraphMetrics =>
		Paragraphs(Document.Blocks).Select(p => (new TextRange(p.ContentStart, p.ContentEnd).Text, p.FontSize, p.LineHeight, p.LineStackingStrategy)).ToList();

	/// <summary>True when the point (in this control's coordinates) is over the id-and-title paragraph; a click there toggles a closed card, the rest of the text only selects.</summary>
	public bool IsHeaderAt(Point point)
	{
		var position = GetPositionFromPoint(point, snapToText: false);
		return position != null && ReferenceEquals(position.Paragraph, _header);
	}

	/// <summary>A fixed line height of font size x factor on every paragraph (FR.9.12, see <see cref="LineSpacing"/>); a factor below 1 returns to the natural one.</summary>
	public void SetLineSpacing(double factor)
	{
		_lineSpacing = factor;
		ApplyLineSpacing();
	}

	/// <summary>The control has no scrolling of its own, so the wheel belongs to whatever scrolls around it (the Focus panel).</summary>
	protected override void OnMouseWheel(MouseWheelEventArgs e)
	{
		if (e.Handled)
			return;

		e.Handled = true;
		var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = this };
		(Parent as UIElement)?.RaiseEvent(forwarded);
	}

	/// <summary>For UI Automation this is plain text, named by what it shows, as the text blocks it replaces were.</summary>
	protected override AutomationPeer OnCreateAutomationPeer() => new FocusCardTextPeer(this);

	private sealed class FocusCardTextPeer(FocusCardText owner) : RichTextBoxAutomationPeer(owner)
	{
		protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

		protected override string GetNameCore() => owner.PlainText;

		protected override string GetClassNameCore() => nameof(FocusCardText);

		protected override List<AutomationPeer> GetChildrenCore()
		{
			var children = base.GetChildrenCore() ?? [];
			children.AddRange(owner._pathSpans.Select(span => new FocusPathAutomationPeer(owner, span)));
			return children;
		}
	}

	private static void OnItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
	{
		var control = (FocusCardText)sender;
		if (e.OldValue is FocusItemViewModel old)
			PropertyChangedEventManager.RemoveHandler(old, control.OnItemPropertyChanged, string.Empty);
		if (e.NewValue is FocusItemViewModel item)
			PropertyChangedEventManager.AddHandler(item, control.OnItemPropertyChanged, string.Empty);
		control.Rebuild();
	}

	private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

	private void Rebuild()
	{
		var item = Item;
		if (item == null)
		{
			_shown = null;
			Document.Blocks.Clear();
			return;
		}

		var state = (Id: item.Id, Title: item.Title, Body: item.IsBodyVisible ? item.Body : null, Outcome: item.OutcomeText);
		if (_shown == state)
			return;

		_shown = state;
		_pathSpans.Clear();
		Document.Blocks.Clear();

		_header.Inlines.Clear();
		_header.Margin = new Thickness(0);
		var id = new Run(item.Id);
		id.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorSecondaryBrush");
		_header.Inlines.Add(id);
		_header.Inlines.Add(new Run(IdGap));
		_header.Inlines.Add(new Run(item.Title) { FontWeight = FontWeights.SemiBold });
		Document.Blocks.Add(_header);
		var paths = PathsOf(item.Title, state.Body);
		var underline = TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;
		_pathSpans.AddRange(_pathMarker.Mark(_header, paths, underline));   // the id and the title; the outcome line below is the user's own text and is never marked

		if (state.Outcome.Length > 0)
		{
			var outcome = new Paragraph(new Run(state.Outcome)) { Margin = new Thickness(0, OutcomeGap, 0, 0) };
			outcome.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorSecondaryBrush");
			Document.Blocks.Add(outcome);
		}

		if (state.Body is { Length: > 0 } body)
		{
			var blocks = new MarkdownBlockBuilder(new UrlPolicy(), Open, OpenInSystemBrowser).Build(body);
			if (blocks.Count > 0)
			{
				var last = Document.Blocks.LastBlock;
				last.Margin = new Thickness(0, last.Margin.Top, 0, TitleGap);
				foreach (var block in blocks)
				{
					Document.Blocks.Add(block);
					_pathSpans.AddRange(_pathMarker.Mark(block, paths, underline));
				}
			}
		}

		ApplyLineSpacing();
	}

	/// <summary>The paths the title and the body spell, by the same rules as the session output (FR.26.5, FR.26.7): one detector, no second set of rules.</summary>
	private List<string> PathsOf(string title, string? body)
	{
		if (Item?.IsElevatedRun == true)
			return [];   // the title of an elevated run is the start of its command: never parsed for paths (FR.23.32)

		var paths = new List<string>(_pathDetector.Detect(title));
		if (!string.IsNullOrEmpty(body))
			paths.AddRange(_pathDetector.Detect(body).Where(p => !paths.Contains(p, StringComparer.Ordinal)));
		return paths;
	}

	private void Open(string url)
	{
		if (OpenLink != null)
			OpenLink(url);
		else
			(Application.Current as App)?.Services.GetService<IUrlLauncher>()?.TryOpen(url);
	}

	private void OpenInSystemBrowser(string url)
	{
		if (OpenSystemLink != null)
			OpenSystemLink(url);
		else
			(Application.Current as App)?.Services.GetService<ISystemUrlLauncher>()?.TryOpen(url);
	}

	private void ApplyLineSpacing()
	{
		foreach (var paragraph in Paragraphs(Document.Blocks))
		{
			if (_lineSpacing >= 1)
			{
				paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
				paragraph.LineHeight = Math.Max(1, paragraph.FontSize * _lineSpacing);
			}
			else
			{
				paragraph.ClearValue(Block.LineHeightProperty);
				paragraph.ClearValue(Block.LineStackingStrategyProperty);
			}
		}
	}

	private static IEnumerable<Paragraph> Paragraphs(IEnumerable<Block> blocks)
	{
		foreach (var block in blocks)
		{
			switch (block)
			{
				case Paragraph paragraph:
					yield return paragraph;
					break;
				case Section section:
					foreach (var inner in Paragraphs(section.Blocks))
						yield return inner;
					break;
				case List list:
					foreach (var item in list.ListItems)
						foreach (var inner in Paragraphs(item.Blocks))
							yield return inner;
					break;
				case Table table:
					foreach (var cell in table.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells))
						foreach (var inner in Paragraphs(cell.Blocks))
							yield return inner;
					break;
			}
		}
	}
}
