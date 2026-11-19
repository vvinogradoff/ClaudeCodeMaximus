using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AvalonDock.Controls;
using AvalonDock.Layout;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.6.13 / FR.6.20 (stage 1): the compact docking chrome, the keyboard routing of floating panels, the focus rule and the slide-out's Esc, on an STA thread.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingChromeViewTests
{
	private sealed class RecordingCommands : IShellCommands
	{
		public List<(Key Key, ModifierKeys Modifiers)> Gestures { get; } = [];

		public bool TryExecuteGesture(Key key, ModifierKeys modifiers, bool textInputFocused, bool isRepeat = false, object? focused = null)
		{
			Gestures.Add((key, modifiers));
			return key == Key.F7;
		}

		public void Register(ShellCommand command) => throw new NotSupportedException();
		public void AddMenuEntry(string menuId, string commandId, int order = 0, string? group = null) => throw new NotSupportedException();
		public ShellCommand? Find(string commandId) => null;
		public IReadOnlyList<ShellCommand> GetCommands() => [];
		public IReadOnlyList<ShellMenuItemModel> BuildMenu(string menuId, object? parameter) => [];
		public string? GetGesture(string commandId) => null;
		public ShellCommand? FindByGesture(Key key, ModifierKeys modifiers) => null;
		public IReadOnlyList<GestureConflict> GetConflicts() => [];
		public Task<bool> ExecuteAsync(string commandId, object? parameter) => Task.FromResult(false);
	}

	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;

			foreach (var nested in Descendants<T>(child))
				yield return nested;
		}
	}

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

	private static DockingArea NewArea()
	{
		var area = new DockingArea(new Border { Name = "Stub", MinWidth = 100 });
		return area;
	}

	private static LayoutAnchorable AddPanel(DockingArea area, Window window, string id)
	{
		var panel = new LayoutAnchorable { ContentId = id, Title = id, Content = new TextBlock { Text = id } };
		new PreviewDockLayoutApplier().Apply(area.Root, area.DocumentPane, panel, null, area.AreaWidth, area.AreaHeight);
		Settle(window);
		return panel;
	}

	[Fact]
	public void TheChromeStyles_Load_AndTheManagerUsesThem()
	{
		StaTestRunner.Run(() =>
		{
			var area = NewArea();
			Assert.IsType<Style>(area.TryFindResource(Constants.DockingChrome.PaneStyleKey));
			Assert.IsType<ContextMenu>(area.TryFindResource(Constants.DockingChrome.AnchorableMenuKey));
			Assert.Same(area.TryFindResource(Constants.DockingChrome.PaneStyleKey), area.Manager.AnchorablePaneControlStyle);
			Assert.NotNull(area.Manager.AnchorableContextMenu);
		});
	}

	[Fact]
	public void APane_HasNoCaptionBar_AndShowsItsOneTabOnTop_WithThePinAtTheEndOfTheRow()
	{
		StaTestRunner.Run(() =>
		{
			var area = NewArea();
			var window = Host(area);
			AddPanel(area, window, "images");

			Assert.Empty(Descendants<AnchorablePaneTitle>(area));   // no caption (title) bar
			var panel = Descendants<AnchorablePaneTabPanel>(area).Single();
			var tab = Assert.Single(Descendants<LayoutAnchorableTabItem>(area));   // the one tab is shown
			Assert.True(panel.IsVisible && tab.IsVisible);
			var content = Descendants<LayoutAnchorableControl>(area).Single();
			Assert.True(panel.TranslatePoint(new Point(0, 0), area).Y < content.TranslatePoint(new Point(0, 0), area).Y);   // tabs on top of the content
			var pin = Descendants<DockingPinButton>(area).Single();
			Assert.True(pin.IsVisible);
			Assert.Equal(panel.TranslatePoint(new Point(0, 0), area).Y, pin.TranslatePoint(new Point(0, 0), area).Y, 1.0);   // in the tab row
			Assert.True(pin.TranslatePoint(new Point(0, 0), area).X > tab.TranslatePoint(new Point(0, 0), area).X);   // at its right end
			window.Close();
		});
	}

	[Fact]
	public void ThePin_TogglesTheAutoHideOfThePane()
	{
		StaTestRunner.Run(() =>
		{
			var area = NewArea();
			var window = Host(area);
			var panel = AddPanel(area, window, "images");
			Assert.False(panel.IsAutoHidden);

			var pin = Descendants<DockingPinButton>(area).Single();
			((IInvokeProvider)new ButtonAutomationPeer(pin).GetPattern(PatternInterface.Invoke)!).Invoke();
			Settle(window);
			Assert.True(panel.IsAutoHidden);
			window.Close();
		});
	}

	private static List<MenuItem> VisibleItems(ContextMenu menu, object layoutItem)
	{
		menu.DataContext = layoutItem;
		menu.IsOpen = false;
		return menu.Items.OfType<MenuItem>().Where(i => i.Visibility == Visibility.Visible && i.IsEnabled).ToList();
	}

	[Fact]
	public void TheTabContextMenu_OffersFloatDockAutoHideAndClose_ForAPanel_AndNothingForTheSessionView()
	{
		StaTestRunner.Run(() =>
		{
			var area = NewArea();
			var window = Host(area);
			var panel = AddPanel(area, window, "images");
			var panelItem = area.Manager.GetLayoutItemFromModel(panel);
			var sessionItem = area.Manager.GetLayoutItemFromModel(area.DocumentPane.Children.OfType<LayoutDocument>().Single());
			Assert.NotNull(panelItem);
			Assert.NotNull(sessionItem);

			var panelItems = VisibleItems(area.Manager.AnchorableContextMenu, panelItem!).Select(i => i.Header).ToList();
			Assert.Contains("Float", panelItems);
			Assert.Contains("Auto-hide", panelItems);
			Assert.Contains("Close", panelItems);

			Assert.Empty(VisibleItems(area.Manager.DocumentContextMenu, sessionItem!));   // the Session view never closes and never floats (FR.6.14)

			panel.CanFloat = false;
			panel.CanAutoHide = false;
			panel.CanHide = false;
			CommandManager.InvalidateRequerySuggested();
			Settle(window);
			Assert.Empty(VisibleItems(area.Manager.AnchorableContextMenu, panelItem!).Where(i => (string)i.Header is "Float" or "Auto-hide" or "Close"));   // each only where allowed
			window.Close();
		});
	}

	[Fact]
	public void AFloatingPanelWindow_PassesUnhandledKeysToTheShellCommands()
	{
		StaTestRunner.Run(() =>
		{
			var area = NewArea();
			var commands = new RecordingCommands();
			area.Chrome.UseShellCommands(commands);
			var window = Host(area);
			var panel = AddPanel(area, window, "images");
			panel.Float();
			Settle(window);

			var floating = Assert.Single(area.Manager.FloatingWindows);
			floating.Show();
			var source = (HwndSource)PresentationSource.FromVisual(floating)!;
			var taken = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F7) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
			floating.RaiseEvent(taken);
			var left = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F8) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
			floating.RaiseEvent(left);

			Assert.Equal([Key.F7, Key.F8], commands.Gestures.Select(g => g.Key));
			Assert.True(taken.Handled);   // a bound gesture is taken, as in the main window
			Assert.False(left.Handled);
			floating.Close();
			window.Close();
		});
	}

	[Fact]
	public void AKeyAlreadyHandled_IsNotRoutedAgain()
	{
		StaTestRunner.Run(() =>
		{
			var commands = new RecordingCommands();
			var router = new DockingKeyboardRouter();
			router.UseCommands(commands);
			var window = new Window { Width = 100, Height = 100, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			router.Attach(window);
			window.Show();
			var source = (HwndSource)PresentationSource.FromVisual(window)!;
			window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F7) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Handled = true });
			Assert.Empty(commands.Gestures);
			window.Close();
		});
	}

	[Fact]
	public void TheFocusPolicy_CancelsOnlyLayoutDrivenMovesIntoAnotherPanel()
	{
		var now = 10_000L;
		var policy = new DockingFocusPolicy(() => now);
		Assert.False(policy.IsFocusSteal(true, true));   // no layout change yet: the user's own move

		policy.NoteLayoutChange();
		now += 50;
		Assert.True(policy.IsFocusSteal(true, true));    // the library follows a layout change
		Assert.False(policy.IsFocusSteal(true, false));  // stays in its panel
		Assert.False(policy.IsFocusSteal(false, true));  // nothing to steal from

		policy.NoteInput();                               // the user clicked into the panel
		Assert.False(policy.IsFocusSteal(true, true));

		now += 1000;
		policy.NoteLayoutChange();
		Assert.True(policy.IsFocusSteal(true, true));    // a later layout change without input
		now += 1000;
		Assert.False(policy.IsFocusSteal(true, true));   // the window has passed
	}

	private static bool MoveFocus(UIElement from, UIElement to)
	{
		var args = new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, from, to) { RoutedEvent = Keyboard.PreviewLostKeyboardFocusEvent, Source = from };
		from.RaiseEvent(args);
		return args.Handled;
	}

	private static (DockingArea Area, Window Window, TextBox Composer, TextBox PanelBox) FocusSetup()
	{
		var composer = new TextBox { Name = "Composer" };
		var area = new DockingArea(composer);
		var window = Host(area);
		var panel = AddPanel(area, window, "images");
		var panelBox = new TextBox();
		panel.Content = panelBox;
		Settle(window);
		return (area, window, composer, panelBox);
	}

	[Fact]
	public void ARealDockingChange_WithoutInput_StillBlocksAProgrammaticPanelToPanelFocusMove()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window, composer, panelBox) = FocusSetup();
			Thread.Sleep(450);   // the setup's own docking changes and input are out of the window
			AddPanel(area, window, "tags");   // a real docking change
			Assert.True(MoveFocus(composer, panelBox));
			window.Close();
		});
	}

	[Fact]
	public void AClickLikeInputOutsideTheDockingManager_JustBeforeTheMove_IsNotBlocked()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window, composer, panelBox) = FocusSetup();
			Thread.Sleep(450);
			AddPanel(area, window, "tags");
			var elsewhere = new Window { Width = 50, Height = 50, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };   // a floating panel, a popup, a menu
			elsewhere.Show();
			InputManager.Current.ProcessInput(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = elsewhere });
			Assert.False(MoveFocus(composer, panelBox));
			elsewhere.Close();
			window.Close();
		});
	}

	[Fact]
	public void AKeyPressOutsideTheDockingManager_JustBeforeTheMove_IsNotBlocked()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window, composer, panelBox) = FocusSetup();
			Thread.Sleep(450);
			AddPanel(area, window, "tags");
			var source = (HwndSource)PresentationSource.FromVisual(window)!;
			InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.A) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = composer });
			Assert.False(MoveFocus(composer, panelBox));
			window.Close();
		});
	}

	[Fact]
	public void ALayoutPassWithNoDockingChange_DoesNotArmTheGuard()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window, composer, panelBox) = FocusSetup();
			Thread.Sleep(450);
			for (var i = 0; i < 5; i++)
			{
				window.InvalidateMeasure();   // UIElement.LayoutUpdated fires on every pass: no docking change
				Settle(window);
			}

			Assert.False(MoveFocus(composer, panelBox));
			window.Close();
		});
	}

	private static void ShowSlideOut(DockingArea area, LayoutAnchorable panel)
	{
		var anchor = Descendants<LayoutAnchorControl>(area).Single(a => ReferenceEquals(a.Model, panel));
		var show = typeof(AvalonDock.DockingManager).GetMethod("ShowAutoHideWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
		show.Invoke(area.Manager, [anchor]);
	}

	[Fact]
	public void Esc_ClosesAnOpenSlideOut_AndAClickOnItsEdgeTabCloses_AndAgainOpensIt()
	{
		StaTestRunner.Run(() =>
		{
			var area = NewArea();
			var window = Host(area);
			var panel = AddPanel(area, window, "images");
			panel.ToggleAutoHide();
			Settle(window);
			Assert.True(area.Chrome.SlideOut.Available);

			ShowSlideOut(area, panel);
			Settle(window);
			Assert.True(area.Chrome.SlideOut.IsOpen);

			var esc = new KeyEventArgs(Keyboard.PrimaryDevice, (HwndSource)PresentationSource.FromVisual(window)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = area.Manager.AutoHideWindow };
			area.Manager.AutoHideWindow.RaiseEvent(esc);
			Assert.True(esc.Handled);
			Assert.False(area.Chrome.SlideOut.IsOpen);

			ShowSlideOut(area, panel);   // the library reopens it: it no longer thinks the panel is the open one
			Settle(window);
			Assert.True(area.Chrome.SlideOut.IsOpen);

			var anchor = Descendants<LayoutAnchorControl>(area).Single(a => ReferenceEquals(a.Model, panel));
			var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = anchor };
			anchor.RaiseEvent(click);
			Assert.True(click.Handled);
			Assert.False(area.Chrome.SlideOut.IsOpen);
			window.Close();
		});
	}

	[Fact]
	public void Esc_WithNoSlideOutOpen_IsLeftAlone()
	{
		StaTestRunner.Run(() =>
		{
			var area = NewArea();
			var window = Host(area);
			var esc = new KeyEventArgs(Keyboard.PrimaryDevice, (HwndSource)PresentationSource.FromVisual(window)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
			area.Manager.RaiseEvent(esc);
			Assert.False(esc.Handled);
			window.Close();
		});
	}

	[Fact]
	public void TheLibraryMethodTheSlideOutCloserNeeds_StillHasTheExpectedShape()
	{
		StaTestRunner.Run(() =>
		{
			var method = typeof(AvalonDock.DockingManager).GetMethod(Constants.DockingChrome.HideAutoHideWindowMethod, BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(LayoutAnchorControl)], null);
			Assert.NotNull(method);   // a library update that changes it must fail here, not at run time
			Assert.Equal(typeof(void), method!.ReturnType);

			var log = new CapturingLogger<DockingChromeViewTests>();
			var closer = new AutoHideWindowCloser(new AvalonDock.DockingManager());
			Assert.True(closer.Available);
			closer.UseLogger(log);
			Assert.Contains(log.Messages, m => m.Contains(Constants.DockingChrome.HideAutoHideWindowMethod));
		});
	}
}
