using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AvalonDock.Controls;
using AvalonDock.Layout;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The real docking area on an STA thread: a new item brings a panel forward without the keyboard focus (FR.29.17, decision 3), Esc from inside a slide-out closes it wherever the key
/// came from (decision 4), and the shell layout's floating panels are saved and restored with their monitor and scale, a forced move never saved (FR.6.18, FR.29.16, FR.29.21).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingAreaRevealAndFloatingTests
{
	private static readonly MonitorBounds Screen = new(0, 0, 1920, 1080);

	private sealed class ScriptedPlacement : IFloatingPanelPlacement
	{
		public bool IsSettling { get; set; }

		public PreviewPlacementModel? Now { get; set; }

		public List<PreviewPlacementModel> Planned { get; } = [];

		public PreviewPlacementModel? Capture(IntPtr hwnd) => Now?.Clone();

		public WindowPlacementModel Plan(PreviewPlacementModel saved)
		{
			Planned.Add(saved);
			return new WindowPlacementModel { Left = 111, Top = 222, Width = 444, Height = 333 };
		}

		public WindowPlacementModel ToLibraryUnits(PreviewPlacementModel pixels) => new() { Left = pixels.Left / 2, Top = pixels.Top / 2, Width = pixels.Width / 2, Height = pixels.Height / 2 };
	}

	private sealed class Rig
	{
		public Rig(MemoryClientState? state = null, ScriptedPlacement? placement = null)
		{
			State = state ?? new MemoryClientState();
			Settings = new ClientSettingsStore(State, NullLogger<ClientSettingsStore>.Instance);
			Placement = placement ?? new ScriptedPlacement();
			Area = new DockingArea(Session);
			Window = new Window { Content = Area, Width = 1400, Height = 800, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			Area.BuildShell(Panels(), new ShellLayoutMemory(Settings, State, true), Log, () => ([Screen], Screen), Placement);
		}

		public Border Session { get; } = new() { MinWidth = 100 };

		public MemoryClientState State { get; }

		public ClientSettingsStore Settings { get; }

		public ScriptedPlacement Placement { get; }

		public CapturingLogger<DockingArea> Log { get; } = new();

		public DockingArea Area { get; }

		public Window Window { get; }

		public Rig Show()
		{
			Window.Show();
			Settle();
			return this;
		}

		public void Settle()
		{
			Window.UpdateLayout();
			Window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
			Window.UpdateLayout();
		}

		public LayoutAnchorable Panel(string id) => Area.FindPanel(id)!;

		public string SavedLayout() => Settings.Get<DockedShellLayoutSettings>().Layout ?? string.Empty;

		public void Stop()
		{
			Area.FlushLayout();
			Window.Close();
		}

		private static List<ShellPanelDefinition> Panels() =>
		[
			Definition(Constants.DockedShell.SessionsPanelId, "Sessions", true),
			Definition(Constants.DockedShell.RecentPanelId, "Recent", true),
			Definition(Constants.DockedShell.TagsPanelId, "Tags", true),
			Definition(Constants.DockedShell.FocusPanelId, "Focus", false),
		];

		private static ShellPanelDefinition Definition(string id, string title, bool left) => new() { Id = id, Title = title, Content = new TextBox { Text = id }, IsLeft = left };
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

	private static LayoutAnchorable Preview(string id) => new() { ContentId = id, Title = id, Content = new TextBlock { Text = id }, CanClose = true, CanFloat = true, CanAutoHide = true };

	// ---- decision 3 ----

	[Fact]
	public void Reveal_SlidesOutAnAutoHiddenPanel_WithoutMovingTheKeyboardFocus()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Show();
			var composer = new TextBox();
			rig.Session.Child = composer;
			var panel = Preview("images");
			new PreviewDockLayoutApplier().Apply(rig.Area.Root, rig.Area.DocumentPane, panel, new PreviewDockLayoutModel { Side = PreviewDockSide.Right, Size = 300, AutoHide = true }, 1400, 800);
			rig.Settle();
			Assert.True(panel.IsAutoHidden);
			Assert.False(rig.Area.Chrome.SlideOut.IsOpen);
			composer.Focus();
			var focusedBefore = Keyboard.FocusedElement;

			rig.Area.Reveal(panel);
			rig.Settle();

			Assert.True(rig.Area.Chrome.SlideOut.IsOpen);
			Assert.Same(focusedBefore, Keyboard.FocusedElement);   // the composer keeps the keyboard
			Assert.False(rig.Area.Manager.AutoHideWindow.IsKeyboardFocusWithin);
			rig.Window.Close();
		});
	}

	[Fact]
	public void Reveal_SelectsATabBehindAnotherPanel_AndRaisesAFloatingOneWithoutActivatingIt()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Show();
			var applier = new PreviewDockLayoutApplier();
			var first = Preview("a");
			var second = Preview("b");
			applier.Apply(rig.Area.Root, rig.Area.DocumentPane, first, new PreviewDockLayoutModel { Side = PreviewDockSide.Right, Size = 300 }, 1400, 800);
			applier.Apply(rig.Area.Root, rig.Area.DocumentPane, second, new PreviewDockLayoutModel { Side = PreviewDockSide.Right, Size = 300, StackedWith = ["a"], TabIndex = 1 }, 1400, 800);
			rig.Settle();
			first.IsSelected = true;
			Assert.False(second.IsSelected);

			rig.Area.Reveal(second);

			Assert.True(second.IsSelected);

			var floating = Preview("c");
			applier.Apply(rig.Area.Root, rig.Area.DocumentPane, floating, new PreviewDockLayoutModel { Floating = true, FloatingPlacement = new PreviewPlacementModel { Left = -3000, Top = -2900, Width = 300, Height = 300 } }, 1400, 800);
			rig.Settle();
			Assert.True(floating.IsFloating);
			var window = rig.Area.Manager.FloatingWindows.Single();
			Assert.NotEqual(IntPtr.Zero, rig.Area.FloatingWindowHandle(floating));
			var activeBefore = window.IsActive;

			rig.Area.Reveal(floating);

			Assert.Equal(activeBefore, window.IsActive);   // raised, never activated by the reveal
			rig.Window.Close();
		});
	}

	[Fact]
	public void FloatingWindowHandle_IsZero_ForAPanelThatDoesNotFloat()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Show();

			Assert.Equal(IntPtr.Zero, rig.Area.FloatingWindowHandle(rig.Panel(Constants.DockedShell.FocusPanelId)));
			rig.Window.Close();
		});
	}

	// ---- decision 4: Esc over a page ----

	[Fact]
	public void Esc_RaisedFromAnElementInsideTheSlideOut_ClosesIt_WhereverTheKeyCameFrom()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Show();
			var page = new Border { Name = "StandsForAWebPage", MinHeight = 50 };
			var panel = Preview("web");
			panel.Content = page;
			new PreviewDockLayoutApplier().Apply(rig.Area.Root, rig.Area.DocumentPane, panel, new PreviewDockLayoutModel { Side = PreviewDockSide.Right, Size = 300, AutoHide = true }, 1400, 800);
			rig.Settle();
			rig.Area.Reveal(panel);
			rig.Settle();
			Assert.True(rig.Area.Chrome.SlideOut.IsOpen);
			Assert.True(page.IsVisible);

			var esc = new KeyEventArgs(Keyboard.PrimaryDevice, (HwndSource)PresentationSource.FromVisual(page)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
			page.RaiseEvent(esc);   // what BrowserPreviewView does when the browser reports Esc

			Assert.True(esc.Handled);
			Assert.False(rig.Area.Chrome.SlideOut.IsOpen);
			rig.Window.Close();
		});
	}

	[Fact]
	public void TheBrowsersEscapeTap_NeedsTheControllerFieldOfTheWpfControl_ItIsStillThere()
	{
		var field = typeof(Microsoft.Web.WebView2.Wpf.WebView2).GetField(WebViewEscapeTap.BaseField, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

		Assert.NotNull(field);   // a WebView2 update that renames it must fail here, not at run time
		var property = field!.FieldType.GetProperty(WebViewEscapeTap.ControllerProperty, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(property);
		Assert.Equal(typeof(Microsoft.Web.WebView2.Core.CoreWebView2Controller), property!.PropertyType);
	}

	// ---- FR.6.18, FR.29.16: the shell layout's floating panels ----

	private static PreviewPlacementModel Saved(int left, string monitor = @"\\.\DISPLAY2", int dpi = 144) =>
		new() { Left = left, Top = 120, Width = 900, Height = 700, MonitorDeviceName = monitor, MonitorLeft = 1920, MonitorTop = 0, Dpi = dpi };

	private static LayoutAnchorable FloatFocus(Rig rig)
	{
		var focus = rig.Panel(Constants.DockedShell.FocusPanelId);
		focus.FloatingLeft = -3000;
		focus.FloatingTop = -2900;
		focus.FloatingWidth = 500;
		focus.FloatingHeight = 400;
		focus.Float();
		rig.Settle();
		return focus;
	}

	[Fact]
	public void AFloatingShellPanel_IsSavedWithItsMonitorAndScale_AndRestoredByThePlacementRules()
	{
		StaTestRunner.Run(() =>
		{
			var first = new Rig().Show();
			first.Placement.Now = Saved(2100);
			FloatFocus(first);
			first.Area.Close(Constants.DockedShell.RecentPanelId);   // any change makes the layout read and saved
			first.Settle();
			first.Stop();

			var xml = first.SavedLayout();
			Assert.Contains("TessynMonitor=\"\\\\.\\DISPLAY2\"", xml);
			Assert.Contains("TessynDpi=\"144\"", xml);
			Assert.Contains("TessynPixelLeft=\"2100\"", xml);
			Assert.Contains("FloatingLeft=\"1050\"", xml);   // the library's units: what the scripted rules say

			var second = new Rig(first.State);
			var restored = second.Panel(Constants.DockedShell.FocusPanelId);
			Assert.True(restored.IsFloating);
			Assert.Equal((111, 222, 444, 333), (restored.FloatingLeft, restored.FloatingTop, restored.FloatingWidth, restored.FloatingHeight));
			Assert.Equal(2100, Assert.Single(second.Placement.Planned).Left);
			Assert.Contains(second.Log.Messages, m => m.Contains("restored by the placement rules"));
		});
	}

	[Fact]
	public void AMoveWindowsForces_IsNotSavedForAFloatingShellPanel()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Show();
			rig.Placement.Now = Saved(2100);
			FloatFocus(rig);
			rig.Area.Close(Constants.DockedShell.RecentPanelId);
			rig.Settle();
			rig.Area.FlushLayout();
			Assert.Contains("TessynPixelLeft=\"2100\"", rig.SavedLayout());

			rig.Placement.IsSettling = true;   // the second monitor was unplugged: Windows pulls the window onto the first
			rig.Placement.Now = Saved(200, @"\\.\DISPLAY1", 96);
			rig.Area.Close(Constants.DockedShell.TagsPanelId);   // another change, saved while Windows is still rearranging
			rig.Settle();
			rig.Area.FlushLayout();
			rig.Placement.IsSettling = false;
			rig.Area.ToggleAutoHide(Constants.DockedShell.SessionsPanelId);   // and another one after the display settled
			rig.Settle();
			rig.Area.FlushLayout();

			var xml = rig.SavedLayout();
			Assert.Contains("TessynPixelLeft=\"2100\"", xml);
			Assert.DoesNotContain("TessynPixelLeft=\"200\"", xml);
			Assert.Contains("TessynMonitor=\"\\\\.\\DISPLAY2\"", xml);
			rig.Window.Close();
		});
	}

	[Fact]
	public void AnOlderSavedLayout_WithoutMonitorAndScale_StaysReadable_AndIsOnlyClamped()
	{
		StaTestRunner.Run(() =>
		{
			var first = new Rig().Show();
			FloatFocus(first);
			first.Area.Close(Constants.DockedShell.RecentPanelId);
			first.Settle();
			first.Stop();
			var withoutPlacement = System.Text.RegularExpressions.Regex.Replace(first.SavedLayout(), " Tessyn\\w+=\"[^\"]*\"", string.Empty);   // as saved before the attributes existed
			first.Settings.Update<DockedShellLayoutSettings>(s => s.Layout = System.Text.RegularExpressions.Regex.Replace(withoutPlacement, "Floating(Left|Top)=\"[-0-9.]+\"", "Floating$1=\"-40000\""));

			var second = new Rig(first.State);

			var restored = second.Panel(Constants.DockedShell.FocusPanelId);
			Assert.True(restored.IsFloating);
			Assert.Empty(second.Placement.Planned);   // no scale known: no conversion
			Assert.InRange(restored.FloatingLeft, 0, 1920);   // only clamped, as before
			Assert.Contains(second.Log.Messages, m => m.Contains("off every monitor"));
		});
	}

	[Fact]
	public void ASavedLayoutOfAnOlderBuild_WithNoFloatingPanel_RoundTripsWithoutTheNewAttributes()
	{
		StaTestRunner.Run(() =>
		{
			var first = new Rig().Show();
			first.Area.Close(Constants.DockedShell.RecentPanelId);
			first.Settle();
			first.Stop();

			Assert.DoesNotContain("TessynMonitor", first.SavedLayout());
			var second = new Rig(first.State);
			Assert.Contains("recent=closed", second.Area.StateText());
		});
	}
}
