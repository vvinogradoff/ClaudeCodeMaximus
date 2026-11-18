using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.1.28 / UC.AW.01 / TC.AW.01 to TC.AW.08: the active workers badge, a red number in a dark-red outlined square on the rows of the tree, the Recent tab, the Tags tab and the Recent
/// dropdown. It shows the workers running now (<c>SessionStateDto.RunningWorkerCount</c>), not the total ("N workers"), is hidden at 0 and follows the state events live.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ActiveWorkersBadgeTests
{
	private const string BadgeName = "Active workers";

	private readonly FakeTessynClient _client = new();
	private readonly TreeBuilder _b = new();

	private static SessionStateDto State(string id, int? running = null) =>
		new(id, false, false, null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, "Work", RunningWorkerCount: running);

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

	private static void Layout(FrameworkElement element, double width = 420, double height = 600)
	{
		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		element.UpdateLayout();
	}

	private static List<Border> Badges(DependencyObject root) =>
		Descendants<Border>(root).Where(b => AutomationProperties.GetName(b) == BadgeName).ToList();

	private static List<Border> VisibleBadges(DependencyObject root) => Badges(root).Where(b => b.Visibility == Visibility.Visible).ToList();

	private static string NumberOf(Border badge) => ((TextBlock)badge.Child).Text;

	private async Task<TreeViewModel> LoadTreeAsync(params TreeSessionDto[] sessions)
	{
		_client.Directories = [_b.Directory("d1", "Repo", [], sessions)];
		var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);   // the directory's sessions are rows once it is open
		return tree;
	}

	// ---- view models (TC.AW.06) ----

	[Fact]
	public void ATreeNode_ReadsTheRunningWorkersFromItsState_AndIsZeroWithoutThem()
	{
		var node = new TreeNodeViewModel("s", TreeNodeKind.Session, "N", null, workerCount: 104);
		Assert.Equal(0, node.RunningWorkerCount);
		Assert.False(node.HasRunningWorkers);

		node.State = State("s", 4);
		Assert.Equal(4, node.RunningWorkerCount);
		Assert.True(node.HasRunningWorkers);
		Assert.Equal("104 workers", node.WorkerCountText);   // the total stays as it was

		node.State = State("s");
		Assert.Equal(0, node.RunningWorkerCount);
		Assert.False(node.HasRunningWorkers);
	}

	[Fact]
	public void ADirectoryNode_NeverHasTheBadge()
	{
		var directory = new TreeNodeViewModel("d", TreeNodeKind.Directory, "D", null) { State = State("d", 7) };

		Assert.Equal(0, directory.RunningWorkerCount);
		Assert.False(directory.HasRunningWorkers);
	}

	[Fact]
	public void ATreeNode_RaisesTheChangeWhenTheCountMoves()
	{
		var node = new TreeNodeViewModel("s", TreeNodeKind.Session, "N", null) { State = State("s") };
		var changed = new List<string?>();
		node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		node.State = State("s", 2);

		Assert.Contains(nameof(TreeNodeViewModel.RunningWorkerCount), changed);
		Assert.Contains(nameof(TreeNodeViewModel.HasRunningWorkers), changed);
	}

	[Fact]
	public void AListRow_ReadsTheRunningWorkersFromItsState_AndRaisesTheChange()
	{
		var row = new SessionRowViewModel("s", "S", "Repo", State("s"));
		var changed = new List<string?>();
		row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
		Assert.False(row.HasRunningWorkers);

		row.State = State("s", 3);

		Assert.Equal(3, row.RunningWorkerCount);
		Assert.True(row.HasRunningWorkers);
		Assert.Equal("Workers running now: 3", row.RunningWorkersToolTip);
		Assert.Contains(nameof(SessionRowViewModel.HasRunningWorkers), changed);
	}

	[Fact]
	public async Task TheTree_FollowsSessionChangedLive_WithoutARefresh()
	{
		var tree = await LoadTreeAsync(_b.Session("s1", "Sup", workers: 5));
		var node = tree.FindNode("s1")!;
		Assert.Equal(0, node.RunningWorkerCount);

		_client.RaiseNotification("session.changed", new SessionChangedPayload(State("s1", 2)));
		Assert.Equal(2, node.RunningWorkerCount);

		_client.RaiseNotification("session.changed", new SessionChangedPayload(State("s1", 1)));
		Assert.Equal(1, node.RunningWorkerCount);

		_client.RaiseNotification("session.changed", new SessionChangedPayload(State("s1")));
		Assert.False(node.HasRunningWorkers);
		Assert.Equal(5, node.WorkerCount);   // the total did not move
	}

	[Fact]
	public void ThePalette_HasTheBadgeColours_WithTheArchitectsLightDefaults()   // TC.AW.08
	{
		var text = ColourCatalog.Find(ColourCatalog.ActiveWorkersText)!;
		var border = ColourCatalog.Find(ColourCatalog.ActiveWorkersBorder)!;

		Assert.Equal("#E00000", text.LightDefault);
		Assert.Equal("#8B0000", border.LightDefault);
		Assert.NotEqual(text.LightDefault, text.DarkDefault);
		Assert.NotEqual(border.LightDefault, border.DarkDefault);
		Assert.Equal(ColourCatalog.StatusGroup, text.Group);
		Assert.Equal(ColourCatalog.StatusGroup, border.Group);
	}

	// ---- the four lists (TC.AW.01 to TC.AW.05) ----

	[Fact]
	public async Task TheTreeRow_ShowsTheBadgeNextToTheTotal_AndHidesItAtZero()   // TC.AW.01
	{
		var tree = await LoadTreeAsync(_b.SessionWith(State("busy", 3), "Busy") with { WorkerCount = 104 }, _b.SessionWith(State("calm"), "Calm") with { WorkerCount = 12 });

		StaTestRunner.Run(() =>
		{
			var panel = new TreePanel { DataContext = tree };
			var host = new Grid { Width = 340, Height = 300 };
			host.Children.Add(panel);
			Layout(host, 340, 300);

			var faces = Descendants<SessionRowFace>(panel).ToDictionary(f => ((TreeNodeViewModel)f.DataContext).Id);
			var busy = VisibleBadges(faces["busy"]);
			Assert.Equal("3", NumberOf(Assert.Single(busy)));
			Assert.Empty(VisibleBadges(faces["calm"]));
			Assert.Contains(Descendants<TextBlock>(faces["busy"]), t => t.Text == "104 workers");   // the total is still there
			Assert.Contains(Descendants<TextBlock>(faces["calm"]), t => t.Text == "12 workers");

			// The badge sits left of the total: the two read as "[3] 104 workers".
			var badgeX = busy[0].TranslatePoint(new Point(0, 0), faces["busy"]).X;
			var totalX = Descendants<TextBlock>(faces["busy"]).Single(t => t.Text == "104 workers").TranslatePoint(new Point(0, 0), faces["busy"]).X;
			Assert.True(badgeX < totalX, "The badge is left of the 'N workers' total");

			// Live: a state event changes the badge with no refresh.
			_client.RaiseNotification("session.changed", new SessionChangedPayload(State("calm", 9)));
			host.UpdateLayout();
			Assert.Equal("9", NumberOf(Assert.Single(VisibleBadges(faces["calm"]))));
			_client.RaiseNotification("session.changed", new SessionChangedPayload(State("busy")));
			host.UpdateLayout();
			Assert.Empty(VisibleBadges(faces["busy"]));
		});
	}

	[Fact]
	public async Task TheBadge_IsARedNumberInADarkRedOutlinedRoundedSquare_FromThePalette()   // TC.AW.02
	{
		var tree = await LoadTreeAsync(_b.SessionWith(State("busy", 12), "Busy"));

		StaTestRunner.Run(() =>
		{
			var panel = new TreePanel { DataContext = tree };
			var red = new SolidColorBrush(Color.FromRgb(0xE0, 0, 0));
			var dark = new SolidColorBrush(Color.FromRgb(0x8B, 0, 0));
			panel.Resources["Colour.ActiveWorkersText"] = red;
			panel.Resources["Colour.ActiveWorkersBorder"] = dark;
			var host = new Grid { Width = 340, Height = 200 };
			host.Children.Add(panel);
			Layout(host, 340, 200);

			var badge = Assert.Single(VisibleBadges(panel));
			var text = (TextBlock)badge.Child;
			Assert.Equal("12", text.Text);
			Assert.Equal(red.Color, ((SolidColorBrush)text.Foreground).Color);
			Assert.Equal(dark.Color, ((SolidColorBrush)badge.BorderBrush).Color);
			Assert.True(badge.BorderThickness.Left >= 1, "outlined");
			Assert.True(badge.CornerRadius.TopLeft > 0, "rounded corners");
			Assert.InRange(badge.ActualHeight, 12, 22);   // a small square, not a stripe
			Assert.True(badge.ActualWidth >= badge.ActualHeight, "a square at least as wide as high");
		});
	}

	[Fact]
	public async Task TheRecentTabRow_ShowsTheBadge_LiveAndHiddenAtZero()   // TC.AW.03
	{
		var tree = await LoadTreeAsync(_b.SessionWith(State("a", 2), "A"), _b.SessionWith(State("b"), "B"));
		var tab = new RecentTabViewModel(new NoActions(), new ImmediateUiDispatcher(), NullLogger<RecentTabViewModel>.Instance, TimeSpan.Zero);
		tab.Attach(tree);

		StaTestRunner.Run(() =>
		{
			var panel = new RecentPanel { DataContext = tab };
			Layout(panel);

			var faces = Descendants<SessionRowFace>(panel).ToDictionary(f => ((SessionRowViewModel)f.DataContext).Id);
			Assert.Equal("2", NumberOf(Assert.Single(VisibleBadges(faces["a"]))));
			Assert.Empty(VisibleBadges(faces["b"]));

			_client.RaiseNotification("session.changed", new SessionChangedPayload(State("b", 5)));
			panel.UpdateLayout();
			Assert.Equal("5", NumberOf(Assert.Single(VisibleBadges(faces["b"]))));
		});
	}

	[Fact]
	public void TheTagsTabSessionList_ShowsTheBadge_LiveAndHiddenAtZero()   // TC.AW.04
	{
		var tags = new TagSearchViewModel(new NoActions(), new ImmediateUiDispatcher(), NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);
		var client = new FakeTessynClient
		{
			TagsSearchHandler = (_, _) => new TagSearchResult(null, [], null,
				[new SessionGroupDto("d", "Repo", [new SessionRowDto("a", "A", State("a", 4)), new SessionRowDto("b", "B", State("b"))])]),
		};
		tags.AttachAsync(client).GetAwaiter().GetResult();

		StaTestRunner.Run(() =>
		{
			var panel = new TagsPanel { DataContext = tags };
			Layout(panel);

			var faces = Descendants<SessionRowFace>(panel).ToDictionary(f => ((SessionRowViewModel)f.DataContext).Id);
			Assert.Equal("4", NumberOf(Assert.Single(VisibleBadges(faces["a"]))));
			Assert.Empty(VisibleBadges(faces["b"]));

			client.RaiseNotification("session.changed", new SessionChangedPayload(State("a")));
			panel.UpdateLayout();
			Assert.Empty(VisibleBadges(faces["a"]));
		});
	}

	[Fact]
	public void TheOpenDropdownRow_ShowsTheBadge_AndHidesItAtZero()   // TC.AW.05
	{
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown();
			var template = (DataTemplate)dropdown.FindResource("DropdownRowTemplate");
			var busy = new ContentPresenter { Content = new SessionRowViewModel("a", "A", "Repo", State("a", 6)), ContentTemplate = template, Width = 400 };
			var calm = new ContentPresenter { Content = new SessionRowViewModel("b", "B", "Repo", State("b")), ContentTemplate = template, Width = 400 };
			Layout(busy, 400, 60);
			Layout(calm, 400, 60);

			Assert.Equal("6", NumberOf(Assert.Single(VisibleBadges(busy))));
			Assert.Empty(VisibleBadges(calm));

			((SessionRowViewModel)busy.Content).State = State("a");
			busy.UpdateLayout();
			Assert.Empty(VisibleBadges(busy));
		});
	}

	private sealed class NoActions : ISessionListActions
	{
		public void Open(string nodeId, string name)
		{
		}

		public Task<bool?> SetFavoriteAsync(string nodeId, bool value) => Task.FromResult<bool?>(value);
	}
}
