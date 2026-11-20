using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using AvalonDock.Controls;
using AvalonDock.Layout;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>Regression: a click on a Hyperlink inside a docked panel threw in the focus guard (VisualTreeHelper.GetParent on a content element) and the link never ran.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingHyperlinkFocusTests
{
	private const string Url = "https://linear.app/hardstop/issue/HSE-233";

	private static Window Host(DockingArea area)
	{
		var window = new Window { Content = area, Width = 1200, Height = 700, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
		window.Show();
		Settle(window);
		return window;
	}

	private static void Settle(Window window)
	{
		window.UpdateLayout();
		window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
		window.UpdateLayout();
	}

	private static LayoutAnchorable AddPanel(DockingArea area, Window window, string id, object content)
	{
		var panel = new LayoutAnchorable { ContentId = id, Title = id, Content = content };
		new PreviewDockLayoutApplier().Apply(area.Root, area.DocumentPane, panel, null, area.AreaWidth, area.AreaHeight);
		Settle(window);
		return panel;
	}

	private static (Hyperlink Link, Run Inner, TextBlock Block) LinkInTextBlock(List<string> clicks)
	{
		var inner = new Run("HSE-233");
		var link = new Hyperlink(inner);
		link.Click += (_, _) => clicks.Add(Url);
		var block = new TextBlock();
		block.Inlines.Add(link);
		return (link, inner, block);
	}

	private static bool MoveFocus(UIElement from, DependencyObject to)
	{
		var args = new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, from, (IInputElement)to) { RoutedEvent = Keyboard.PreviewLostKeyboardFocusEvent, Source = from };
		from.RaiseEvent(args);
		return args.Handled;
	}

	[Fact]
	public void TheParentWalk_HandlesVisualsAndContentElementsAlike()
	{
		StaTestRunner.Run(() =>
		{
			var tree = new DockingVisualTree();
			var (link, inner, block) = LinkInTextBlock([]);
			Assert.Same(link, tree.ParentOf(inner));
			Assert.Same(block, tree.ParentOf(link));
			Assert.Null(tree.ParentOf(block));
			Assert.Null(tree.ParentOf(new Hyperlink()));   // a framework content element without a parent
			Assert.Null(tree.ParentOf(new Run("x")));

			var border = new Border();
			var child = new TextBlock();
			border.Child = child;
			Assert.Same(border, tree.ParentOf(child));

			var document = new FlowDocument();
			var paragraph = new Paragraph();
			var documentLink = new Hyperlink(new Run("x"));
			paragraph.Inlines.Add(documentLink);
			document.Blocks.Add(paragraph);
			Assert.Same(paragraph, tree.ParentOf(documentLink));
			Assert.Same(document, tree.ParentOf(paragraph));
		});
	}

	[Fact]
	public void AFocusMoveOntoAHyperlinkOrItsRun_InADockedPanel_DoesNotThrow_AndFollowsTheRule()
	{
		StaTestRunner.Run(() =>
		{
			var composer = new TextBox();
			var area = new DockingArea(composer);
			var window = Host(area);
			var clicks = new List<string>();
			var (link, inner, block) = LinkInTextBlock(clicks);
			AddPanel(area, window, "images", block);
			Thread.Sleep(450);
			AddPanel(area, window, "tags", new TextBlock());   // a real docking change: the guard is armed

			Assert.True(MoveFocus(composer, link));    // armed, no input: a panel to panel move by the library is cancelled
			Assert.True(MoveFocus(composer, inner));

			area.Chrome.Focus.NoteInput();             // the user's click comes first
			Assert.False(MoveFocus(composer, link));
			Assert.False(MoveFocus(composer, inner));

			link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
			Assert.Equal([Url], clicks);
			window.Close();
		});
	}

	[Fact]
	public void TheMouseDownOnAHyperlink_InADockedPanel_FocusesIt_ThrowsNothing_AndTheClickRuns()
	{
		StaTestRunner.Run(() =>
		{
			var composer = new TextBox();
			var area = new DockingArea(composer);
			var window = Host(area);
			var clicks = new List<string>();
			var (link, inner, block) = LinkInTextBlock(clicks);
			AddPanel(area, window, "images", block);
			Thread.Sleep(450);
			AddPanel(area, window, "tags", new TextBlock());   // armed
			Keyboard.Focus(composer);

			foreach (var source in new Inline[] { link, inner })
			{
				var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent, Source = source };
				var leftDown = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = source };
				var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent, Source = source };
				source.RaiseEvent(down);
				source.RaiseEvent(leftDown);
				source.RaiseEvent(up);
			}

			// Hyperlink fires Click on the mouse-up only while it holds the real mouse capture and IsMouseOver, which a synthetic event cannot give: the Click itself is raised here.
			link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
			Assert.Equal([Url], clicks);
			window.Close();
		});
	}

	[Fact]
	public void Esc_WhoseSourceIsAHyperlinkInsideAnOpenSlideOut_ClosesIt_WithoutAnException()
	{
		StaTestRunner.Run(() =>
		{
			var area = new DockingArea(new Border { MinWidth = 100 });
			var window = Host(area);
			var (link, _, block) = LinkInTextBlock([]);
			var panel = AddPanel(area, window, "images", block);
			panel.ToggleAutoHide();
			Settle(window);
			var anchor = new DockingVisualTree().Descendants<LayoutAnchorControl>(area).Single(a => ReferenceEquals(a.Model, panel));
			typeof(AvalonDock.DockingManager).GetMethod("ShowAutoHideWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(area.Manager, [anchor]);
			Settle(window);
			Assert.True(area.Chrome.SlideOut.IsOpen);

			var esc = new KeyEventArgs(Keyboard.PrimaryDevice, (HwndSource)PresentationSource.FromVisual(window)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = link };
			link.RaiseEvent(esc);
			Assert.True(esc.Handled);
			Assert.False(area.Chrome.SlideOut.IsOpen);
			window.Close();
		});
	}
}
