using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Wrapping text the mouse can select and copy, with clickable links: WPF's <see cref="TextBlock"/> cannot be selected, so this is a read-only,
/// borderless <see cref="RichTextBox"/> holding a single paragraph. It looks like a text block (no chrome, transparent, no scrolling of its own).
/// <see cref="Inlines"/> is filled by code; <see cref="Text"/> is the plain-text form for bindings.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SelectableText : RichTextBox
{
	public static readonly DependencyProperty TextProperty =
		DependencyProperty.Register(nameof(Text), typeof(string), typeof(SelectableText), new PropertyMetadata(string.Empty, OnTextChanged));

	private readonly Paragraph _paragraph = new() { Margin = new Thickness(0) };

	static SelectableText()
	{
		TemplateProperty.OverrideMetadata(typeof(SelectableText), new FrameworkPropertyMetadata(ChromelessTemplate(typeof(SelectableText))));
	}

	/// <summary>Without a template of its own the Fluent RichTextBox draws a frame and an accent underline on focus: a bare scroll host (no scrolling of its own) instead.</summary>
	internal static ControlTemplate ChromelessTemplate(Type owner)
	{
		var template = new ControlTemplate(owner);
		var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
		host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
		host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
		host.SetValue(ScrollViewer.FocusableProperty, false);
		template.VisualTree = host;
		template.Seal();
		return template;
	}

	public SelectableText()
	{
		IsReadOnly = true;
		IsDocumentEnabled = true; // hyperlinks respond to the mouse
		IsReadOnlyCaretVisible = false;
		BorderThickness = new Thickness(0);
		Padding = new Thickness(0);
		Background = Brushes.Transparent;
		VerticalAlignment = VerticalAlignment.Top;
		SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
		Document = new FlowDocument(_paragraph) { PagePadding = new Thickness(0), PageWidth = double.NaN };
		Document.LineHeight = double.NaN;
		FocusVisualStyle = null;
		SizeChanged += (_, e) =>
		{
			if (e.WidthChanged && ActualWidth > 0)
				Document.PageWidth = ActualWidth;
		};
	}

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	/// <summary>A fixed line height for the paragraph (FR.9.12, see <see cref="LineSpacing"/>); null returns to the natural one.</summary>
	public void SetLineHeight(double? height)
	{
		if (height is { } value)
		{
			_paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			_paragraph.LineHeight = value;
		}
		else
		{
			_paragraph.ClearValue(Block.LineHeightProperty);
			_paragraph.ClearValue(Block.LineStackingStrategyProperty);
		}
	}

	/// <summary>The line height of the paragraph (NaN = natural) and its stacking strategy.</summary>
	public (double Height, LineStackingStrategy Strategy) LineMetrics => (_paragraph.LineHeight, _paragraph.LineStackingStrategy);

	/// <summary>The text as shown, whichever way it was filled.</summary>
	public string PlainText => new TextRange(Document.ContentStart, Document.ContentEnd).Text.TrimEnd('\r', '\n');

	/// <summary>The content; add runs, spans and hyperlinks here.</summary>
	public InlineCollection Inlines => _paragraph.Inlines;

	/// <summary>The control has no scrolling of its own, so the wheel belongs to whatever scrolls around it (the Focus panel).</summary>
	protected override void OnMouseWheel(MouseWheelEventArgs e)
	{
		if (e.Handled)
			return;

		e.Handled = true;
		var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = this };
		(Parent as UIElement)?.RaiseEvent(forwarded);
	}

	/// <summary>For UI Automation (and screen readers) this is plain text, named by what it shows, as the text block it replaces was.</summary>
	protected override AutomationPeer OnCreateAutomationPeer() => new SelectableTextPeer(this);

	private sealed class SelectableTextPeer(SelectableText owner) : RichTextBoxAutomationPeer(owner)
	{
		protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

		protected override string GetNameCore() => owner.PlainText;

		protected override string GetClassNameCore() => nameof(SelectableText);
	}

	private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
	{
		var control = (SelectableText)sender;
		control.Inlines.Clear();
		if (e.NewValue is string { Length: > 0 } text)
			control.Inlines.Add(new Run(text));
	}
}
