using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock.Controls;
using AvalonDock.Layout;
using AvalonDock.Themes;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.29.19 (stage 1): the main window's docking area on an STA thread: the session pane is the single, non-closable, non-floatable document without a tab strip; panels dock around it; the theme follows light and dark.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingAreaViewTests
{
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

	/// <summary>Shows the area in an off-screen window (the docking manager builds its controls when it is loaded) and lets the dispatcher settle.</summary>
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

	[Fact]
	public void TheSessionPane_IsTheSingleNonClosableNonFloatableDocument_WithoutATabStrip()
	{
		StaTestRunner.Run(() =>
		{
			var area = new DockingArea(new Border { Name = "Stub", MinWidth = 100 });
			var window = Host(area);

			var document = Assert.Single(area.Root.Descendents().OfType<LayoutDocument>());
			Assert.False(document.CanClose);
			Assert.False(document.CanFloat);
			Assert.Same(area.SessionView, document.Content);
			Assert.Same(document, Assert.Single(area.DocumentPane.Children));
			Assert.All(Descendants<DocumentPaneTabPanel>(area), p => Assert.False(p.IsVisible));   // no tab header of the one document
			Assert.Contains(area.SessionView, Descendants<Border>(area));   // but the pane is shown
			Assert.True(area.SessionView.IsVisible);
			Assert.True(area.SessionView.ActualWidth > 600);
			window.Close();
		});
	}

	[Fact]
	public void ADockedPanel_SitsBesideTheSessionPane_AndTheSessionPaneKeepsTheRest()
	{
		StaTestRunner.Run(() =>
		{
			var area = new DockingArea(new Border { Name = "Stub", MinWidth = 100 });
			var window = Host(area);
			var panel = new LayoutAnchorable { ContentId = "images", Title = "images", Content = new System.Windows.Controls.TextBlock { Text = "panel" } };
			new PreviewDockLayoutApplier().Apply(area.Root, area.DocumentPane, panel, null, area.AreaWidth, area.AreaHeight);
			Settle(window);

			Assert.NotEmpty(Descendants<LayoutAnchorableControl>(area));
			Assert.Equal(PreviewDockSide.Right, new PreviewDockLayoutReader().Read(panel, area.DocumentPane).Side);
			Assert.True(area.SessionView.ActualWidth < 1200 && area.SessionView.ActualWidth > 400);
			var panelControl = Descendants<LayoutAnchorableControl>(area).Single();
			Assert.True(panelControl.TranslatePoint(new Point(0, 0), area).X > area.SessionView.TranslatePoint(new Point(0, 0), area).X);   // right of it
			Assert.InRange(panelControl.ActualWidth, 400, 560);   // FR.29.21: 40 % of the area the first time
			window.Close();
		});
	}

	[Fact]
	public void TheTheme_FollowsLightAndDark()
	{
		StaTestRunner.Run(() =>
		{
			var area = new DockingArea(new Border { Name = "Stub", MinWidth = 100 });
			Assert.Null(area.Manager.Theme);   // the main window sets it once, from the stored look (no throwaway light theme in dark mode)

			area.ApplyTheme(dark: true);
			Assert.IsType<Vs2013DarkTheme>(area.Manager.Theme);

			area.ApplyTheme(dark: false);
			Assert.IsType<Vs2013LightTheme>(area.Manager.Theme);
		});
	}

	[Fact]
	public void TheDockingArea_ImplementsTheSurface_AndSaysWhenTheLayoutChanges()
	{
		StaTestRunner.Run(() =>
		{
			var area = new DockingArea(new Border { Name = "Stub", MinWidth = 100 });
			var window = Host(area);
			IDockingSurface surface = area;
			var changes = 0;
			surface.LayoutChanged += (_, _) => changes++;
			Assert.True(surface.AreaWidth > 1000);

			new PreviewDockLayoutApplier().Apply(area.Root, area.DocumentPane, new LayoutAnchorable { ContentId = "x", Content = "x" }, null, 1200, 700);
			Settle(window);

			Assert.True(changes > 0);
			window.Close();
		});
	}

	[Fact]
	public void TheTabStrip_ShowsWhileTheDocumentPaneHoldsMoreThanTheSession_AndHidesAgain()
	{
		StaTestRunner.Run(() =>
		{
			var area = new DockingArea(new Border { MinWidth = 100 });
			area.ApplyTheme(dark: false);
			var window = Host(area);
			var strips = () => Descendants<DocumentPaneTabPanel>(area).ToList();
			Assert.NotEmpty(strips());
			Assert.All(strips(), p => Assert.False(p.IsVisible));

			var preview = new LayoutAnchorable { ContentId = "images", Title = "images", Content = new TextBlock { Text = "panel" } };
			area.DocumentPane.InsertChildAt(1, preview);   // dropped onto the centre of the session pane
			Settle(window);
			Assert.All(strips(), p => Assert.True(p.IsVisible));   // the user can select the tabs and drag the preview out
			var session = area.Root.Descendents().OfType<LayoutDocument>().Single();
			Assert.False(session.CanClose);
			Assert.False(session.CanFloat);

			area.DocumentPane.RemoveChild(preview);
			Settle(window);
			Assert.All(strips(), p => Assert.False(p.IsVisible));

			area.DocumentPane.InsertChildAt(1, preview);   // and again after a theme switch
			area.ApplyTheme(dark: true);
			Settle(window);
			Assert.All(strips(), p => Assert.True(p.IsVisible));
			window.Close();
		});
	}
}
