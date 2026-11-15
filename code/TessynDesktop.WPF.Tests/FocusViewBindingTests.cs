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
			cardTexts.AddRange(Descendants<TextBlock>(panel).Select(t => t.Text));
			markdownBlocks = Descendants<StackPanel>(panel).Count(p => MarkdownText.GetSource(p) != null);
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
	public void FocusPanel_ShowsTheEmptyState_WhenNothingIsOpen()
	{
		var texts = new List<string>();
		StaTestRunner.Run(() =>
		{
			var client = new FakeTessynClient();
			var focus = new FocusViewModel(client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);
			var panel = new FocusPanel { DataContext = focus };
			Layout(panel);
			texts.AddRange(Descendants<TextBlock>(panel).Where(t => t.Visibility == Visibility.Visible).Select(t => t.Text));
		});

		Assert.Contains(Constants.Texts.NoOpenItems, texts);
	}
}
