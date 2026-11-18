using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The tighter margins (the panel scrollbars and the splitter are the delimiters): the input panel (no left padding, 4 px right and bottom), the search bars of the tree and Tags tabs
/// (4 px left, 0 right), the profile-mismatch icon (centred in a space with 4 px either side) and the tree rows (no left or right margin, the indent per level kept). Real layout on an STA thread.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection(nameof(SessionDisplayCollection))]
public sealed class MarginsViewTests
{
	private const double Tolerance = 0.51;

	private readonly FakeTessynClient _client = new();
	private readonly ImmediateUiDispatcher _ui = new();

	private static SessionStateDto State(string id, string pinned) =>
		new(id, false, false, null, false, null, null, false, pinned, PinnedProfileId: "id-" + pinned, PinnedProfileName: pinned);

	private static void Layout(FrameworkElement element, double width, double height)
	{
		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		for (var i = 0; i < 3; i++)
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

	private static Rect BoundsIn(FrameworkElement element, UIElement relativeTo) =>
		new(element.TranslatePoint(new Point(0, 0), relativeTo), new Size(element.ActualWidth, element.ActualHeight));

	private static void Near(double expected, double actual, string what) => Assert.True(Math.Abs(expected - actual) <= Tolerance, $"{what}: expected {expected}, got {actual}");

	// ---- the input panel ----

	[Fact]
	public void TheInputPanel_HasNoLeftPadding_FourPixelsRightAndBottom_AndTheBoxStillFills()
	{
		StaTestRunner.Run(() =>
		{
			var client = new FakeTessynClient();
			client.Sessions["a"] = FakeSession.WithEntries(3);
			var model = new SessionPaneViewModel(client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(),
				new ImmediateUiDispatcher(), new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);
			model.OpenAsync("a", "A").GetAwaiter().GetResult();
			var pane = new SessionPane { DataContext = model };
			Layout(pane, 900, 700);

			var input = (TextBox)pane.FindName("InputBox");
			var side = (Grid)pane.FindName("SideButtons");
			var content = (Grid)pane.FindName("InputContent");
			var inputBounds = BoundsIn(input, pane);
			var sideBounds = BoundsIn(side, pane);

			Near(0, inputBounds.Left, "the input box left edge is the pane left edge");
			Near(900 - 4, sideBounds.Right, "the button column ends 4 px before the right edge");
			Near(700 - 4, BoundsIn(content, pane).Bottom, "the input panel ends 4 px above the bottom edge");
			Near(sideBounds.Left - 8, inputBounds.Right, "the text area fills up to the gap before the buttons");
			Assert.True(inputBounds.Width > 900 - sideBounds.Width - 20, $"the text area is what stretches: {inputBounds.Width}");
		});
	}

	// ---- the search bars ----

	[Fact]
	public void TheTreeSearchBox_HasFourPixelsLeftAndNothingRight()
	{
		StaTestRunner.Run(() =>
		{
			var tree = new TreeViewModel(_client, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
			var panel = new TreePanel { DataContext = tree };
			var host = new Grid { Width = 340, Height = 300 };
			host.Children.Add(panel);
			Layout(host, 340, 300);

			var box = BoundsIn(FindAll<TextBox>(panel).First(), host);
			Near(4, box.Left, "search box left");
			Near(340, box.Right, "search box right");
		});
	}

	[Fact]
	public void TheTagsSearchArea_HasFourPixelsLeft_AndTheClearButtonEndsAtTheRightEdge()
	{
		StaTestRunner.Run(() =>
		{
			using var tags = new TagSearchViewModel(new NoActions(), _ui, NullLogger<TagSearchViewModel>.Instance, TimeSpan.Zero);
			var panel = new TagsPanel { DataContext = tags };
			var host = new Grid { Width = 320, Height = 500 };
			host.Children.Add(panel);
			Layout(host, 320, 500);

			var box = BoundsIn(FindAll<TextBox>(panel).First(), host);
			var clear = BoundsIn(FindAll<Button>(panel).First(b => (string)b.GetValue(AutomationProperties.NameProperty) == "Clear tag filter"), host);
			Near(4, box.Left, "tag search box left");
			Near(320, clear.Right, "the clear button right edge");
		});
	}

	// ---- the tree rows and the mismatch icon ----

	private async Task<TreeViewModel> LoadMismatchTreeAsync()
	{
		_client.Directories = [new TreeDirectoryDto("d1", "Repo", false, [], [new TreeSessionDto("s1", "Super", 1, null, State("s1", "Work"))])];
		_client.Workers["s1"] = [new TreeSessionDto("w1", "Worker", 0, "s1", State("w1", "Old"))];
		var tree = new TreeViewModel(_client, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
		await tree.LoadAsync();
		await tree.ToggleExpandCommand.ExecuteAsync(tree.Rows[0]);
		tree.ToggleExpandCommand.Execute(tree.FindNode("s1"));
		await tree.WhenIdleAsync();
		Assert.True(tree.FindNode("s1")!.HasProfileMismatch, "setup: the supervisor has a worker on another profile");
		return tree;
	}

	[Fact]
	public async Task TheTreeRows_SpanTheFullWidth_WithNoSideMarginsOrPaddings_AndKeepTheIndent()
	{
		var tree = await LoadMismatchTreeAsync();
		StaTestRunner.Run(() =>
		{
			var panel = new TreePanel { DataContext = tree };
			var host = new Grid { Width = 340, Height = 300 };
			host.Children.Add(panel);
			Layout(host, 340, 300);

			var list = FindAll<ListBox>(panel).First(l => l.Name == "RowList");
			var viewport = FindAll<ScrollViewer>(list).First();
			var items = FindAll<ListBoxItem>(list);
			var faces = FindAll<SessionRowFace>(list).Where(f => f.Visibility == Visibility.Visible).ToList();   // a directory row keeps a collapsed face
			Assert.True(items.Count >= 3 && faces.Count >= 2);

			foreach (var item in items)
			{
				Assert.Equal(0, item.Margin.Left);
				Assert.Equal(0, item.Margin.Right);
				Assert.Equal(0, item.Padding.Left);
				Assert.Equal(0, item.Padding.Right);
				var bounds = BoundsIn(item, list);
				Near(BoundsIn(viewport, list).Left, bounds.Left, "row left edge is the list left edge (inside its border)");
				Near(viewport.ViewportWidth, bounds.Width, "row spans the full width of the list");
			}

			foreach (var face in faces)
			{
				var tint = FindAll<Border>(face).First();
				Assert.Equal(0, tint.Margin.Left);
				Assert.Equal(0, tint.Margin.Right);
				var inner = FindAll<Grid>(face).First();
				Assert.Equal(0, inner.Margin.Left);
				Assert.Equal(0, inner.Margin.Right);
				Near(BoundsIn(face, list).Left, BoundsIn(tint, list).Left, "the recency background starts at the face left edge");
				Near(BoundsIn(face, list).Right, BoundsIn(tint, list).Right, "the recency background ends at the face right edge");
			}

			var top = BoundsIn(faces.First(f => ((TreeNodeViewModel)f.DataContext).Id == "s1"), list).Left;
			var worker = BoundsIn(faces.First(f => ((TreeNodeViewModel)f.DataContext).Id == "w1"), list).Left;
			Assert.True(worker - top >= TreeNodeViewModel.IndentPerLevel - Tolerance, $"the indent per level is kept: {worker - top}");
		});
	}

	[Fact]
	public async Task TheProfileMismatchIcon_IsCentredInItsSpace_WithFourPixelsEitherSide()
	{
		var tree = await LoadMismatchTreeAsync();
		StaTestRunner.Run(() =>
		{
			var panel = new TreePanel { DataContext = tree };
			var host = new Grid { Width = 340, Height = 300 };
			host.Children.Add(panel);
			Layout(host, 340, 300);

			var glyph = FindAll<TextBlock>(panel).Single(t => (string)t.GetValue(AutomationProperties.NameProperty) == "Profile badge" && ((Border)t.Parent).Visibility == Visibility.Visible);
			var space = (Border)glyph.Parent;
			Assert.Equal(new Thickness(4, 0, 4, 0), space.Padding);
			var spaceBox = BoundsIn(space, host);
			var glyphBox = BoundsIn(glyph, host);
			Near(spaceBox.Left + 4, glyphBox.Left, "4 px left of the icon");
			Near(spaceBox.Right - 4, glyphBox.Right, "4 px right of the icon");
			Near(spaceBox.Left + spaceBox.Width / 2, glyphBox.Left + glyphBox.Width / 2, "the icon is centred horizontally");
			Near(spaceBox.Top + spaceBox.Height / 2, glyphBox.Top + glyphBox.Height / 2, "the icon is centred vertically");
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
