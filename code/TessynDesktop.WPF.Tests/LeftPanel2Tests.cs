using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The Architect's second round on the left panel and the Recent dropdown: the two date forms, the worker count inside the row (it is a count, not an age), full-width rows in the
/// Recent tab and the dropdown (project name at the far right of the dropdown row), equal tabs, no empty status line under the tree search, and the Tags scope.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection(nameof(SessionDisplayCollection))]
public sealed class LeftPanel2Tests
{
	private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

	private readonly FakeTessynClient _client = new();
	private readonly TreeBuilder _b = new();
	private readonly ImmediateUiDispatcher _ui = new();

	private static SessionStateDto State(string id, DateTimeOffset? prompt = null, bool running = false) =>
		new(id, running, false, null, false, prompt, prompt, false, "Work", Resumable: true);

	private async Task<TreeViewModel> LoadAsync(params TreeDirectoryDto[] directories)
	{
		_client.Directories = directories.ToList();
		var tree = new TreeViewModel(_client, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		return tree;
	}

	// ---- the date: exactly two forms ----

	[Theory]
	[InlineData(20, "now")]
	[InlineData(5 * 60, "5m")]
	[InlineData(3 * 3600, "3h")]
	[InlineData(9 * 86400, "9d")]
	[InlineData(107 * 7 * 86400, "107w")]
	[InlineData(13 * 86400, "13d")]
	[InlineData(14 * 86400, "2w")]
	[InlineData(400 * 86400, "57w")]
	public void TheRelativeForm_IsAnAge(int seconds, string expected)
	{
		var now = Now;
		Assert.Equal(expected, SessionDisplay.DateText(now.AddSeconds(-seconds), now, SessionDateFormat.Relative));
	}

	[Fact]
	public void TheAbsoluteForm_IsTheDateAlone()
	{
		var at = new DateTimeOffset(2026, 11, 17, 8, 53, 30, TimeSpan.Zero);

		Assert.Equal(at.ToLocalTime().ToString("yyyy-MM-dd"), SessionDisplay.DateText(at, Now, SessionDateFormat.Absolute));
	}

	[Fact]
	public void TheAppearanceTab_OffersExactlyTheDateAndTheRelativeForm()
	{
		var settings = SettingsTestSupport.NewSettings(new FakeTessynClient(), SettingsTestSupport.NewStore());

		var labels = settings.Appearance.DateFormats.Select(o => o.Label).ToList();

		Assert.Equal(["Date (yyyy-MM-dd)", "Relative (for example 107w)"], labels);
		Assert.Equal([SessionDateFormat.Absolute, SessionDateFormat.Relative], settings.Appearance.DateFormats.Select(o => o.Value).ToList());
	}

	[Fact]
	public void TheRowShowsTheDateOrTheAge_NeverBoth()
	{
		var prompt = DateTimeOffset.UtcNow.AddDays(-9);
		var node = new TreeNodeViewModel("s", TreeNodeKind.Session, "N", null, workerCount: 104) { State = State("s", prompt) };
		var original = SessionDisplay.DateFormat;
		try
		{
			SessionDisplay.DateFormat = SessionDateFormat.Absolute;
			Assert.Equal(prompt.ToLocalTime().ToString("yyyy-MM-dd"), node.LastPromptText);
			SessionDisplay.DateFormat = SessionDateFormat.Relative;
			Assert.Equal("9d", node.LastPromptText);
		}
		finally
		{
			SessionDisplay.DateFormat = original;
		}
	}

	// ---- "104w" is a worker count ----

	[Fact]
	public void TheWorkerCount_IsWrittenOut_AndNeverLooksLikeAnAge()
	{
		Assert.Equal("104 workers", new TreeNodeViewModel("s", TreeNodeKind.Session, "N", null, workerCount: 104).WorkerCountText);
		Assert.Equal("1 worker", new TreeNodeViewModel("s", TreeNodeKind.Session, "N", null, workerCount: 1).WorkerCountText);
		Assert.Equal(string.Empty, new TreeNodeViewModel("s", TreeNodeKind.Session, "N", null).WorkerCountText);
		Assert.Equal(string.Empty, new TreeNodeViewModel("d", TreeNodeKind.Directory, "D", null, workerCount: 3).WorkerCountText);
		Assert.False(new SessionRowViewModel("s", "N", "d", State("s")).HasWorkers);
	}

	[Fact]
	public async Task TheWorkerCount_SitsInsideTheRecencyBox_AndTheDateIsTheOnlyTextOnTheDateLine()
	{
		var tree = await LoadAsync(_b.Directory("d1", "Repo", [], _b.SessionWith(State("s1", DateTimeOffset.UtcNow.AddMinutes(-3)), "Load session output") with { WorkerCount = 104 }));
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);

		StaTestRunner.Run(() =>
		{
			var panel = new TreePanel { DataContext = tree };
			var host = new Grid { Width = 340, Height = 300 };
			host.Children.Add(panel);
			Layout(host, 340, 300);

			var face = FindAll<SessionRowFace>(panel).Single(f => ((TreeNodeViewModel)f.DataContext).Id == "s1");
			var tint = (Border)face.Content;
			var count = FindAll<TextBlock>(face).Single(t => t.Text == "104 workers");
			Assert.Equal(Visibility.Visible, count.Visibility);
			Assert.True(IsInside(count, tint, host), "The worker count is inside the coloured box");
			Assert.DoesNotContain(FindAll<TextBlock>(panel), t => t.Text == "104w");   // the old count outside the box

			var date = FindAll<TextBlock>(face).Single(t => Equals(System.Windows.Automation.AutomationProperties.GetName(t), "Last prompt"));
			Assert.Equal(DateTimeOffset.UtcNow.AddMinutes(-3).ToLocalTime().ToString("yyyy-MM-dd"), date.Text);
		});
	}

	// ---- the Recent tab and the dropdown: full-width rows ----

	[Fact]
	public async Task TheRecentTabRows_SpanTheWholeWidth_AndShowNoProjectName()
	{
		var tree = await LoadAsync(_b.Directory("d1", "Repo", [], _b.SessionWith(State("s1", DateTimeOffset.UtcNow.AddMinutes(-3)), "Alpha")));
		var tab = new RecentTabViewModel(new NoActions(), _ui, NullLogger<RecentTabViewModel>.Instance, TimeSpan.Zero);
		tab.Attach(tree);

		StaTestRunner.Run(() =>
		{
			var panel = new RecentPanel { DataContext = tab };
			Layout(panel, 320, 300);

			var tint = FindAll<Border>(panel).First(b => b.Style?.TargetType == typeof(Border) && b.MinHeight >= 34 && b.CornerRadius.TopLeft == 3);
			var width = tint.ActualWidth;
			var list = FindAll<ListBox>(panel).Single();
			Assert.True(width >= list.ActualWidth - 12, $"The row's colour spans the list: {width} of {list.ActualWidth}");
			Assert.DoesNotContain(FindAll<TextBlock>(panel), t => t.Text == "Repo" && t.FontSize == 11);   // the group header names the project, the row does not repeat it
		});
	}

	[Fact]
	public void TheDropdownRow_SpansTheWidth_WithTheProjectNameAfterTheSessionNameAtTheFarRight()
	{
		var row = new SessionRowViewModel("s1", "A session name", "HardStop", State("s1", DateTimeOffset.UtcNow.AddMinutes(-3)), canHide: true);
		var recent = new RecentViewModel(new NoActions(), _ui, NullLogger<RecentViewModel>.Instance);
		recent.Rows.Add(row);

		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = recent };
			var menu = (System.Windows.Controls.Primitives.Popup)dropdown.FindName("Menu");
			var window = new Window { Content = dropdown, Width = 460, Height = 300, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			window.Show();
			try
			{
				menu.IsOpen = true;
				var list = (FrameworkElement)menu.Child;
				list.UpdateLayout();

				var name = FindAll<TextBlock>(list).Single(t => t.Text == "A session name");
				var project = FindAll<TextBlock>(list).Single(t => t.Text == "HardStop");
				var origin = new Point(0, 0);
				var nameAt = name.TranslatePoint(origin, list);
				var projectAt = project.TranslatePoint(origin, list);
				Assert.True(projectAt.X > nameAt.X + name.ActualWidth - 1, "The project name comes after the session name");
				Assert.True(projectAt.X + project.ActualWidth > list.ActualWidth - 70, "...at the far right (the icon slot keeps the last 30 px)");
				var tint = FindAll<Border>(list).First(b => b.MinHeight >= 30 && b.CornerRadius.TopLeft == 3);
				Assert.True(tint.ActualWidth >= list.ActualWidth - 16, $"The row's colour spans the dropdown: {tint.ActualWidth} of {list.ActualWidth}");
			}
			finally
			{
				menu.IsOpen = false;
				window.Close();
			}
		});
	}

	[Fact]
	public void TheDropdownButton_ShowsItsLabel_AndHasNoAccentFillWhileOpen()
	{
		StaTestRunner.Run(() =>
		{
			var recent = new RecentViewModel(new NoActions(), _ui, NullLogger<RecentViewModel>.Instance);
			var dropdown = new RecentDropdown { DataContext = recent, Width = 440 };
			Layout(dropdown, 440, 40);
			var toggle = (System.Windows.Controls.Primitives.ToggleButton)dropdown.FindName("Toggle");
			toggle.IsChecked = true;
			Layout(dropdown, 440, 40);

			var label = (TextBlock)dropdown.FindName("Label");
			Assert.True(label.ActualWidth > 0, "The label is shown");
			var chrome = FindAll<Border>(toggle).First(b => b.Name == "Chrome");
			Assert.NotEqual(Color.FromRgb(0x00, 0x5A, 0x9E), (chrome.Background as SolidColorBrush)?.Color);   // the Fluent checked toggle's dark blue
		});
	}

	// ---- the tabs ----

	// ---- the tree search bar ----

	[Fact]
	public async Task UnderTheTreeSearchBar_ThereIsNoEmptyStatusLine()
	{
		var tree = await LoadAsync(_b.Directory("d1", "Repo", [], _b.Session("s1", "One")));
		StaTestRunner.Run(() =>
		{
			var panel = new TreePanel { DataContext = tree };
			var host = new Grid { Width = 340, Height = 300 };
			host.Children.Add(panel);
			Layout(host, 340, 300);

			var search = FindAll<TextBox>(panel).First();
			var list = FindAll<ListBox>(panel).First(l => l.Name == "RowList");
			var gap = list.TranslatePoint(new Point(0, 0), host).Y - (search.TranslatePoint(new Point(0, 0), host).Y + search.ActualHeight);
			Assert.True(gap < 14, $"Gap between the search box and the rows: {gap}");
		});
	}

	// ---- the Tags tab ----

	[Fact]
	public async Task Tags_ASelectedSessionScopes_ADirectoryOrNothingDoesNot_AndShowAllDropsTheScope()
	{
		var tree = await LoadAsync(_b.Directory("d1", "Repo", [], _b.Session("s1", "One")));
		using var tags = new TagSearchViewModel(new NoActions(), _ui, NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);
		tags.AttachTree(tree);
		await tags.AttachAsync(_client);

		tree.SelectNodeById("d1");
		await tags.SearchTask;
		Assert.False(tags.IsScoped);
		Assert.Null(_client.TagsSearchScopes.Last());

		tree.SelectNodeById("s1");
		await tags.SearchTask;
		Assert.True(tags.IsScoped);
		Assert.Equal("s1", _client.TagsSearchScopes.Last());

		tags.ShowAllDirectoriesCommand.Execute(null);
		await tags.SearchTask;
		Assert.False(tags.IsScoped);
		Assert.Null(_client.TagsSearchScopes.Last());
	}

	[Fact]
	public async Task TheTagsTab_ListsTheTagsTheDaemonSent_AndGivesTheChipsTheRoomWhileNoTagIsSelected()
	{
		_client.TagsSearchHandler = (_, _) => new TagSearchResult(null,
			Enumerable.Range(1, 40).Select(i => new TagChipDto($"HSE-{i}", $"HSE-{i}", $"https://linear.app/x/issue/HSE-{i}", Models.SessionTagKind.LinearIssue, 1, false)).ToList(),
			TagSessionsEmptyReason.SelectTags, []);
		using var tags = new TagSearchViewModel(new NoActions(), _ui, NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);
		await tags.AttachAsync(_client);

		StaTestRunner.Run(() =>
		{
			var panel = new TagsPanel { DataContext = tags };
			var host = new Grid { Width = 320, Height = 600 };
			host.Children.Add(panel);
			Layout(host, 320, 600);

			Assert.Equal(40, tags.Chips.Count);
			var scroller = FindAll<ScrollViewer>(panel).First(s => s.Content is StackPanel);
			Assert.True(scroller.ActualHeight > 600 * 0.6, $"The chips have most of the panel while no tag is selected: {scroller.ActualHeight} of 600");
		});
	}

	private static bool IsInside(FrameworkElement inner, FrameworkElement outer, Visual relativeTo)
	{
		var innerBox = new Rect(inner.TranslatePoint(new Point(0, 0), outer), new Size(inner.ActualWidth, inner.ActualHeight));
		return new Rect(0, 0, outer.ActualWidth, outer.ActualHeight).Contains(innerBox);
	}

	private sealed class NoActions : ISessionListActions
	{
		public void Open(string nodeId, string name)
		{
		}

		public Task<bool?> SetFavoriteAsync(string nodeId, bool value) => Task.FromResult<bool?>(value);
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
}