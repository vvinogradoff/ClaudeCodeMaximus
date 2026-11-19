using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The Focus panel, the preview pane and the side column of the session pane, bound to real view models with items, references and a preview.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusViewBindingTests
{
	private sealed class BindingErrorListener : TraceListener
	{
		public List<string> Errors { get; } = [];

		public override void Write(string? message) => Capture(message);

		public override void WriteLine(string? message) => Capture(message);

		private void Capture(string? message)
		{
			if (message != null && message.Contains("BindingExpression", StringComparison.Ordinal))
				lock (Errors)
					Errors.Add(message);
		}
	}

	private static void Layout(FrameworkElement element)
	{
		element.Measure(new Size(1100, 700));
		element.Arrange(new Rect(0, 0, 1100, 700));
		element.UpdateLayout();
	}

	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;
			foreach (var nested in Descendants<T>(child))
				yield return nested;
		}
	}

	/// <summary>The texts shown in a Focus panel: text blocks, plus every run of a card's one document (id, title, outcome, body text) and of the selectable texts.</summary>
	private static IEnumerable<string> Texts(DependencyObject panel, bool visibleOnly = false) =>
		Descendants<TextBlock>(panel).Where(t => !visibleOnly || t.Visibility == Visibility.Visible).Select(t => t.Text)
			.Concat(Descendants<SelectableText>(panel).Where(t => !visibleOnly || t.Visibility == Visibility.Visible).Select(t => t.PlainText))
			.Concat(Descendants<FocusCardText>(panel).Where(t => !visibleOnly || t.Visibility == Visibility.Visible).SelectMany(t => Runs(t.Document)));

	private static IEnumerable<string> Runs(object element)
	{
		if (element is System.Windows.Documents.Run run)
			yield return run.Text;
		else if (element is DependencyObject node)
			foreach (var child in LogicalTreeHelper.GetChildren(node))
				foreach (var text in Runs(child))
					yield return text;
	}

	/// <summary>The cards whose document holds more than the id-and-title paragraph (the open ones with a body, closed ones with their body shown).</summary>
	private static int CardsShowingABody(DependencyObject panel) =>
		Descendants<FocusCardText>(panel).Count(t => t.Visibility == Visibility.Visible && t.ParagraphMetrics.Count > (t.PlainText.Contains("Answered:") || t.PlainText.Contains("Dismissed") ? 2 : 1));

	[Fact]
	public void SessionPane_WithFocusItemsAndAPreview_BindsWithoutBindingErrors_AndShowsTheSideColumn()
	{
		var listener = new BindingErrorListener();
		var cardTexts = new List<string>();
		var sideVisible = false;
		var sideWidth = 0.0;
		var markdownBlocks = 0;
		using var temp = new TempAppPaths();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

			var client = new FakeTessynClient();
			client.Sessions["a"] = FakeSession.WithEntries(3);
			client.Attention["a"] =
			[
				new AttentionItemDto
				{
					Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "Which database?", CreatedUtc = DateTimeOffset.UnixEpoch,
					Body = "Pick **one** of the options, see [the docs](https://example.com).\n\n- first\n- second", Options = ["Postgres", "SQLite"], Recommendation = "Postgres",
					Refs = [new AttentionRefDto { Kind = "entry", NodeId = "a", EntryId = "fa:1", Label = "the benchmark" }, new AttentionRefDto { Kind = "file", RefId = "r", Name = "plan.md" }, new AttentionRefDto { Kind = "url", Url = "https://example.com" }],
				},
				new AttentionItemDto { Id = "A2", Number = 2, Kind = AttentionKind.Critical, Status = AttentionStatus.Open, Title = "Disk is full", CreatedUtc = DateTimeOffset.UnixEpoch.AddMinutes(1) },
			];
			client.Files["r"] = ("plan.md", "text/markdown", System.Text.Encoding.UTF8.GetBytes("# Plan"));
			var ui = new ImmediateUiDispatcher();
			var policy = new ExternalOpenPolicy();
			var store = new PreviewFileStore(temp.Paths);
			var preview = new FilePreviewViewModel(
				new FilePreviewLoader(client, new FileKindClassifier(), store, policy), new FakePreviewSurface(), new MarkdigEntryRenderer(new UrlPolicy()),
				new FakeExternalFileLauncher(), policy, store, new FakeUrlLauncher(), ui, NullLogger<FilePreviewViewModel>.Instance);
			var pane = new SessionPaneViewModel(
				client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), ui,
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance, Constants.Limits.MaxCachedTranscripts,
				new FocusViewModel(client, ui, NullLogger<FocusViewModel>.Instance), preview, new MediaPreviewPolicy());
			pane.OpenAsync("a", "A").GetAwaiter().GetResult();
			pane.Focus.WhenIdleAsync().GetAwaiter().GetResult();

			// The markdown preview renders on a worker thread and continues there (no dispatcher is pumping in this test), so it is opened
			// before the view binds; in the app the continuation returns to the UI thread.
			preview.OpenAsync(new PreviewTarget("r", "plan.md")).GetAwaiter().GetResult();
			var view = new SessionPane { DataContext = pane };
			Layout(view);
			pane.Focus.Items[1].ReplyText = "Postgres";
			pane.Notice = "Something to know";
			Layout(view);

			var side = (Grid)view.FindName("SidePane");
			sideVisible = side.Visibility == Visibility.Visible;
			sideWidth = ((ColumnDefinition)view.FindName("SideColumn")).ActualWidth;
			var panel = Descendants<FocusPanel>(view).Single();
			cardTexts.AddRange(Texts(panel));
			cardTexts.AddRange(Descendants<TextBlock>((DependencyObject)view.FindName("SendAnswersButton")).Select(t => t.Text));   // "Send answers (n)" now lives in the session header (FR.23.14)
			markdownBlocks = CardsShowingABody(panel);
		});

		Assert.Empty(listener.Errors);
		Assert.True(sideVisible);
		Assert.True(sideWidth >= Constants.Limits.MinSidePaneWidth);
		Assert.Contains("Which database?", cardTexts);
		Assert.Contains("Disk is full", cardTexts);
		Assert.Contains("A1", cardTexts);
		Assert.Contains("Ask", cardTexts);
		Assert.Contains("Critical", cardTexts);
		Assert.Contains("the benchmark", cardTexts);
		Assert.Contains("plan.md", cardTexts);
		Assert.Contains("example.com", cardTexts);
		Assert.Contains("Send answers (1)", cardTexts);
		Assert.Equal(1, markdownBlocks);      // only the item with a body has one
	}

	[Fact]
	public void SendAnswersButton_SitsInTheHeader_VisibleOnlyInFocus_AndCtrlEnterInAReplyBoxSendsIt()
	{
		var visibleInFocus = Visibility.Collapsed;
		var visibleInSession = Visibility.Visible;
		var insideFocusPanel = true;
		var plainEnterHandled = true;
		var ctrlEnterHandled = false;
		var calls = new List<string>();
		StaTestRunner.Run(() =>
		{
			var client = new FakeTessynClient();
			client.Sessions["a"] = FakeSession.WithEntries(3);
			client.Attention["a"] =
			[
				new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "Which database?", CreatedUtc = DateTimeOffset.UnixEpoch },
			];
			var ui = new ImmediateUiDispatcher();
			var pane = new SessionPaneViewModel(
				client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), ui,
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance, Constants.Limits.MaxCachedTranscripts,
				new FocusViewModel(client, ui, NullLogger<FocusViewModel>.Instance), null!, new MediaPreviewPolicy());
			pane.OpenAsync("a", "A").GetAwaiter().GetResult();
			pane.Focus.WhenIdleAsync().GetAwaiter().GetResult();
			var view = new SessionPane { DataContext = pane };
			Layout(view);
			var button = (Button)view.FindName("SendAnswersButton");
			var panel = Descendants<FocusPanel>(view).Single();
			insideFocusPanel = Descendants<Button>(panel).Contains(button);

			pane.IsFocusSelected = false;
			Layout(view);
			visibleInSession = button.Visibility;
			pane.IsFocusSelected = true;
			Layout(view);
			visibleInFocus = button.Visibility;

			var reply = Descendants<TextBox>(panel).First();
			plainEnterHandled = panel.HandleReplyKey(reply, System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.None);
			Assert.False(button.Command.CanExecute(null));   // nothing typed: disabled like the button
			ctrlEnterHandled = panel.HandleReplyKey(reply, System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Control);
			reply.Text = "Postgres";
			panel.HandleReplyKey(reply, System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Control);
			calls.AddRange(client.Calls);
		});

		Assert.False(insideFocusPanel);
		Assert.Equal(Visibility.Collapsed, visibleInSession);
		Assert.Equal(Visibility.Visible, visibleInFocus);
		Assert.False(plainEnterHandled);
		Assert.True(ctrlEnterHandled);
		Assert.Contains(calls, c => c.StartsWith("attention.reply:a:A1=Postgres", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Control, true)]
	[InlineData(System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.None, false)]
	[InlineData(System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Shift, false)]
	[InlineData(System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, false)]
	[InlineData(System.Windows.Input.Key.Space, System.Windows.Input.ModifierKeys.Control, false)]
	public void SendShortcut_IsExactlyCtrlEnter(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers, bool expected) =>
		Assert.Equal(expected, FocusSendShortcut.Matches(key, modifiers));

	[Fact]
	public void FocusPanel_ShowsTheEmptyState_WhenNothingIsOpen()
	{
		var texts = new List<string>();
		StaTestRunner.Run(() =>
		{
			var client = new FakeTessynClient();
			var focus = new FocusViewModel(client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);
			var panel = new FocusPanel { DataContext = focus };
			Layout(panel);
			texts.AddRange(Texts(panel, visibleOnly: true));
		});

		Assert.Contains(Constants.Texts.NoOpenItems, texts);
	}

	/// <summary>FR.23.27: no "Focus" title line; the switch in the session header names the view.</summary>
	[Fact]
	public void FocusPanel_HasNoTitleLine()
	{
		var texts = new List<string>();
		StaTestRunner.Run(() =>
		{
			var focus = new FocusViewModel(new FakeTessynClient(), new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);
			var panel = new FocusPanel { DataContext = focus };
			Layout(panel);
			texts.AddRange(Texts(panel, visibleOnly: true));
		});

		Assert.DoesNotContain("Focus", texts);
	}

	private static bool ShowsRefButton(DependencyObject panel, string label) =>
		Descendants<Button>(panel).Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == label && ShownUpTo(b, panel));

	private static bool ShownUpTo(DependencyObject element, DependencyObject root)
	{
		for (var current = element; current != null && !ReferenceEquals(current, root); current = System.Windows.Media.VisualTreeHelper.GetParent(current))
		{
			if (current is UIElement { Visibility: not Visibility.Visible })
				return false;
		}

		return true;
	}

	[Fact]
	public void FocusPanel_ClosedCard_ShowsItsReferencesOnlyWhenExpanded_FR23_29()
	{
		var listener = new BindingErrorListener();
		bool collapsedShows = true, expandedShows = false, openShows = false, collapsedAgain = true;
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

			var t0 = DateTimeOffset.UnixEpoch;
			var client = new FakeTessynClient();
			client.Attention["a"] =
			[
				new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "Open one", Body = "text", CreatedUtc = t0, Refs = [new AttentionRefDto { Kind = "file", RefId = "r1", Name = "open.md" }] },
				new AttentionItemDto { Id = "A2", Number = 2, Kind = AttentionKind.Ask, Status = AttentionStatus.Answered, Title = "Closed one", Body = "text", Answer = "ok", CreatedUtc = t0, ClosedUtc = t0.AddMinutes(5), Refs = [new AttentionRefDto { Kind = "file", RefId = "r2", Name = "closed.md" }] },
			];
			var focus = new FocusViewModel(client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);
			focus.SetSessionAsync("a").GetAwaiter().GetResult();
			var panel = new FocusPanel { DataContext = focus };
			Layout(panel);
			focus.IsClosedExpanded = true;
			Layout(panel);
			openShows = ShowsRefButton(panel, "open.md");
			collapsedShows = ShowsRefButton(panel, "closed.md");

			focus.ClosedItems.Single().ToggleBodyCommand.Execute(null);
			Layout(panel);
			expandedShows = ShowsRefButton(panel, "closed.md");

			focus.ClosedItems.Single().ToggleBodyCommand.Execute(null);
			Layout(panel);
			collapsedAgain = ShowsRefButton(panel, "closed.md");
		});

		Assert.Empty(listener.Errors);
		Assert.True(openShows);
		Assert.False(collapsedShows);
		Assert.True(expandedShows);
		Assert.False(collapsedAgain);
	}

	[Fact]
	public void FocusPanel_ShowsDecisionsAndClosedCollapsed_WithEmptyStateWhenOnlyThoseExist_AndTheClosedCardsAreCompact()
	{
		var listener = new BindingErrorListener();
		string? closedHeader = null, decisionsHeader = null;
		var collapsedTexts = new List<string>();
		var expandedTexts = new List<string>();
		var emptyShown = false;
		var bodyHiddenAtFirst = false;
		var bodyShownAfterClick = false;
		var stayedExpandedAcrossRefresh = false;
		var collapsedOnReselect = false;
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

			var t0 = DateTimeOffset.UnixEpoch;
			var client = new FakeTessynClient();
			client.Attention["a"] =
			[
				new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Decided, Status = AttentionStatus.Open, Title = "Use SQLite", CreatedUtc = t0 },
				new AttentionItemDto { Id = "A2", Number = 2, Kind = AttentionKind.Ask, Status = AttentionStatus.Answered, Title = "Which database?", Body = "Pick **one**", Answer = "Postgres", CreatedUtc = t0, ClosedUtc = t0.AddMinutes(5) },
				new AttentionItemDto { Id = "A3", Number = 3, Kind = AttentionKind.Alert, Status = AttentionStatus.Dismissed, Title = "Disk is full", CreatedUtc = t0, ClosedUtc = t0.AddMinutes(9) },
			];
			var focus = new FocusViewModel(client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);
			focus.SetSessionAsync("a").GetAwaiter().GetResult();
			var panel = new FocusPanel { DataContext = focus };
			Layout(panel);

			var expanders = Descendants<Expander>(panel).ToList();
			decisionsHeader = expanders.Select(e => e.Header as string).FirstOrDefault(h => h?.StartsWith("Decisions", StringComparison.Ordinal) == true);
			closedHeader = expanders.Select(e => e.Header as string).FirstOrDefault(h => h?.StartsWith("Closed", StringComparison.Ordinal) == true);
			emptyShown = Descendants<TextBlock>(panel).Any(t => t.Text == Constants.Texts.NoOpenItems && t.Visibility == Visibility.Visible);
			collapsedTexts.AddRange(Texts(panel));

			var closed = expanders.Single(e => (e.Header as string)?.StartsWith("Closed", StringComparison.Ordinal) == true);
			closed.IsExpanded = true;
			Layout(panel);
			expandedTexts.AddRange(Texts(panel));
			var bodyPanelsBefore = CardsShowingABody(panel);
			bodyHiddenAtFirst = bodyPanelsBefore == 0;

			focus.ClosedItems.Single(i => i.Id == "A2").ToggleBodyCommand.Execute(null);
			Layout(panel);
			bodyShownAfterClick = CardsShowingABody(panel) == 1;

			focus.RefreshAsync().GetAwaiter().GetResult();
			Layout(panel);
			stayedExpandedAcrossRefresh = focus.IsClosedExpanded && closed.IsExpanded;

			focus.OnFocusSelected();
			collapsedOnReselect = !focus.IsClosedExpanded;
		});

		Assert.Empty(listener.Errors);
		Assert.Equal("Decisions (1)", decisionsHeader);
		Assert.Equal("Closed (2)", closedHeader);
		Assert.True(emptyShown);                                 // only a Decided and closed items: nothing else is open
		Assert.DoesNotContain("Use SQLite", collapsedTexts);     // Decisions starts collapsed
		Assert.DoesNotContain("Which database?", collapsedTexts); // so does Closed
		Assert.Contains("Answered: Postgres", expandedTexts);
		Assert.Contains("Dismissed", expandedTexts);
		Assert.True(expandedTexts.IndexOf("Disk is full") < expandedTexts.IndexOf("Which database?"));   // newest closed first
		Assert.True(bodyHiddenAtFirst);
		Assert.True(bodyShownAfterClick);
		Assert.True(stayedExpandedAcrossRefresh);
		Assert.True(collapsedOnReselect);
	}
}
