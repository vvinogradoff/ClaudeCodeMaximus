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

	private static SessionStateDto State(string id, bool favorite = false, bool running = false) =>
		new(id, running, false, null, favorite, DateTimeOffset.UtcNow, null, false, null);

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
		Descendants<ButtonBase>(root).Select(b => AutomationProperties.GetName(b)).Where(n => n.Length > 0).ToList();

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

	[Fact]
	public void RecentPanel_ShowsRowsWithOpenFavouriteAndHideButtons_AndPinnedRowsHaveNoHide()
	{
		WithBindingCheck(() =>
		{
			var recent = new RecentViewModel(new Actions(), new ImmediateUiDispatcher(), NullLogger<RecentViewModel>.Instance);
			var client = new FakeTessynClient
			{
				RecentListHandler = () => new RecentListResult(DateTimeOffset.UtcNow,
				[
					new RecentRowDto("a", "Alpha", "proj", State("a", running: true), false, false, true),
					new RecentRowDto("f", "Starred", "proj", State("f", favorite: true), false, true, false),
				]),
			};
			recent.AttachAsync(client).GetAwaiter().GetResult();
			var panel = new RecentPanel { DataContext = recent };

			Layout(panel);

			var names = ButtonNames(panel);
			Assert.Contains("Open Alpha", names);
			Assert.Contains("Favourite Alpha", names);
			Assert.Contains("Hide Alpha", names);
			Assert.Contains("Open Starred", names);
			var hide = Descendants<Button>(panel).Single(b => AutomationProperties.GetName(b) == "Hide Starred");
			Assert.Equal(Visibility.Collapsed, hide.Visibility);
			var hideAlpha = Descendants<Button>(panel).Single(b => AutomationProperties.GetName(b) == "Hide Alpha");
			Assert.Equal(Visibility.Visible, hideAlpha.Visibility);
		});
	}

	[Fact]
	public void RecentPanel_RowButtons_RunTheRowCommands()
	{
		WithBindingCheck(() =>
		{
			var actions = new Actions();
			var recent = new RecentViewModel(actions, new ImmediateUiDispatcher(), NullLogger<RecentViewModel>.Instance);
			var client = new FakeTessynClient
			{
				RecentListHandler = () => new RecentListResult(DateTimeOffset.UtcNow, [new RecentRowDto("a", "Alpha", "proj", State("a"), false, false, true)]),
			};
			recent.AttachAsync(client).GetAwaiter().GetResult();
			var panel = new RecentPanel { DataContext = recent };
			Layout(panel);

			Descendants<Button>(panel).Single(b => AutomationProperties.GetName(b) == "Open Alpha").Command.Execute(recent.Rows[0]);
			Descendants<Button>(panel).Single(b => AutomationProperties.GetName(b) == "Hide Alpha").Command.Execute(recent.Rows[0]);
			recent.RefreshTask.GetAwaiter().GetResult();
			Layout(panel);

			Assert.Equal(["a"], actions.Opened);
			Assert.Empty(recent.Rows);
			Assert.Contains("recent.hide:a", client.Calls);
		});
	}

	[Fact]
	public void RecentPanel_WithNoRows_ShowsTheEmptyText()
	{
		WithBindingCheck(() =>
		{
			var recent = new RecentViewModel(new Actions(), new ImmediateUiDispatcher(), NullLogger<RecentViewModel>.Instance);
			recent.AttachAsync(new FakeTessynClient()).GetAwaiter().GetResult();
			var panel = new RecentPanel { DataContext = recent };

			Layout(panel);

			Assert.Contains(Descendants<TextBlock>(panel), t => t.Text == Constants.SessionLists.RecentEmptyText && t.Visibility == Visibility.Visible);
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
}
