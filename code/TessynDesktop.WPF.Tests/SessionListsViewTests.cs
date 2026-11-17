using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The Recent tab, Tags tab, Recent dropdown and header star as real WPF controls: they bind without errors and show their rows and buttons (W3).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionListsViewTests
{
	private sealed class BindingErrors : TraceListener
	{
		public List<string> Messages { get; } = [];

		public override void Write(string? message) => Capture(message);

		public override void WriteLine(string? message) => Capture(message);

		private void Capture(string? message)
		{
			if (message != null && message.Contains("BindingExpression", StringComparison.Ordinal))
				lock (Messages)
					Messages.Add(message);
		}
	}

	private sealed class Actions : ISessionListActions
	{
		public List<string> Opened { get; } = [];

		public void Open(string nodeId, string name) => Opened.Add(nodeId);

		public Task<bool?> SetFavoriteAsync(string nodeId, bool value) => Task.FromResult<bool?>(value);
	}

	private static SessionStateDto State(string id, bool favorite = false, bool running = false, DateTimeOffset? activity = null) =>
		new(id, running, false, null, favorite, activity ?? DateTimeOffset.UtcNow, null, false, null);

	private static void Layout(FrameworkElement element)
	{
		element.Measure(new Size(400, 600));
		element.Arrange(new Rect(0, 0, 400, 600));
		element.UpdateLayout();
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

	private static List<string> ButtonNames(DependencyObject root) =>
		Descendants<ButtonBase>(root).Where(b => b.Visibility == Visibility.Visible).Select(b => AutomationProperties.GetName(b)).Where(n => n.Length > 0).ToList();

	private static void WithBindingCheck(Action body)
	{
		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			body();
		});

		Assert.Empty(listener.Messages);
	}

	private static async Task<(RecentTabViewModel Tab, TreeViewModel Tree, FakeTessynClient Client, Actions Actions)> TabAsync(int sessions = 2)
	{
		var builder = new TreeBuilder();
		var client = new FakeTessynClient();
		client.Directories = [builder.Directory("d1", "Repo", [], Enumerable.Range(0, sessions).Select(i =>
			builder.SessionWith(State("s" + i, favorite: i == 1, running: i == 0, activity: DateTimeOffset.UtcNow.AddMinutes(-i)), "Session " + i)).ToArray())];
		var tree = new TreeViewModel(client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		var actions = new Actions();
		var tab = new RecentTabViewModel(actions, new ImmediateUiDispatcher(), NullLogger<RecentTabViewModel>.Instance, TimeSpan.Zero);
		tab.Attach(tree);
		return (tab, tree, client, actions);
	}

	[Fact]
	public void RecentPanel_ShowsGroupHeaderRowsAndTheOneIconSlot()
	{
		WithBindingCheck(() =>
		{
			var (tab, _, _, actions) = TabAsync().GetAwaiter().GetResult();
			var panel = new RecentPanel { DataContext = tab };

			Layout(panel);

			var names = ButtonNames(panel);
			Assert.Contains("Group Repo", names);
			Assert.Contains("Open Session 0", names);
			Assert.Contains("Favourite Session 0", names);   // not a favourite: a star to set
			Assert.Contains("Favourite Session 1", names);   // a favourite: the filled star
			Assert.DoesNotContain(names, n => n.StartsWith("Hide ", StringComparison.Ordinal));   // the tab has no Hide; the dropdown does

			Descendants<Button>(panel).Single(b => AutomationProperties.GetName(b) == "Open Session 1").Command.Execute(tab.List.Items.OfType<SessionRowViewModel>().Single(r => r.Id == "s1"));
			Assert.Equal(["s1"], actions.Opened);
		});
	}

	[Fact]
	public void RecentPanel_HeaderButtonCollapsesAndExpandsItsGroup()
	{
		WithBindingCheck(() =>
		{
			var (tab, _, _, _) = TabAsync().GetAwaiter().GetResult();
			var panel = new RecentPanel { DataContext = tab };
			Layout(panel);
			var header = Descendants<Button>(panel).Single(b => AutomationProperties.GetName(b) == "Group Repo");

			header.Command.Execute(header.CommandParameter);
			Layout(panel);
			Assert.DoesNotContain("Open Session 0", ButtonNames(panel));

			header.Command.Execute(header.CommandParameter);
			Layout(panel);
			Assert.Contains("Open Session 0", ButtonNames(panel));
		});
	}

	[Fact]
	public void RecentPanel_EachTimeItIsShown_CollapsesAllButTheSelectedSessionsDirectory()
	{
		var builder = new TreeBuilder();
		var client = new FakeTessynClient { Directories = [builder.Directory("d1", "One", [], builder.Session("a", "A")), builder.Directory("d2", "Two", [], builder.Session("b", "B"))] };
		StaTestRunner.Run(() =>
		{
			var tree = new TreeViewModel(client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
			tree.LoadAsync().GetAwaiter().GetResult();
			var tab = new RecentTabViewModel(new Actions(), new ImmediateUiDispatcher(), NullLogger<RecentTabViewModel>.Instance, TimeSpan.Zero);
			tab.Attach(tree);
			tree.SelectNodeById("b");
			var panel = new RecentPanel { DataContext = tab };
			var window = new Window { Content = panel, Width = 300, Height = 300, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			window.Show();
			try
			{
				window.UpdateLayout();   // Loaded: the tab was shown

				Assert.Equal(["Group One", "Group Two", "Open B", "Favourite B"], ButtonNames(panel));
			}
			finally
			{
				window.Close();
			}
		});
	}

	[Fact]
	public void RecentPanel_WithNoSessions_ShowsAHint()
	{
		WithBindingCheck(() =>
		{
			var panel = new RecentPanel { DataContext = new RecentTabViewModel(new Actions(), new ImmediateUiDispatcher(), NullLogger<RecentTabViewModel>.Instance, TimeSpan.Zero) };

			Layout(panel);

			Assert.Contains(Descendants<TextBlock>(panel), t => t.Text == "No sessions yet." && t.Visibility == Visibility.Visible);
		});
	}

	[Fact]
	public void RecentPanel_VirtualisesALongList()
	{
		WithBindingCheck(() =>
		{
			var (tab, _, _, _) = TabAsync(sessions: 600).GetAwaiter().GetResult();
			var panel = new RecentPanel { DataContext = tab };

			Layout(panel);

			Assert.Equal(601, tab.List.Items.Count);
			var realised = Descendants<ListBoxItem>(panel).Count();
			Assert.InRange(realised, 1, 60);
		});
	}

	[Fact]
	public void TagsPanel_SessionListVirtualisesAndTheChipsAreHeightCapped()
	{
		WithBindingCheck(() =>
		{
			var tags = new TagSearchViewModel(new Actions(), new ImmediateUiDispatcher(), NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);
			var client = new FakeTessynClient
			{
				TagsSearchHandler = (_, _) => new TagSearchResult(null,
					Enumerable.Range(0, 300).Select(i => new TagChipDto("t" + i, "TAG-" + i, "u", SessionTagKind.LinearIssue, 1, false)).ToList(), null,
					[new SessionGroupDto("d", "Repo", Enumerable.Range(0, 500).Select(i => new SessionRowDto("s" + i, "Session " + i, State("s" + i))).ToList())]),
			};
			tags.AttachAsync(client).GetAwaiter().GetResult();
			var panel = new TagsPanel { DataContext = tags };

			Layout(panel);

			Assert.Equal(501, tags.Sessions.Items.Count);
			Assert.InRange(Descendants<ListBoxItem>(panel).Count(), 1, 80);
			Assert.True(Descendants<GridSplitter>(panel).Any(), "chips and sessions are separated by a splitter");
			var chipScroller = Descendants<ScrollViewer>(panel).First();
			Assert.True(chipScroller.ActualHeight < 300, "the chip region scrolls inside its own height");
		});
	}

	[Fact]
	public void TagsPanel_HasTheScopeToggleAndTheSearchWatermark()
	{
		WithBindingCheck(() =>
		{
			var tags = new TagSearchViewModel(new Actions(), new ImmediateUiDispatcher(), NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);
			var panel = new TagsPanel { DataContext = tags };

			Layout(panel);

			var scope = Descendants<ToggleButton>(panel).Single(b => AutomationProperties.GetName(b) == Constants.SessionLists.ScopeToggleName);
			Assert.False(scope.IsEnabled);   // nothing selected: the scope is forced off
			Assert.Contains(Descendants<TextBlock>(panel), t => t.Text == "Search tags…" && t.Visibility == Visibility.Visible);
			tags.SearchText = "x";
			Layout(panel);
			Assert.DoesNotContain(Descendants<TextBlock>(panel), t => t.Text == "Search tags…" && t.Visibility == Visibility.Visible);
		});
	}

	[Fact]
	public void RecentDropdown_OpeningReloadsTheList_AndPickingASessionSelectsTheTreeTab()
	{
		StaTestRunner.Run(() =>
		{
			var actions = new Actions();
			var recent = new RecentViewModel(actions, new ImmediateUiDispatcher(), NullLogger<RecentViewModel>.Instance, TimeSpan.Zero);
			var client = new FakeTessynClient
			{
				RecentListHandler = () => new RecentListResult(DateTimeOffset.UtcNow, [new RecentRowDto("a", "Alpha", "proj", State("a"), false, false, true)]),
			};
			recent.AttachAsync(client).GetAwaiter().GetResult();
			var dropdown = new RecentDropdown { DataContext = recent };
			var tabs = new TabControl { Name = Constants.SessionLists.LeftTabsName, Items = { new TabItem { Header = "Tree" }, new TabItem { Header = "Recent" } }, SelectedIndex = 1 };
			var stack = new StackPanel { Children = { dropdown, tabs } };
			var window = new Window { Content = stack, Width = 400, Height = 400, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			NameScope.SetNameScope(window, new NameScope());
			window.RegisterName(Constants.SessionLists.LeftTabsName, tabs);
			window.Show();
			try
			{
				window.UpdateLayout();
				var before = client.Calls.Count(c => c == "recent.list");
				var menu = (System.Windows.Controls.Primitives.Popup)dropdown.FindName("Menu");

				menu.IsOpen = true;
				window.UpdateLayout();
				Assert.Equal(before + 1, client.Calls.Count(c => c == "recent.list"));   // opening reloads

				var open = Descendants<Button>((DependencyObject)menu.Child).Single(b => AutomationProperties.GetName(b) == "Open Alpha");
				open.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, open));
				Assert.False(menu.IsOpen);
				Assert.Equal(0, tabs.SelectedIndex);
			}
			finally
			{
				window.Close();
			}
		});
	}

	[Fact]
	public void TagsPanel_ShowsChipsAndTheSessionGroupsOfTheSelectedTags()
	{
		WithBindingCheck(() =>
		{
			var tags = new TagSearchViewModel(new Actions(), new ImmediateUiDispatcher(), NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);
			var client = new FakeTessynClient
			{
				TagsSearchHandler = (_, selected) => new TagSearchResult(null,
					[new TagChipDto("a", "ABC-1", "https://x/a", SessionTagKind.LinearIssue, 2, selected.Count > 0)],
					selected.Count > 0 ? null : TagSessionsEmptyReason.SelectTags,
					selected.Count > 0 ? [new SessionGroupDto("d", "Repo", [new SessionRowDto("s1", "Session one", State("s1"))])] : []),
			};
			tags.AttachAsync(client).GetAwaiter().GetResult();
			var panel = new TagsPanel { DataContext = tags };
			Layout(panel);
			Assert.Contains("Tag ABC-1", ButtonNames(panel));
			Assert.Contains(Descendants<TextBlock>(panel), t => t.Text == Constants.SessionLists.TagsSelectTagsText && t.Visibility == Visibility.Visible);

			Descendants<Button>(panel).Single(b => AutomationProperties.GetName(b) == "Tag ABC-1").Command.Execute(tags.Chips[0]);
			Layout(panel);

			Assert.Contains("Open Session one", ButtonNames(panel));
			Assert.Contains(Descendants<TextBlock>(panel), t => t.Text == "Repo");
			Assert.Equal("Tag search", AutomationProperties.GetName(Descendants<TextBox>(panel).Single()));
		});
	}

	[Fact]
	public void RecentDropdown_BindsTheSameList_AndHasAnAccessibleToggle()
	{
		WithBindingCheck(() =>
		{
			var recent = new RecentViewModel(new Actions(), new ImmediateUiDispatcher(), NullLogger<RecentViewModel>.Instance);
			var client = new FakeTessynClient
			{
				RecentListHandler = () => new RecentListResult(DateTimeOffset.UtcNow, [new RecentRowDto("a", "Alpha", "proj", State("a"), false, false, true)]),
			};
			recent.AttachAsync(client).GetAwaiter().GetResult();
			var dropdown = new RecentDropdown { DataContext = recent };

			Layout(dropdown);

			var toggle = Descendants<ToggleButton>(dropdown).Single();
			Assert.Equal(Constants.SessionLists.RecentDropdownName, AutomationProperties.GetName(toggle));
			Assert.False(toggle.IsChecked);
		});
	}

	[Fact]
	public void FavoriteButton_FollowsTheSessionPanesFlag()
	{
		WithBindingCheck(() =>
		{
			var client = new FakeTessynClient();
			client.Sessions["s1"] = FakeSession.WithEntries(2, "One");
			var pane = new SessionPaneViewModel(client, new FakeTranscriptSurface(), new TessynDesktop.WPF.Rendering.MarkdigEntryRenderer(new TessynDesktop.WPF.Services.UrlPolicy()),
				new FakeUrlLauncher(), new ImmediateUiDispatcher(), new TessynDesktop.WPF.Infrastructure.SwitchLatencyTracker(NullLogger<TessynDesktop.WPF.Infrastructure.SwitchLatencyTracker>.Instance),
				NullLogger<SessionPaneViewModel>.Instance);
			var button = new FavoriteButton { DataContext = pane };
			Layout(button);
			var inner = Descendants<Button>(button).Single();
			Assert.Equal(Visibility.Collapsed, inner.Visibility);   // no session open

			pane.OpenAsync("s1", "One").GetAwaiter().GetResult();
			Layout(button);
			Assert.Equal(Visibility.Visible, inner.Visibility);
			Assert.Equal("Favourite", AutomationProperties.GetName(inner));

			inner.Command.Execute(null);
			Layout(button);
			Assert.True(pane.IsFavorite);
		});
	}

	[Fact]
	public void LeftTabs_ATabSwitchKeepsTheTreesScrollPositionAndSelection()
	{
		StaTestRunner.Run(() =>
		{
			var slots = new TessynDesktop.WPF.Services.ShellSlots();
			slots.Add(new TessynDesktop.WPF.Services.ShellSlotContribution { SlotId = Constants.Slots.LeftPanelTabs, Title = "Recent", Create = () => new TextBlock { Text = "recent list" } });
			var tree = new ListBox { ItemsSource = Enumerable.Range(0, 400).Select(i => "row " + i).ToList(), SelectedIndex = 150 };
			var tabs = new SlotTabControl { SlotId = Constants.Slots.LeftPanelTabs, Slots = slots, Items = { new TabItem { Header = "Tree", Content = tree } } };
			var window = new Window { Content = tabs, Width = 300, Height = 300, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			window.Show();
			try
			{
				tabs.Populate();
				window.UpdateLayout();
				var scroll = Descendants<ScrollViewer>(tree).First();
				scroll.ScrollToVerticalOffset(120);
				window.UpdateLayout();
				Assert.Equal(120, scroll.VerticalOffset);

				tabs.SelectedIndex = 1;
				window.UpdateLayout();
				tabs.SelectedIndex = 0;
				window.UpdateLayout();

				var scrollAfter = Descendants<ScrollViewer>(tree).First();
				Assert.Equal(150, tree.SelectedIndex);
				Assert.Equal(120, scrollAfter.VerticalOffset);
			}
			finally
			{
				window.Close();
			}
		});
	}
}
