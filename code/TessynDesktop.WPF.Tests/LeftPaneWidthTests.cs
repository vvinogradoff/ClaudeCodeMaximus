using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The left pane fills the width the splitter gives it (Architect feedback, round 3: "the wider I drag the panel, the bigger the empty margins inside it"). The real left tab control
/// (the LeftTabControl style, as MainWindow uses it) holds the real Tree, Recent and Tags pages and the tree toolbar contributed through its slot, laid out at 300 and at 700 px with the
/// shared scrollbar style. Whatever the panel width, the lists, the rows and the bottom button line span it, less the scrollbar. General rule: in a panel the text area is what stretches.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection(nameof(SessionDisplayCollection))]
public sealed class LeftPaneWidthTests
{
	private const double Tolerance = 1.5;

	/// <summary>The ListBox template's own padding/border (a ListBox without Fluent chrome in a test keeps 1 px each side).</summary>
	private const double ListChrome = 2;
	private static readonly double ScrollBar = Constants.Appearance.ScrollBarWidth;

	private readonly FakeTessynClient _client = new();
	private readonly TreeBuilder _b = new();
	private readonly ImmediateUiDispatcher _ui = new();

	private static SessionStateDto State(string id) =>
		new(id, false, false, null, false, DateTimeOffset.UtcNow.AddMinutes(-3), DateTimeOffset.UtcNow.AddMinutes(-3), false, "Work", Resumable: true);

	private sealed class NoActions : ISessionListActions
	{
		public void Open(string nodeId, string name)
		{
		}

		public Task<bool?> SetFavoriteAsync(string nodeId, bool value) => Task.FromResult<bool?>(value);
	}

	private async Task<TreeViewModel> ManySessionsAsync(int count)
	{
		var sessions = Enumerable.Range(0, count).Select(i => _b.SessionWith(State("s" + i), "Session number " + i)).ToArray();
		_client.Directories = [_b.Directory("d1", "Repo", [], sessions)];
		var tree = new TreeViewModel(_client, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);
		return tree;
	}

	/// <summary>The left tab control as MainWindow builds it: LeftTabControl style, a "Tree" tab and the contributed ones, with the app's shared scrollbar style.</summary>
	private static SlotTabControl LeftTabs(params (string Header, FrameworkElement Page)[] tabs)
	{
		var control = new SlotTabControl { SlotId = "test.leftTabs", Slots = new ShellSlots(), Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
		control.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/TessynDesktop.WPF;component/Styles/LeftTabs.xaml", UriKind.Relative) });
		control.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/TessynDesktop.WPF;component/Styles/ScrollBars.xaml", UriKind.Relative) });
		control.SetResourceReference(FrameworkElement.StyleProperty, "LeftTabControl");
		foreach (var (header, page) in tabs)
			control.Items.Add(new TabItem { Header = header, Content = page });
		return control;
	}

	private static Grid Host(FrameworkElement child, double width)
	{
		var host = new Grid { Width = width, Height = 560 };
		host.Children.Add(child);
		Layout(host, width, 560);
		return host;
	}

	private static void Layout(FrameworkElement element, double width, double height)
	{
		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		element.UpdateLayout();
	}

	private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
	{
		var found = new List<T>();
		void Walk(DependencyObject node)
		{
			for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
			{
				var child = VisualTreeHelper.GetChild(node, i);
				if (child is T match)
					found.Add(match);
				Walk(child);
			}
		}

		Walk(root);
		return found;
	}

	private static void AssertNear(double expected, double actual, string what) => Assert.True(Math.Abs(expected - actual) <= Tolerance, $"{what}: expected {expected}, was {actual}");

	[Theory]
	[InlineData(300)]
	[InlineData(700)]
	public async Task TheTreePage_FillsThePanel_TheRowsSpanItLessTheScrollbar_AndTheToolbarSpansIt(double width)
	{
		var tree = await ManySessionsAsync(60);
		StaTestRunner.Run(() =>
		{
			var slots = new ShellSlots();
			slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.TreeToolbar, Order = 10, Create = () => new TreeToolbar() });
			var page = new TreePanel { DataContext = tree };
			var tabs = LeftTabs(("Tree", page));
			var root = Host(tabs, width);
			foreach (var host in FindAll<SlotHost>(page))   // the slots are filled on Loaded in the app; here by hand, once the page is built
			{
				host.Slots = slots;
				host.Populate();
			}

			Layout(root, width, 560);

			AssertNear(width, page.ActualWidth, "The Tree page spans the panel");
			var list = FindAll<ListBox>(page).Single(l => l.Name == "RowList");
			AssertNear(width, list.ActualWidth, "The tree list spans the panel");
			var bar = FindAll<ScrollBar>(list).Single(s => s.Orientation == Orientation.Vertical);
			Assert.Equal(Visibility.Visible, bar.Visibility);
			AssertNear(ScrollBar, bar.ActualWidth, "The vertical scrollbar is the theme width");

			// A row (no side margins: the scrollbar and the splitter are the delimiters) spans the list less the scrollbar.
			var row = FindAll<ListBoxItem>(list).First();
			Assert.InRange(row.ActualWidth, width - ScrollBar - ListChrome - Tolerance, width - ScrollBar + Tolerance);   // spans the list: only the scrollbar and the list's own border are left

			var toolbar = FindAll<TreeToolbar>(page).Single();
			AssertNear(width, toolbar.ActualWidth, "The bottom button line spans the panel");
			var buttons = FindAll<Button>(toolbar);
			var last = buttons.Last().TranslatePoint(new Point(buttons.Last().ActualWidth, 0), root).X;
			Assert.True(last >= width - 14, $"The last button of the line ends at the right edge: {last} of {width}");
		});
	}

	[Theory]
	[InlineData(300)]
	[InlineData(700)]
	public async Task TheRecentPage_FillsThePanel_AndItsRowsSpanTheListLessTheScrollbar(double width)
	{
		var tree = await ManySessionsAsync(60);
		var recent = new RecentTabViewModel(new NoActions(), _ui, NullLogger<RecentTabViewModel>.Instance, TimeSpan.Zero);
		recent.Attach(tree);

		StaTestRunner.Run(() =>
		{
			var page = new RecentPanel { DataContext = recent };
			var tabs = LeftTabs(("Tree", new Grid()), ("Recent", page));
			tabs.SelectedIndex = 1;
			Host(tabs, width);

			AssertNear(width, page.ActualWidth, "The Recent page spans the panel");
			var list = FindAll<ListBox>(page).Single();
			AssertNear(width - 4, list.ActualWidth, "The Recent list spans the page (2 px margin each side)");
			var tint = FindAll<Border>(page).First(b => b.Style?.TargetType == typeof(Border) && b.MinHeight >= 34 && b.CornerRadius.TopLeft == 3);
			var bar = FindAll<ScrollBar>(list).Single(s => s.Orientation == Orientation.Vertical);
			Assert.Equal(Visibility.Visible, bar.Visibility);
			Assert.True(tint.ActualWidth >= list.ActualWidth - ScrollBar - ListChrome - 6 - Tolerance, $"A Recent row spans the list less the scrollbar: {tint.ActualWidth} of {list.ActualWidth}");
		});
	}

	[Theory]
	[InlineData(300)]
	[InlineData(700)]
	public async Task TheTagsPage_FillsThePanel_TheSearchBarAndTheChipsSpanIt(double width)
	{
		_client.TagsSearchHandler = (_, _) => new TagSearchResult(null,
			Enumerable.Range(1, 40).Select(i => new TagChipDto($"HSE-{i}", $"HSE-{i}", $"https://linear.app/x/issue/HSE-{i}", Models.SessionTagKind.LinearIssue, 1, false)).ToList(),
			TagSessionsEmptyReason.SelectTags, []);
		using var tags = new TagSearchViewModel(new NoActions(), _ui, NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);
		await tags.AttachAsync(_client);

		StaTestRunner.Run(() =>
		{
			var page = new TagsPanel { DataContext = tags };
			var tabs = LeftTabs(("Tree", new Grid()), ("Recent", new Grid()), ("Tags", page));
			tabs.SelectedIndex = 2;
			Host(tabs, width);

			AssertNear(width, page.ActualWidth, "The Tags page spans the panel");
			var search = FindAll<TextBox>(page).First();
			Assert.True(search.ActualWidth > width * 0.5, $"The search box takes the room the buttons leave: {search.ActualWidth} of {width}");
			var scroller = FindAll<ScrollViewer>(page).First(s => s.Content is StackPanel);
			AssertNear(width - 16, scroller.ActualWidth, "The chip area spans the page less its 8 px margins");
			var sessions = FindAll<ListBox>(page).Single();
			AssertNear(width - 4, sessions.ActualWidth, "The tagged sessions list spans the page");
		});
	}

	[Fact]
	public void TheLeftTabs_StretchTheirPages_AndCentreTheirHeaderText()
	{
		StaTestRunner.Run(() =>
		{
			var tabs = LeftTabs(("Tree", new Grid()), ("Recent", new Grid()));
			Host(tabs, 300);

			var items = tabs.Items.OfType<TabItem>().ToList();
			Assert.All(items, i => Assert.Equal(HorizontalAlignment.Stretch, i.HorizontalContentAlignment));   // TabControl copies it onto the page host
			var host = FindAll<ContentPresenter>(tabs).Single(p => p.Name == "PART_SelectedContentHost");
			Assert.Equal(HorizontalAlignment.Stretch, host.HorizontalAlignment);
			var header = FindAll<TextBlock>(tabs).First(t => t.Text == "Tree");
			Assert.Equal(HorizontalAlignment.Center, header.HorizontalAlignment);
		});
	}
}
