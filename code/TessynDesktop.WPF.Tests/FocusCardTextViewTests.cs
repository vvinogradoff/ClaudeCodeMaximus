using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>TC.FX.01-04 and TC.FX.10-11: one selection across a Focus card (FR.23.25) and the vertical option buttons (FR.23.26), in a real layout.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusCardTextViewTests
{
	private const double PanelWidth = 420;
	private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

	private const string RichBody = "First paragraph of the body.\n\nSecond paragraph with [a link](https://example.com/doc).\n\nThird paragraph.\n\n"
		+ "- list one\n- list two\n\n> a quoted line\n\n| Name | Value |\n|---|---|\n| cell-a | cell-b |\n\n```\ncode line one\ncode line two\n```\n\nLast paragraph.";

	private static AttentionItemDto Open(int n, AttentionKind kind = AttentionKind.Ask, string? body = RichBody, string[]? options = null, string? recommendation = null) => new()
	{
		Id = "A" + n, Number = n, Kind = kind, Status = AttentionStatus.Open, Title = "Title " + n, Body = body, Options = options ?? [], Recommendation = recommendation, CreatedUtc = T0.AddMinutes(n),
	};

	private static AttentionItemDto Closed(int n) => new()
	{
		Id = "A" + n, Number = n, Kind = AttentionKind.Ask, Status = AttentionStatus.Answered, Title = "Title " + n, Body = RichBody, Answer = "Postgres", CreatedUtc = T0.AddMinutes(n), ClosedUtc = T0.AddMinutes(n + 1),
	};

	private static FocusPanel Show(FocusViewModel focus)
	{
		var panel = new FocusPanel { DataContext = focus, Width = PanelWidth };
		Layout(panel);
		return panel;
	}

	private static void Layout(FrameworkElement element)
	{
		element.Measure(new Size(PanelWidth, 2000));
		element.Arrange(new Rect(0, 0, PanelWidth, 2000));
		element.UpdateLayout();
	}

	private static FocusViewModel Create(params AttentionItemDto[] items)
	{
		var client = new FakeTessynClient();
		client.Attention["a"] = [.. items];
		var focus = new FocusViewModel(client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);
		focus.SetSessionAsync("a").GetAwaiter().GetResult();
		return focus;
	}

	private static List<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		var result = new List<T>();
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				result.Add(match);
			result.AddRange(Descendants<T>(child));
		}

		return result;
	}

	private static void AssertWholeCardText(string text, bool closed)
	{
		foreach (var part in new[] { "Title ", "First paragraph", "Second paragraph", "a link", "Third paragraph", "list one", "list two", "a quoted line", "cell-a", "cell-b", "code line one", "code line two", "Last paragraph" })
			Assert.Contains(part, text);
		if (closed)
			Assert.Contains("Answered: Postgres", text);
	}

	[Fact]
	public void TC_FX_01_ACardsIdTitleBodyAndOutcome_AreOneSelectableText()
	{
		string openText = "", closedText = "", decidedText = "", selected = "";
		int openControls = 0, selectables = 0;
		StaTestRunner.Run(() =>
		{
			var focus = Create(Open(1), Open(2, AttentionKind.Decided), Closed(3));
			focus.IsDecisionsExpanded = true;
			focus.IsClosedExpanded = true;
			focus.ClosedItems.Single().ToggleBodyCommand.Execute(null);
			var panel = Show(focus);

			var cards = Descendants<FocusCardText>(panel);
			openControls = cards.Count;
			selectables = Descendants<SelectableText>(panel).Count;
			openText = cards.Single(c => c.Item!.Id == "A1").PlainText;
			decidedText = cards.Single(c => c.Item!.Id == "A2").PlainText;
			closedText = cards.Single(c => c.Item!.Id == "A3").PlainText;

			// A selection from the title to the last paragraph covers everything in between.
			var card = cards.Single(c => c.Item!.Id == "A1");
			card.Selection.Select(card.Document.ContentStart, card.Document.ContentEnd);
			selected = card.Selection.Text;
		});

		Assert.Equal(3, openControls);      // one text control per card, open, Decided and closed alike
		Assert.Equal(0, selectables);       // no separate selectable paragraph is left on a card
		AssertWholeCardText(openText, closed: false);
		AssertWholeCardText(decidedText, closed: false);
		AssertWholeCardText(closedText, closed: true);
		Assert.StartsWith("A1", openText);
		AssertWholeCardText(selected, closed: false);
		Assert.True(selected.IndexOf("Title 1", StringComparison.Ordinal) < selected.IndexOf("Last paragraph", StringComparison.Ordinal));
	}

	[Fact]
	public void TC_FX_02_CtrlA_SelectsTheCardsTextOnly_AndCopyKeepsTheParagraphBreaks()
	{
		string selectedFirst = "", selectedSecond = "", copied = "";
		bool canSelectAll = false;
		StaTestRunner.Run(() =>
		{
			var focus = Create(Open(1), Open(2));
			var panel = new FocusPanel { DataContext = focus };
			var window = new Window { Content = panel, Width = PanelWidth, Height = 900, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			window.Show();
			panel.UpdateLayout();
			var cards = Descendants<FocusCardText>(panel);
			var first = cards.Single(c => c.Item!.Id == "A1");
			var second = cards.Single(c => c.Item!.Id == "A2");
			first.Focus();

			canSelectAll = ApplicationCommands.SelectAll.CanExecute(null, first);
			ApplicationCommands.SelectAll.Execute(null, first);
			selectedFirst = first.Selection.Text;
			selectedSecond = second.Selection.Text;

			Clipboard.Clear();
			ApplicationCommands.Copy.Execute(null, first);
			copied = Clipboard.GetText();
			window.Close();
		});

		Assert.True(canSelectAll);
		AssertWholeCardText(selectedFirst, closed: false);
		Assert.Equal(string.Empty, selectedSecond);                 // Ctrl+A never reaches another card
		AssertWholeCardText(copied, closed: false);
		Assert.Contains("First paragraph of the body.\r\n", copied);    // the paragraph breaks are kept
		Assert.Contains("Third paragraph.", copied);
		Assert.True(copied.Split('\n').Length >= 8, "the copied text lost its line breaks");
	}

	[Fact]
	public void TC_FX_03_ASelectionStaysInItsCard_AndOptionsAndTheReplyBoxAreNotPartOfIt()
	{
		string selectedFirst = "", cardText = "";
		bool replyBoxInsideText = true, buttonsInsideText = true;
		string secondSelection = "x";
		StaTestRunner.Run(() =>
		{
			var focus = Create(Open(1, options: ["Option alpha", "Option beta"], recommendation: "The recommended one"), Open(2));
			focus.Items.Single(i => i.Id == "A1").ReplyText = "my typed reply";
			var panel = Show(focus);
			var cards = Descendants<FocusCardText>(panel);
			var first = cards.Single(c => c.Item!.Id == "A1");
			var second = cards.Single(c => c.Item!.Id == "A2");

			// Even a selection stretched from the first card's start to the second card's end cannot cross: each card has its own document.
			Assert.NotSame(first.Document, second.Document);
			first.Selection.Select(first.Document.ContentStart, first.Document.ContentEnd);
			selectedFirst = first.Selection.Text;
			secondSelection = second.Selection.Text;
			cardText = first.PlainText;
			replyBoxInsideText = Descendants<TextBox>(first).Any();
			buttonsInsideText = Descendants<Button>(first).Any();
		});

		AssertWholeCardText(selectedFirst, closed: false);
		Assert.Equal(string.Empty, secondSelection);
		Assert.DoesNotContain("Option alpha", cardText);
		Assert.DoesNotContain("The recommended one", cardText);
		Assert.DoesNotContain("my typed reply", cardText);
		Assert.False(replyBoxInsideText);
		Assert.False(buttonsInsideText);
	}

	[Fact]
	public void TC_FX_04_APressEndingASelection_TriggersNoCardAction_AndALinkStillOpens()
	{
		var toggledByClick = false;
		var toggledByDrag = true;
		var toggledByBodyPress = true;
		var toggledByDoubleClick = true;
		bool titleIsHeader = false, bodyIsHeader = true;
		var opened = new List<string>();
		int hyperlinks = 0;
		StaTestRunner.Run(() =>
		{
			var focus = Create(Closed(3));
			focus.IsClosedExpanded = true;
			var item = focus.ClosedItems.Single();
			item.ToggleBodyCommand.Execute(null);   // the body shows, so there is text to press in
			var panel = Show(focus);
			var card = Descendants<FocusCardText>(panel).Single();

			var title = card.Document.Blocks.FirstBlock.ContentStart.GetCharacterRect(LogicalDirection.Forward).TopLeft + new Vector(30, 4);
			var body = card.Document.Blocks.LastBlock.ContentStart.GetCharacterRect(LogicalDirection.Forward).TopLeft + new Vector(4, 4);
			titleIsHeader = card.IsHeaderAt(title);
			bodyIsHeader = card.IsHeaderAt(body);

			// A drag from the title (a selection) ends in a release far away: nothing toggles.
			panel.PressOnClosedCard(new Point(50, 50), clickCount: 1, overHeader: true);
			toggledByDrag = panel.ReleaseOnClosedCard(item, new Point(200, 120));
			// A press in the body, released where it started: a click on text that is not the header does nothing either.
			panel.PressOnClosedCard(new Point(50, 200), clickCount: 1, overHeader: bodyIsHeader);
			toggledByBodyPress = panel.ReleaseOnClosedCard(item, new Point(50, 200));
			// A double click (selects a word) does not toggle.
			panel.PressOnClosedCard(new Point(50, 50), clickCount: 2, overHeader: true);
			toggledByDoubleClick = panel.ReleaseOnClosedCard(item, new Point(50, 50));
			Assert.True(item.IsBodyExpanded);

			// A plain click on the header still shows or hides the body.
			panel.PressOnClosedCard(new Point(50, 50), clickCount: 1, overHeader: titleIsHeader);
			toggledByClick = panel.ReleaseOnClosedCard(item, new Point(51, 50));

			// A link in the body opens on a click.
			item.IsBodyExpanded = true;
			Layout(panel);
			card = Descendants<FocusCardText>(panel).Single();
			card.OpenLink = opened.Add;
			var links = card.Document.Blocks.SelectMany(Inlines).OfType<Hyperlink>().ToList();
			hyperlinks = links.Count;
			foreach (var link in links)
				link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
		});

		Assert.True(titleIsHeader);
		Assert.False(bodyIsHeader);
		Assert.False(toggledByDrag);
		Assert.False(toggledByBodyPress);
		Assert.False(toggledByDoubleClick);
		Assert.True(toggledByClick);
		Assert.Equal(1, hyperlinks);
		Assert.Equal(["https://example.com/doc"], opened);
	}

	[Fact]
	public void ALinkInACardsText_InsideTheDockingArea_FocusesWithoutAnException_AndOpensTheUrl()
	{
		var opened = new List<string>();
		StaTestRunner.Run(() =>
		{
			var focus = Create(Open(1));
			var panel = new FocusPanel { DataContext = focus };
			var area = new DockingArea(new TextBox());
			var window = new Window { Content = area, Width = 1200, Height = 700, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			window.Show();
			var layoutPanel = new AvalonDock.Layout.LayoutAnchorable { ContentId = "focus-test", Title = "Focus", Content = panel };
			new PreviewDockLayoutApplier().Apply(area.Root, area.DocumentPane, layoutPanel, null, area.AreaWidth, area.AreaHeight);
			window.UpdateLayout();
			window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
			window.UpdateLayout();
			Thread.Sleep(450);
			area.Chrome.Focus.NoteDockingChange();   // armed

			var card = Descendants<FocusCardText>(panel).Single();
			card.OpenLink = opened.Add;
			var link = card.Document.Blocks.SelectMany(Inlines).OfType<Hyperlink>().Single();
			var from = new TextBox();
			var args = new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, from, link) { RoutedEvent = Keyboard.PreviewLostKeyboardFocusEvent, Source = from };
			area.Manager.RaiseEvent(args);   // the guard sees the focus move onto the link (a content element)
			link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
			window.Close();
		});

		Assert.Equal(["https://example.com/doc"], opened);
	}

	private static IEnumerable<Inline> Inlines(Block block) => block switch
	{
		Paragraph p => p.Inlines.SelectMany(Flatten),
		Section s => s.Blocks.SelectMany(Inlines),
		List l => l.ListItems.SelectMany(i => i.Blocks.SelectMany(Inlines)),
		_ => [],
	};

	private static IEnumerable<Inline> Flatten(Inline inline) => inline is Span span ? span.Inlines.SelectMany(Flatten).Prepend(inline) : [inline];

	private static (List<Button> Buttons, FocusCardText Text, Point[] Tops) OptionLayout(FocusPanel panel)
	{
		var buttons = Descendants<Button>(panel).Where(b => b.DataContext is FocusOptionViewModel).ToList();
		var text = Descendants<FocusCardText>(panel).First();
		return (buttons, text, buttons.Select(b => b.TranslatePoint(new Point(0, 0), panel)).ToArray());
	}

	[Fact]
	public void TC_FX_10_OptionsAreOneVerticalList_EachAsWideAsTheCard_WithTwoPixelGaps_AndOnlyALongOneWraps()
	{
		var widths = new List<double>();
		var heights = new List<double>();
		var tops = new List<double>();
		var wraps = new List<bool>();
		double cardWidth = 0;
		var longText = string.Join(' ', Enumerable.Repeat("a very long option that has to wrap when the card is narrow", 6));
		StaTestRunner.Run(() =>
		{
			var focus = Create(Open(1, body: "Pick one", options: ["Yes", longText, "No"], recommendation: "Go with yes"));
			var panel = Show(focus);
			var (buttons, text, positions) = OptionLayout(panel);
			cardWidth = Descendants<ItemsControl>(panel).Single(i => i.ItemsSource == focus.Items.Single().Options).ActualWidth;
			foreach (var (button, position) in buttons.Zip(positions))
			{
				widths.Add(button.ActualWidth);
				heights.Add(button.ActualHeight);
				tops.Add(position.Y);
				wraps.Add(Descendants<TextBlock>(button).Single().ActualHeight > Descendants<TextBlock>(button).Single().FontSize * 2.2);
				Assert.Equal(text.TranslatePoint(new Point(0, 0), panel).X, position.X, 3);   // left edges line up with the card text: one column
			}
		});

		Assert.Equal(4, widths.Count);                    // Yes, the long one, No, and the recommendation last
		Assert.All(widths, w => Assert.Equal(cardWidth, w, 1));   // each as wide as the card
		Assert.True(cardWidth > PanelWidth * 0.8);
		Assert.Equal([false, true, false, false], wraps); // only the long one wraps, and it grows in height
		Assert.True(heights[1] > heights[0] * 2);
		for (var i = 1; i < tops.Count; i++)
			Assert.Equal(2.0, tops[i] - (tops[i - 1] + heights[i - 1]), 1);   // the gap between buttons is 2 px
	}

	/// <summary>TC.FX.11 / TC.FX.20 (FR.23.26, FR.23.28): the recommendation is the last button, without a prefix, tooltip Recommended, in the accent button style; the others keep the default style.</summary>
	[Fact]
	public void TC_FX_20_TheRecommendationIsTheLastButton_WithoutAPrefix_InTheAccentButtonStyle_AndTooltipRecommended()
	{
		var shown = new List<string>();
		var styles = new List<Style?>();
		var tooltips = new List<object?>();
		Style? accentStyle = null;
		StaTestRunner.Run(() =>
		{
			var focus = Create(Open(1, body: null, options: ["Yes", "No"], recommendation: "Maybe"));
			accentStyle = new Style(typeof(Button));
			var panel = new FocusPanel { DataContext = focus, Width = PanelWidth };
			panel.Resources["AccentButtonStyle"] = accentStyle;   // the style of Send answers, Send and the Settings Save button
			Layout(panel);
			foreach (var button in Descendants<Button>(panel).Where(b => b.DataContext is FocusOptionViewModel))
			{
				shown.Add(Descendants<TextBlock>(button).Single().Text);
				styles.Add(button.Style);
				tooltips.Add(button.ToolTip);
			}
		});

		Assert.Equal(["Yes", "No", "Maybe"], shown);                       // no "Recommended: " or "Use recommendation: " prefix
		Assert.Same(accentStyle, styles[2]);
		Assert.NotSame(accentStyle, styles[0]);
		Assert.NotSame(accentStyle, styles[1]);
		Assert.Equal("Recommended", tooltips[2]);
		Assert.NotEqual("Recommended", tooltips[0]);
	}

	/// <summary>TC.FX.21 (FR.23.28): the reply box sits in a 2 px accent border, with no border of its own.</summary>
	[Fact]
	public void TC_FX_21_TheReplyBox_HasATwoPixelAccentBorder()
	{
		Thickness frame = default, own = default;
		Brush? brush = null;
		SolidColorBrush? accent = null;
		StaTestRunner.Run(() =>
		{
			var focus = Create(Open(1, body: null, options: ["Yes"], recommendation: null));
			accent = new SolidColorBrush(Color.FromRgb(0x12, 0x78, 0xD4));
			var panel = new FocusPanel { DataContext = focus, Width = PanelWidth };
			panel.Resources["AccentFillColorDefaultBrush"] = accent;
			Layout(panel);
			var box = Descendants<TextBox>(panel).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Your answer");
			var border = (Border)((FrameworkElement)box.Parent).Parent;
			frame = border.BorderThickness;
			brush = border.BorderBrush;
			own = box.BorderThickness;
		});

		Assert.Equal(new Thickness(2), frame);
		Assert.Same(accent, brush);
		Assert.Equal(new Thickness(0), own);
	}

	/// <summary>TC.FX.22 (FR.23.28): the grip sets the reply box height, within 56..600 px.</summary>
	[Fact]
	public void TC_FX_22_TheGrip_ResizesTheReplyBox_WithinItsRange()
	{
		var heights = new List<double>();
		var hasGrip = false;
		StaTestRunner.Run(() =>
		{
			var focus = Create(Open(1, body: null, options: ["Yes"], recommendation: null));
			var panel = new FocusPanel { DataContext = focus, Width = PanelWidth };
			Layout(panel);
			var box = Descendants<TextBox>(panel).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Your answer");
			hasGrip = Descendants<System.Windows.Controls.Primitives.Thumb>(panel).Any(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Resize the answer box");
			var start = box.ActualHeight;
			FocusPanel.ResizeReply(box, 100);
			heights.Add(box.Height - start);
			FocusPanel.ResizeReply(box, 5000);
			heights.Add(box.Height);
			FocusPanel.ResizeReply(box, -5000);
			heights.Add(box.Height);
			Layout(panel);
			heights.Add(box.ActualHeight);
		});

		Assert.True(hasGrip);
		Assert.Equal(100, heights[0], 0.5);
		Assert.Equal(FocusPanel.MaxReplyHeight, heights[1]);
		Assert.Equal(FocusPanel.MinReplyHeight, heights[2]);
		Assert.Equal(FocusPanel.MinReplyHeight, heights[3], 0.5);
	}
}
