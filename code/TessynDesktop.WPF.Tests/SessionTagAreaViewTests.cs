using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The session tag area (FR.24) with real WPF layout: one line in mini, the clip and the "+N" count, up to ten lines in full, and the mouse gestures.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagAreaViewTests
{
	private const double Width = 400;

	private sealed class Fixture
	{
		public required FakeUrlLauncher Launcher { get; init; }
		public required ObservableCollection<SessionTagViewModel> Tags { get; init; }
		public required TagAreaViewModel Model { get; init; }
		public required SessionTagArea Area { get; init; }

		public void Settle() => SessionTagAreaViewTests.Settle(Area);

		public Button[] Chips() => Descendants<Button>(Area).ToArray();

		/// <summary>Distinct lines the chips occupy in the laid-out wrap panel (whatever the clip hides).</summary>
		public int Lines()
		{
			var panel = Descendants<WrapPanel>(Area).Single();
			return panel.Children.Cast<FrameworkElement>().Select(c => Math.Round(c.TranslatePoint(default, panel).Y)).Distinct().Count();
		}

		public int ChipsOnFirstLine()
		{
			var panel = Descendants<WrapPanel>(Area).Single();
			return panel.Children.Cast<FrameworkElement>().Count(c => c.TranslatePoint(default, panel).Y < 1);
		}

		public void Add(int count)
		{
			for (var i = 0; i < count; i++)
				Tags.Add(Chip(Tags.Count));
		}
	}

	private static SessionTagViewModel Chip(int index, FakeUrlLauncher? launcher = null) =>
		new(new SessionTagDto { Kind = SessionTagKind.GitHubPullRequest, Label = "repo#" + (1000 + index), Url = "https://github.com/o/r/pull/" + index }, launcher ?? new FakeUrlLauncher());

	private static Fixture Create(int tags, bool expanded = false)
	{
		var launcher = new FakeUrlLauncher();
		var collection = new ObservableCollection<SessionTagViewModel>(Enumerable.Range(0, tags).Select(i => Chip(i, launcher)));
		var model = new TagAreaViewModel(collection) { IsExpanded = expanded };
		var area = new SessionTagArea { DataContext = model };
		Host(area);
		var fixture = new Fixture { Launcher = launcher, Tags = collection, Model = model, Area = area };
		fixture.Settle();
		return fixture;
	}

	/// <summary>
	/// Lays the area out until nothing changes any more. The area sits in a real (never shown) top-level window, so it is connected to a layout
	/// manager like in the app: a collection change or a mode change re-measures it the way it does there.
	/// </summary>
	private static void Settle(FrameworkElement element)
	{
		for (var i = 0; i < 6; i++)
		{
			element.UpdateLayout();
			Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Render, () => { });
		}
	}

	private static readonly List<HwndSource> Hosts = [];

	private static void Host(SessionTagArea area)
	{
		var root = new Border { Width = Width, VerticalAlignment = VerticalAlignment.Top, Child = area };
		var source = new HwndSource(new HwndSourceParameters("tag-area-test", (int)Width, 600) { WindowStyle = 0 }) { SizeToContent = SizeToContent.Height, RootVisual = root };
		Hosts.Add(source);
	}

	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;

			foreach (var nested in Descendants<T>(child))
				yield return nested;
		}
	}

	private static FrameworkElement Named(SessionTagArea area, string name) => (FrameworkElement)area.FindName(name);

	private static MouseButtonEventArgs Press(UIElement target, int clickCount)
	{
		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
		{
			RoutedEvent = Mouse.PreviewMouseDownEvent,
			Source = target,
		};
		typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.GetSetMethod(nonPublic: true)!.Invoke(args, [clickCount]);
		target.RaiseEvent(args);
		return args;
	}

	private static void Run(Action action) => StaTestRunner.Run(() =>
	{
		try
		{
			action();
		}
		finally
		{
			foreach (var host in Hosts)
				host.Dispose();
			Hosts.Clear();
		}
	});

	// ---- mini ----

	[Fact]
	public void Mini_shows_exactly_one_line_and_clips_the_rest()
	{
		Run(() =>
		{
			var f = Create(60);

			Assert.True(f.Lines() > 3, "the fixture must overflow one line");
			Assert.True(f.Area.LineHeight > 0);
			Assert.Equal(f.Area.LineHeight, f.Area.AreaHeight, 1);
			Assert.Equal(f.Area.LineHeight, f.Area.DesiredSize.Height, 1);
			Assert.Equal(ScrollBarVisibility.Disabled, ((ScrollViewer)Named(f.Area, "Scroller")).VerticalScrollBarVisibility);
		});
	}

	[Fact]
	public void The_line_height_is_the_real_chip_height_plus_its_margin()
	{
		Run(() =>
		{
			var f = Create(5);

			var chip = f.Chips().First();
			var container = (FrameworkElement)VisualTreeHelper.GetParent(chip);

			Assert.Equal(chip.ActualHeight + chip.Margin.Top + chip.Margin.Bottom, f.Area.LineHeight, 1);
			Assert.Equal(container.ActualHeight, f.Area.LineHeight, 1);
		});
	}

	[Fact]
	public void The_plus_hint_counts_the_chips_the_line_hides()
	{
		Run(() =>
		{
			var f = Create(60);

			var onLine = f.ChipsOnFirstLine();
			Assert.InRange(onLine, 1, 59);
			Assert.Equal(60 - onLine, f.Area.HiddenCount);
			var hint = Named(f.Area, "HiddenHint");
			Assert.Equal(Visibility.Visible, hint.Visibility);
			Assert.Equal($"+{60 - onLine}", ((TextBlock)Named(f.Area, "HiddenHintText")).Text);
		});
	}

	[Fact]
	public void No_hint_shows_when_everything_fits_on_the_line()
	{
		Run(() =>
		{
			var f = Create(2);

			Assert.Equal(0, f.Area.HiddenCount);
			Assert.Equal(Visibility.Collapsed, Named(f.Area, "HiddenHint").Visibility);
			Assert.Equal(1, f.Lines());
		});
	}

	[Fact]
	public void Rows_without_tags_take_no_height()
	{
		Run(() =>
		{
			var f = Create(0);

			Assert.False(f.Area.HasTags);
			Assert.Equal(0, f.Area.DesiredSize.Height, 3);
			Assert.Equal(0, f.Area.AreaHeight);
			Assert.False(f.Area.IsTabStop);
			Assert.Equal(Visibility.Collapsed, Named(f.Area, "HiddenHint").Visibility);
		});
	}

	// ---- full ----

	[Fact]
	public void Full_grows_to_fit_up_to_ten_lines_without_a_scrollbar()
	{
		Run(() =>
		{
			var f = Create(24, expanded: true);

			var lines = f.Lines();
			Assert.InRange(lines, 2, 10);
			Assert.Equal(lines * f.Area.LineHeight, f.Area.AreaHeight, 1);
			var scroller = (ScrollViewer)Named(f.Area, "Scroller");
			Assert.Equal(0, scroller.ScrollableHeight, 1);
			Assert.Equal(0, f.Area.HiddenCount);
			Assert.Equal(Visibility.Collapsed, Named(f.Area, "HiddenHint").Visibility);
		});
	}

	[Fact]
	public void Full_stops_at_ten_lines_and_scrolls_vertically_beyond()
	{
		Run(() =>
		{
			var f = Create(300, expanded: true);

			Assert.True(f.Lines() > 10, "the fixture must exceed ten lines");
			Assert.Equal(Constants.TagArea.MaxFullLines * f.Area.LineHeight, f.Area.AreaHeight, 1);
			var scroller = (ScrollViewer)Named(f.Area, "Scroller");
			Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
			Assert.True(scroller.ScrollableHeight > 0);
			Assert.Equal(f.Lines() * f.Area.LineHeight, scroller.ExtentHeight, 1);
		});
	}

	[Fact]
	public void Tags_added_or_removed_update_the_layout_and_the_count()
	{
		Run(() =>
		{
			var f = Create(2);
			Assert.Equal(0, f.Area.HiddenCount);

			f.Add(58);
			f.Settle();
			var hiddenWith60 = f.Area.HiddenCount;
			Assert.Equal(60 - f.ChipsOnFirstLine(), hiddenWith60);
			Assert.True(hiddenWith60 > 0);

			for (var i = 0; i < 30; i++)
				f.Tags.RemoveAt(f.Tags.Count - 1);
			f.Settle();
			Assert.Equal(30 - f.ChipsOnFirstLine(), f.Area.HiddenCount);
			Assert.True(f.Area.HiddenCount < hiddenWith60);

			f.Tags.Clear();
			f.Settle();
			Assert.Equal(0, f.Area.HiddenCount);
			Assert.Equal(0, f.Area.AreaHeight);

			f.Add(1);
			f.Settle();
			Assert.True(f.Area.AreaHeight > 0);
		});
	}

	[Fact]
	public void Changing_the_mode_in_the_view_model_changes_the_layout()
	{
		Run(() =>
		{
			var f = Create(300);
			var mini = f.Area.AreaHeight;

			f.Model.IsExpanded = true;
			f.Settle();
			Assert.True(f.Area.AreaHeight > mini);

			f.Model.IsExpanded = false;
			f.Settle();
			Assert.Equal(mini, f.Area.AreaHeight, 1);
			Assert.Equal(0, ((ScrollViewer)Named(f.Area, "Scroller")).VerticalOffset);
		});
	}

	[Fact]
	public void Collapsing_a_scrolled_area_returns_to_the_first_line()
	{
		Run(() =>
		{
			var f = Create(300, expanded: true);
			var scroller = (ScrollViewer)Named(f.Area, "Scroller");
			scroller.ScrollToVerticalOffset(scroller.ScrollableHeight);
			f.Settle();
			Assert.True(scroller.VerticalOffset > 0);

			f.Model.IsExpanded = false;
			f.Settle();

			Assert.Equal(0, scroller.VerticalOffset);
		});
	}

	// ---- gestures ----

	[Fact]
	public void A_click_on_empty_space_expands_mini_to_full()
	{
		Run(() =>
		{
			var f = Create(60);

			Press(Named(f.Area, "Root"), 1);
			f.Settle();

			Assert.True(f.Model.IsExpanded);
			Assert.True(f.Area.AreaHeight > f.Area.LineHeight);
		});
	}

	[Fact]
	public void A_click_on_the_plus_hint_expands_too()
	{
		Run(() =>
		{
			var f = Create(60);

			Press(Named(f.Area, "HiddenHintText"), 1);

			Assert.True(f.Model.IsExpanded);
		});
	}

	[Fact]
	public void A_click_on_a_chip_does_not_expand_and_is_left_to_the_chip()
	{
		Run(() =>
		{
			var f = Create(60);
			var chip = f.Chips().First();

			var args = Press(chip, 1);
			var label = Descendants<TextBlock>(chip).First();
			var onLabel = Press(label, 1);

			Assert.False(f.Model.IsExpanded);
			Assert.False(args.Handled);
			Assert.False(onLabel.Handled);
			// The chip's own command still opens its link at once.
			chip.Command!.Execute(null);
			Assert.Equal(["https://github.com/o/r/pull/0"], f.Launcher.Opened);
		});
	}

	[Fact]
	public void A_click_on_empty_space_in_full_mode_does_nothing()
	{
		Run(() =>
		{
			var f = Create(60, expanded: true);

			Press(Named(f.Area, "Root"), 1);

			Assert.True(f.Model.IsExpanded);
		});
	}

	[Fact]
	public void A_double_click_on_empty_space_in_full_mode_collapses_to_mini()
	{
		Run(() =>
		{
			var f = Create(60, expanded: true);

			Press(Named(f.Area, "Root"), 1);
			Press(Named(f.Area, "Root"), 2);
			f.Settle();

			Assert.False(f.Model.IsExpanded);
			Assert.Equal(f.Area.LineHeight, f.Area.AreaHeight, 1);
		});
	}

	[Fact]
	public void A_double_click_on_a_chip_in_full_mode_does_not_collapse_only_empty_space_does()
	{
		Run(() =>
		{
			var f = Create(60, expanded: true);
			var chip = f.Chips().First();

			Press(chip, 1);
			var second = Press(chip, 2);

			Assert.True(f.Model.IsExpanded);
			Assert.False(second.Handled, "a chip press keeps its own meaning");
		});
	}

	[Fact]
	public void A_double_click_on_empty_space_in_mini_ends_expanded_not_collapsed_again()
	{
		Run(() =>
		{
			var f = Create(60);

			Press(Named(f.Area, "Root"), 1);
			var second = Press(Named(f.Area, "Root"), 2);

			Assert.True(f.Model.IsExpanded);
			Assert.True(second.Handled);

			// A later, separate double-click collapses as usual.
			Press(Named(f.Area, "Root"), 1);
			Press(Named(f.Area, "Root"), 2);
			Assert.False(f.Model.IsExpanded);
		});
	}

	[Fact]
	public void A_triple_click_on_empty_space_in_mini_ends_expanded()
	{
		Run(() =>
		{
			var f = Create(60);

			Press(Named(f.Area, "Root"), 1);
			var second = Press(Named(f.Area, "Root"), 2);
			var third = Press(Named(f.Area, "Root"), 3);

			Assert.True(f.Model.IsExpanded);
			Assert.True(second.Handled);
			Assert.True(third.Handled);
		});
	}

	[Fact]
	public void A_double_click_on_the_scrollbar_neither_collapses_nor_swallows_the_press()
	{
		Run(() =>
		{
			var f = Create(60, expanded: true);
			var scrollBar = Descendants<ScrollBar>(f.Area).Single(s => s.Orientation == Orientation.Vertical);

			var first = Press(scrollBar, 1);
			var second = Press(scrollBar, 2);

			Assert.True(f.Model.IsExpanded);
			Assert.False(first.Handled);
			Assert.False(second.Handled, "the scrollbar must still receive the second press");
		});
	}

	[Fact]
	public void The_plus_hint_never_covers_a_chip_of_the_line()
	{
		Run(() =>
		{
			var f = Create(60);
			var hint = Named(f.Area, "HiddenHint");
			Assert.Equal(Visibility.Visible, hint.Visibility);
			var hintLeft = hint.TranslatePoint(default, f.Area).X;

			var panel = Descendants<WrapPanel>(f.Area).Single();
			var firstLine = panel.Children.Cast<FrameworkElement>().Where(c => c.TranslatePoint(default, panel).Y < 1).ToList();
			Assert.NotEmpty(firstLine);
			foreach (var chip in firstLine)
			{
				var right = chip.TranslatePoint(new Point(chip.ActualWidth, 0), f.Area).X;
				Assert.True(right <= hintLeft + 0.5, $"chip ends at {right}, hint starts at {hintLeft}");
			}
		});
	}

	[Fact]
	public void Expand_through_automation_without_tags_changes_nothing()
	{
		Run(() =>
		{
			var f = Create(0);
			var provider = (IExpandCollapseProvider)UIElementAutomationPeer.CreatePeerForElement(f.Area).GetPattern(PatternInterface.ExpandCollapse)!;

			provider.Expand();

			Assert.False(f.Model.IsExpanded);
		});
	}

	[Fact]
	public void A_double_click_in_mini_does_not_expand_on_a_chip()
	{
		Run(() =>
		{
			var f = Create(60);
			var chip = f.Chips().First();

			Press(chip, 1);
			Press(chip, 2);

			Assert.False(f.Model.IsExpanded);
		});
	}

	[Fact]
	public void Gestures_do_nothing_without_tags()
	{
		Run(() =>
		{
			var f = Create(0);

			Press(Named(f.Area, "Root"), 1);

			Assert.False(f.Model.IsExpanded);
		});
	}

	// ---- UI Automation ----

	[Fact]
	public void The_area_exposes_expand_and_collapse_through_UI_Automation()
	{
		Run(() =>
		{
			var f = Create(60);
			var peer = UIElementAutomationPeer.CreatePeerForElement(f.Area);
			var provider = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));

			Assert.Equal(Constants.TagArea.AutomationName, peer.GetName());
			Assert.Equal(Constants.TagArea.AutomationId, peer.GetAutomationId());
			Assert.Equal(ExpandCollapseState.Collapsed, provider.ExpandCollapseState);

			provider.Expand();
			f.Settle();
			Assert.Equal(ExpandCollapseState.Expanded, provider.ExpandCollapseState);
			Assert.True(f.Model.IsExpanded);

			provider.Collapse();
			Assert.Equal(ExpandCollapseState.Collapsed, provider.ExpandCollapseState);
			Assert.False(f.Model.IsExpanded);
		});
	}

	[Fact]
	public void Without_tags_the_area_is_a_leaf_and_not_a_control_element()
	{
		Run(() =>
		{
			var f = Create(0);
			var peer = UIElementAutomationPeer.CreatePeerForElement(f.Area);

			var provider = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)!;

			Assert.Equal(ExpandCollapseState.LeafNode, provider.ExpandCollapseState);
			Assert.False(peer.IsControlElement());
		});
	}

	// ---- in the session pane ----

	[Fact]
	public void The_session_pane_shows_its_tags_through_the_tag_area()
	{
		Run(() =>
		{
			var client = new FakeTessynClient();
			var launcher = new FakeUrlLauncher();
			var pane = new SessionPaneViewModel(client, new FakeTranscriptSurface(), new TessynDesktop.WPF.Rendering.MarkdigEntryRenderer(new TessynDesktop.WPF.Services.UrlPolicy()),
				launcher, new ImmediateUiDispatcher(),
				new TessynDesktop.WPF.Infrastructure.SwitchLatencyTracker(Microsoft.Extensions.Logging.Abstractions.NullLogger<TessynDesktop.WPF.Infrastructure.SwitchLatencyTracker>.Instance),
				Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionPaneViewModel>.Instance);
			for (var i = 0; i < 50; i++)
				pane.Tags.Add(Chip(i, launcher));
			var view = new SessionPane { DataContext = pane };
			view.Measure(new Size(700, 600));
			view.Arrange(new Rect(0, 0, 700, 600));
			view.UpdateLayout();

			var area = Descendants<SessionTagArea>(view).Single();

			Assert.True(area.HasTags);
			Assert.Same(pane.TagArea, area.DataContext);
			Assert.True(area.HiddenCount > 0);
		});
	}
}
