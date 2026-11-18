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

/// <summary>Markdown that makes the parser give up (thousands of nested quotes, huge bodies) must never hide an entry, a Focus card or a preview (review V6).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PathologicalMarkdownTests
{
	private static string DeepQuotes => new string('>', 5000) + " the hidden warning";

	[Fact]
	public void TheEntryRenderer_FallsBackToEscapedText_InsteadOfThrowing()
	{
		var renderer = new MarkdigEntryRenderer(new UrlPolicy());

		var html = renderer.RenderMarkdown(DeepQuotes + " <img src=https://e.example/x>");

		Assert.Contains("the hidden warning", html);
		MarkdownNoRemoteResourceTests.AssertSafe(html);
		var view = renderer.Render(new SessionEntryDto { Index = 1, Role = "assistant", Content = DeepQuotes, Timestamp = DateTimeOffset.UnixEpoch });
		Assert.Contains("the hidden warning", view.Html);
	}

	[Fact]
	public void TheFocusCardBuilder_ShowsTheTextAsItIs_AndCapsAHugeBody()
	{
		var texts = new List<string>();
		StaTestRunner.Run(() =>
		{
			var builder = new MarkdownBlockBuilder(new UrlPolicy(), _ => { });
			foreach (var block in builder.Build(DeepQuotes))
				texts.Add(new System.Windows.Documents.TextRange(block.ContentStart, block.ContentEnd).Text);
			var huge = builder.Build(new string('x', Constants.Limits.MaxMarkdownChars * 3));
			texts.Add(string.Concat(huge.OfType<System.Windows.Documents.Paragraph>().SelectMany(t => t.Inlines.OfType<System.Windows.Documents.Run>()).Select(r => r.Text)));
		});

		Assert.Contains("the hidden warning", texts[0]);
		Assert.True(texts[^1].Length <= Constants.Limits.MaxMarkdownChars + 1);
	}

	[Fact]
	public async Task AFocusItemWithAPathologicalBody_StillShowsItsCard()
	{
		var shown = false;
		var client = new FakeTessynClient();
		client.Attention["n"] = [new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Critical, Status = AttentionStatus.Open, Title = "Look", Body = DeepQuotes, CreatedUtc = DateTimeOffset.UnixEpoch }];
		var focus = new FocusViewModel(client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance);
		await focus.SetSessionAsync("n");

		StaTestRunner.Run(() =>
		{
			var panel = new FocusPanel { DataContext = focus };
			panel.Measure(new System.Windows.Size(400, 600));
			panel.Arrange(new System.Windows.Rect(0, 0, 400, 600));
			panel.UpdateLayout();
			shown = Descendants<FocusCardText>(panel).Any(t => t.PlainText.Contains("hidden warning", StringComparison.Ordinal));
		});

		Assert.True(shown, "the card body disappeared");
	}

	private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
	{
		for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;
			foreach (var nested in Descendants<T>(child))
				yield return nested;
		}
	}

	private sealed class ThrowingRenderer : IMarkdownRenderer
	{
		public string RenderMarkdown(string markdown) => throw new InvalidOperationException("boom");
	}

	[Fact]
	public async Task ThePreview_ShowsTheSource_WhenTheMarkdownCannotBeRendered_AndCapsWhatItRenders()
	{
		using var temp = new TempAppPaths();
		var client = new FakeTessynClient();
		var surface = new FakePreviewSurface();
		var policy = new ExternalOpenPolicy();
		var store = new PreviewFileStore(temp.Paths);
		var preview = new FilePreviewViewModel(new FilePreviewLoader(client, new FileKindClassifier(), store, policy), surface, new ThrowingRenderer(),
			new FakeExternalFileLauncher(), policy, store, new FakeUrlLauncher(), new ImmediateUiDispatcher(), NullLogger<FilePreviewViewModel>.Instance);
		client.Files["h"] = ("plan.md", "text/markdown", System.Text.Encoding.UTF8.GetBytes("# Plan\n\nbody"));

		await preview.OpenAsync(new PreviewTarget("h", "plan.md"));

		Assert.Equal("# Plan\n\nbody", surface.LastText);
		Assert.Null(surface.LastHtml);
		Assert.Contains("could not be rendered", preview.Message);
		Assert.False(preview.IsLoading);

		var capped = new FilePreviewViewModel(new FilePreviewLoader(client, new FileKindClassifier(), store, policy), surface, new MarkdigEntryRenderer(new UrlPolicy()),
			new FakeExternalFileLauncher(), policy, store, new FakeUrlLauncher(), new ImmediateUiDispatcher(), NullLogger<FilePreviewViewModel>.Instance);
		client.Files["big"] = ("big.md", "text/markdown", System.Text.Encoding.UTF8.GetBytes(new string('y', Constants.Limits.MaxMarkdownChars + 5000)));
		await capped.OpenAsync(new PreviewTarget("big", "big.md"));
		Assert.Contains("first part", capped.Message);
		Assert.True(surface.LastHtml!.Length < Constants.Limits.MaxMarkdownChars + 100);
	}
}
