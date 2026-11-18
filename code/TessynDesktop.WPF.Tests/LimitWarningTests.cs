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
/// FR.1.29 / UC.LE.01 / TC.LE.10 to TC.LE.17: the yellow limits-error warning on a session row. It sits left of the date in the tree, the Recent tab and the Tags tab list, left of the
/// project and profile block in the Recent dropdown, and all the way right on the name line of a worker row in the tree; its tooltip is the error text; it follows the state events.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LimitWarningTests
{
	private const string IconName = "Limits error";
	private const string Text = "You've hit your session limit · resets 5:20pm (Asia/Makassar)";

	private readonly FakeTessynClient _client = new();
	private readonly TreeBuilder _b = new();

	private static SessionStateDto State(string id, string? limit = null) =>
		new(id, false, false, null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, "Work", LimitError: limit);

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

	private static List<TextBlock> Icons(DependencyObject root) =>
		Descendants<TextBlock>(root).Where(t => AutomationProperties.GetName(t) == IconName && t.Visibility == Visibility.Visible).ToList();

	private static Point Origin(UIElement element, UIElement relativeTo) => element.TranslatePoint(new Point(0, 0), relativeTo);

	private static TextBlock Find(DependencyObject root, string name) =>
		Descendants<TextBlock>(root).First(t => AutomationProperties.GetName(t) == name && t.Visibility == Visibility.Visible);

	// ---- view models (TC.LE.10) ----

	[Fact]
	public void ATreeNode_ReadsTheLimitErrorFromItsState_AndTheWorkerPutsItOnTheNameLine()
	{
		var top = new TreeNodeViewModel("s", TreeNodeKind.Session, "S", null) { State = State("s") };
		var worker = new TreeNodeViewModel("w", TreeNodeKind.Worker, "W", top) { State = State("w") };
		Assert.False(top.HasLimitError);
		Assert.False(worker.HasLimitError);

		top.State = State("s", Text);
		worker.State = State("w", Text);

		Assert.Equal(Text, top.LimitError);
		Assert.True(top.ShowLimitOnDateLine);
		Assert.False(top.ShowLimitOnNameLine);
		Assert.True(worker.ShowLimitOnNameLine);
		Assert.False(worker.ShowLimitOnDateLine);

		top.State = State("s");
		Assert.False(top.HasLimitError);
	}

	[Fact]
	public void ADirectoryNode_NeverShowsIt()
	{
		var directory = new TreeNodeViewModel("d", TreeNodeKind.Directory, "D", null) { State = State("d", Text) };

		Assert.False(directory.HasLimitError);
		Assert.False(directory.ShowLimitOnDateLine);
	}

	[Fact]
	public void AListRow_ReadsItAndRaisesTheChange_AndIsNeverOnTheNameLine()
	{
		var row = new SessionRowViewModel("s", "S", "Repo", State("s"));
		var changed = new List<string?>();
		row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		row.State = State("s", Text);

		Assert.Equal(Text, row.LimitError);
		Assert.True(row.ShowLimitOnDateLine);
		Assert.False(row.ShowLimitOnNameLine);
		Assert.Contains(nameof(SessionRowViewModel.HasLimitError), changed);
	}

	[Fact]
	public void ThePalette_HasAYellowWarningColour_ForBothThemes()   // TC.LE.11
	{
		var definition = ColourCatalog.Find(ColourCatalog.LimitWarning)!;

		foreach (var hex in new[] { definition.LightDefault, definition.DarkDefault })
		{
			var c = (Color)ColorConverter.ConvertFromString(hex);
			Assert.True(c.R >= 0xD0 && c.G >= 0x90 && c.B <= 0x60, $"{hex} is a yellow");
		}
	}

	// ---- placement in the four lists ----

	private async Task<TreeViewModel> LoadTreeAsync()
	{
		_client.Directories = [_b.Directory("d1", "Repo", [], _b.SessionWith(State("top", Text), "Top") with { WorkerCount = 2 }, _b.SessionWith(State("calm"), "Calm"))];
		_client.Workers["top"] = [_b.SessionWith(State("w1", Text), "Worker limited") with { SupervisorId = "top" }, _b.SessionWith(State("w2"), "Worker fine") with { SupervisorId = "top" }];
		var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows.First(r => r.Id == "top"));
		return tree;
	}

	[Fact]
	public async Task InTheTree_ATopLevelRowHasItLeftOfTheDate_AndAWorkerRowAllTheWayRightOnTheNameLine()   // TC.LE.12
	{
		var tree = await LoadTreeAsync();

		StaTestRunner.Run(() =>
		{
			var panel = new TreePanel { DataContext = tree };
			var host = new Grid { Width = 340, Height = 400 };
			host.Children.Add(panel);
			Layout(host, 340, 400);

			var faces = Descendants<SessionRowFace>(panel).ToDictionary(f => ((TreeNodeViewModel)f.DataContext).Id);

			var top = Assert.Single(Icons(faces["top"]));
			Assert.Equal(Text, top.ToolTip);
			var date = Find(faces["top"], "Last prompt");
			Assert.True(Origin(top, faces["top"]).X < Origin(date, faces["top"]).X, "left of the date");
			Assert.True(Math.Abs(Origin(top, faces["top"]).Y - Origin(date, faces["top"]).Y) < 14, "on the date line, not on the name line");

			Assert.Empty(Icons(faces["calm"]));
			Assert.Empty(Icons(faces["w2"]));

			// The worker: on the name line (above the date line), at the right end of the row.
			var worker = Assert.Single(Icons(faces["w1"]));
			var workerDate = Find(faces["w1"], "Last prompt");
			Assert.True(Origin(worker, faces["w1"]).Y < Origin(workerDate, faces["w1"]).Y, "above the date");
			Assert.True(Origin(worker, faces["w1"]).X + worker.ActualWidth >= faces["w1"].ActualWidth - 12, "all the way to the right");
			Assert.Equal(Text, worker.ToolTip);
		});
	}

	[Fact]
	public async Task InTheTree_TheWarningFollowsSessionChangedLive_AndClearsWhenALaterTurnSucceeded()   // TC.LE.13
	{
		var tree = await LoadTreeAsync();

		StaTestRunner.Run(() =>
		{
			var panel = new TreePanel { DataContext = tree };
			var host = new Grid { Width = 340, Height = 400 };
			host.Children.Add(panel);
			Layout(host, 340, 400);
			var faces = Descendants<SessionRowFace>(panel).ToDictionary(f => ((TreeNodeViewModel)f.DataContext).Id);
			Assert.Empty(Icons(faces["calm"]));

			_client.RaiseNotification("session.changed", new SessionChangedPayload(State("calm", Text)));
			host.UpdateLayout();
			Assert.Single(Icons(faces["calm"]));

			_client.RaiseNotification("session.changed", new SessionChangedPayload(State("calm")));
			_client.RaiseNotification("session.changed", new SessionChangedPayload(State("w1")));
			host.UpdateLayout();
			Assert.Empty(Icons(faces["calm"]));
			Assert.Empty(Icons(faces["w1"]));
		});
	}

	[Fact]
	public async Task InTheRecentTab_TheWarningIsLeftOfTheDate_LiveAndHiddenWithoutIt()   // TC.LE.14
	{
		_client.Directories = [_b.Directory("d1", "Repo", [], _b.SessionWith(State("a", Text), "A"), _b.SessionWith(State("b"), "B"))];
		var tree = new TreeViewModel(_client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		var tab = new RecentTabViewModel(new NoActions(), new ImmediateUiDispatcher(), NullLogger<RecentTabViewModel>.Instance, TimeSpan.Zero);
		tab.Attach(tree);

		StaTestRunner.Run(() =>
		{
			var panel = new RecentPanel { DataContext = tab };
			Layout(panel);
			var faces = Descendants<SessionRowFace>(panel).ToDictionary(f => ((SessionRowViewModel)f.DataContext).Id);

			var icon = Assert.Single(Icons(faces["a"]));
			Assert.True(Origin(icon, faces["a"]).X < Origin(Find(faces["a"], "Last prompt"), faces["a"]).X);
			Assert.Equal(Text, icon.ToolTip);
			Assert.Empty(Icons(faces["b"]));

			_client.RaiseNotification("session.changed", new SessionChangedPayload(State("a")));
			panel.UpdateLayout();
			Assert.Empty(Icons(faces["a"]));
		});
	}

	[Fact]
	public void InTheTagsTabList_TheWarningIsLeftOfTheDate_LiveAndHiddenWithoutIt()   // TC.LE.15
	{
		var tags = new TagSearchViewModel(new NoActions(), new ImmediateUiDispatcher(), NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);
		var client = new FakeTessynClient
		{
			TagsSearchHandler = (_, _) => new TagSearchResult(null, [], null,
				[new SessionGroupDto("d", "Repo", [new SessionRowDto("a", "A", State("a", Text)), new SessionRowDto("b", "B", State("b"))])]),
		};
		tags.AttachAsync(client).GetAwaiter().GetResult();

		StaTestRunner.Run(() =>
		{
			var panel = new TagsPanel { DataContext = tags };
			Layout(panel);
			var faces = Descendants<SessionRowFace>(panel).ToDictionary(f => ((SessionRowViewModel)f.DataContext).Id);

			var icon = Assert.Single(Icons(faces["a"]));
			Assert.True(Origin(icon, faces["a"]).X < Origin(Find(faces["a"], "Last prompt"), faces["a"]).X);
			Assert.Empty(Icons(faces["b"]));

			client.RaiseNotification("session.changed", new SessionChangedPayload(State("a")));
			panel.UpdateLayout();
			Assert.Empty(Icons(faces["a"]));
		});
	}

	[Fact]
	public void InTheOpenDropdownRow_TheWarningIsLeftOfTheProjectAndProfileBlock()   // TC.LE.16
	{
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown();
			var template = (DataTemplate)dropdown.FindResource("DropdownRowTemplate");
			var limited = new ContentPresenter { Content = new SessionRowViewModel("a", "A", "Repo", State("a", Text)), ContentTemplate = template, Width = 420 };
			var fine = new ContentPresenter { Content = new SessionRowViewModel("b", "B", "Repo", State("b")), ContentTemplate = template, Width = 420 };
			Layout(limited, 420, 60);
			Layout(fine, 420, 60);

			var icon = Assert.Single(Icons(limited));
			var project = Find(limited, "Project");
			Assert.True(Origin(icon, limited).X + icon.ActualWidth <= Origin(project, limited).X + 1, "left of the project and profile block");
			Assert.Equal(Text, icon.ToolTip);
			Assert.Empty(Icons(fine));

			((SessionRowViewModel)limited.Content).State = State("a");
			limited.UpdateLayout();
			Assert.Empty(Icons(limited));
		});
	}

	[Fact]
	public void TheWarning_IsYellowFromThePalette_AndRendersAsAGlyph()   // TC.LE.17
	{
		StaTestRunner.Run(() =>
		{
			var row = new SessionRowViewModel("a", "A", "Repo", State("a", Text));
			var face = new SessionRowFace { DataContext = row, Width = 400 };
			var yellow = new SolidColorBrush(Color.FromRgb(0xE0, 0xA8, 0x00));
			face.Resources["Colour.LimitWarning"] = yellow;
			Layout(face, 400, 80);

			var icon = Assert.Single(Icons(face));
			Assert.Equal(yellow.Color, ((SolidColorBrush)icon.Foreground).Color);
			Assert.Equal("", icon.Text);
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
