using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The session pane's part of the preview windows (FR.29.4, FR.29.7, FR.29.9, FR.29.11): what it offers the router and what it tells it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionPanePreviewWindowsTests : IDisposable
{
	private const string ChipPath = @"C:\work\plan.md";

	private readonly FakeTessynClient _client = new();
	private readonly FakeTranscriptSurface _surface = new();
	private readonly FakePreviewSurface _previewSurface = new();
	private readonly FakeUrlLauncher _urls = new();
	private readonly RecordingPreviewRouter _router = new();
	private readonly TempAppPaths _temp = new();
	private readonly SessionPaneViewModel _pane;

	public SessionPanePreviewWindowsTests()
	{
		IUiDispatcher ui = new ImmediateUiDispatcher();
		var policy = new ExternalOpenPolicy();
		var store = new PreviewFileStore(_temp.Paths);
		var preview = new FilePreviewViewModel(
			new FilePreviewLoader(_client, new FileKindClassifier(), store, policy), _previewSurface, new MarkdigEntryRenderer(new UrlPolicy()), new FakeExternalFileLauncher(),
			policy, store, _urls, ui, NullLogger<FilePreviewViewModel>.Instance);
		_pane = new SessionPaneViewModel(
			_client, _surface, new MarkdigEntryRenderer(new UrlPolicy()), _urls, ui, new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance),
			NullLogger<SessionPaneViewModel>.Instance, Constants.Limits.MaxCachedTranscripts,
			new FocusViewModel(_client, ui, NullLogger<FocusViewModel>.Instance), preview, new MediaPreviewPolicy())
		{
			PreviewRouter = _router,
		};
	}

	public void Dispose()
	{
		_pane.Dispose();
		_temp.Dispose();
	}

	private void AddSession(string id, int count, string? chipPathInEntry3 = null)
	{
		var session = FakeSession.WithEntries(count, "Session " + id);
		session.Entries = [.. session.Entries.Select(e => e with { EntryId = $"f{id}:{e.Index}", Content = e.Index == 3 && chipPathInEntry3 != null ? $"see {chipPathInEntry3}" : $"entry {e.Index}" })];
		_client.Sessions[id] = session;
	}

	private async Task OpenWithFocusItemAsync(string nodeId, params AttentionRefDto[] refs)
	{
		AddSession(nodeId, 10);
		_client.Attention[nodeId] =
		[
			new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "Which?", CreatedUtc = DateTimeOffset.UnixEpoch, Refs = refs },
		];
		await _pane.OpenAsync(nodeId);
		await _pane.Focus.WhenIdleAsync();
	}

	[Fact]
	public void RestoreIsRequestedOnce_WhenThePaneGetsItsRouter()
	{
		Assert.Equal(1, _router.Restores);
	}

	[Fact]
	public async Task SelectingASession_TellsTheRouter_AndOnlyOnAChange()
	{
		AddSession("a", 5);
		AddSession("b", 5);

		await _pane.OpenAsync("a");
		await _pane.OpenAsync("a");
		await _pane.OpenAsync("b");

		Assert.Equal(["a", "b"], _router.Shown);
	}

	[Fact]
	public async Task PathChip_WhenTheRouterTakesIt_OpensInAWindow_NotInThePane()
	{
		AddSession("a", 10, ChipPath);
		_client.Files["h1"] = ("plan.md", "text/markdown", System.Text.Encoding.UTF8.GetBytes("# Chip plan"));
		_client.PreviewHandles[$"a|fa:3|{ChipPath}"] = "h1";
		_router.TakesFiles = true;
		await _pane.OpenAsync("a");

		await _pane.PreviewLocalPathAsync(3, ChipPath);

		var (item, target) = Assert.Single(_router.Files);
		Assert.Equal(PreviewItemKind.File, item.Kind);
		Assert.Equal("plan.md", item.FileName);
		Assert.Equal(("a", "fa:3", ChipPath, "a"), (item.NodeId, item.EntryId, item.Path, item.SourceNodeId));
		Assert.Equal(new PreviewTarget("h1", "plan.md"), target);
		Assert.False(_pane.Preview!.IsOpen);
	}

	[Fact]
	public async Task PathChip_WhenTheRouterRefuses_OpensInThePaneAsBefore()
	{
		AddSession("a", 10, ChipPath);
		_client.Files["h1"] = ("plan.md", "text/markdown", System.Text.Encoding.UTF8.GetBytes("# Chip plan"));
		_client.PreviewHandles[$"a|fa:3|{ChipPath}"] = "h1";
		_router.TakesFiles = false;
		await _pane.OpenAsync("a");

		await _pane.PreviewLocalPathAsync(3, ChipPath);

		Assert.Single(_router.Files);
		Assert.True(_pane.Preview!.IsOpen);
	}

	[Fact]
	public async Task VideoPath_WithNoGroup_StillGoesToTheDefaultPlayer()
	{
		const string video = @"C:\work\clip.mp4";
		AddSession("a", 10, video);
		_client.Files["v1"] = ("clip.mp4", "video/mp4", [1, 2, 3]);
		_client.PreviewHandles[$"a|fa:3|{video}"] = "v1";
		_router.TakesFiles = false;
		await _pane.OpenAsync("a");

		await _pane.PreviewLocalPathAsync(3, video);

		Assert.Single(_router.Files);   // offered first, no group took it
		Assert.False(_pane.Preview!.IsOpen);
	}

	[Fact]
	public async Task FileRef_WhenTheRouterTakes_CarriesTheRefAndTheFocusItem_AndNeverOpensThePane()
	{
		var reference = new AttentionRefDto { Kind = "file", RefId = "ref1", Name = "plan.md", Path = "docs/plan.md" };
		await OpenWithFocusItemAsync("a", reference);
		_router.TakesFiles = true;

		await _pane.ActivateRefAsync(_pane.Focus.Items.Single().Refs.Single().Reference);

		var (item, target) = Assert.Single(_router.Files);
		Assert.Equal(("plan.md", "ref1", "a", "A1"), (item.FileName, item.RefId, item.SourceNodeId, item.SourceFocusItemId));
		Assert.Equal(new PreviewTarget("ref1", "plan.md"), target);
		Assert.False(_pane.Preview!.IsOpen);
	}

	[Fact]
	public async Task FileRef_WhenTheRouterRefuses_OpensThePane()
	{
		_client.Files["ref1"] = ("plan.md", "text/markdown", System.Text.Encoding.UTF8.GetBytes("# The plan"));
		await OpenWithFocusItemAsync("a", new AttentionRefDto { Kind = "file", RefId = "ref1", Name = "plan.md" });
		_router.TakesFiles = false;

		await _pane.ActivateRefAsync(_pane.Focus.Items.Single().Refs.Single().Reference);
		await _pane.Preview!.WhenIdleAsync();

		Assert.True(_pane.Preview.IsOpen);
	}

	[Fact]
	public async Task UrlRef_CarriesTheFocusItem_AndFallsBackToTheLauncherWhenRefused()
	{
		await OpenWithFocusItemAsync("a", new AttentionRefDto { Kind = "url", Url = "https://github.com/org/repo/pull/1" });
		_router.TakesUrls = true;
		var reference = _pane.Focus.Items.Single().Refs.Single().Reference;

		await _pane.ActivateRefAsync(reference);

		var item = Assert.Single(_router.Urls);
		Assert.Equal(("https://github.com/org/repo/pull/1", "a", "A1"), (item.Url, item.SourceNodeId, item.SourceFocusItemId));
		Assert.Empty(_urls.Opened);

		_router.TakesUrls = false;
		await _pane.ActivateRefAsync(reference);

		Assert.Equal(["https://github.com/org/repo/pull/1"], _urls.Opened);
	}

	[Fact]
	public async Task ClosedFocusItem_ReachesTheRouter_OnlyForTheSessionOnShow()
	{
		AddSession("a", 5);
		_client.Attention["a"] =
		[
			new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Open, Title = "Q", CreatedUtc = DateTimeOffset.UnixEpoch },
		];
		await _pane.OpenAsync("a");
		await _pane.Focus.WhenIdleAsync();
		Assert.Empty(_router.ClosedFocus);

		_client.Attention["a"] =
		[
			new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Answered, Title = "Q", CreatedUtc = DateTimeOffset.UnixEpoch, ClosedUtc = DateTimeOffset.UnixEpoch },
		];
		await _pane.Focus.RefreshAsync();

		var (nodeId, ids) = Assert.Single(_router.ClosedFocus);
		Assert.Equal("a", nodeId);
		Assert.Equal(["A1"], ids);
	}

	[Fact]
	public async Task FileRefOfAClosedItem_ReachesTheRouterWithNoFocusItem()
	{
		AddSession("a", 5);
		_client.Attention["a"] =
		[
			new AttentionItemDto
			{
				Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Answered, Title = "Q", CreatedUtc = DateTimeOffset.UnixEpoch,
				ClosedUtc = DateTimeOffset.UnixEpoch, Refs = [new AttentionRefDto { Kind = "file", RefId = "ref1", Name = "plan.md" }],
			},
		];
		await _pane.OpenAsync("a");
		await _pane.Focus.WhenIdleAsync();
		_router.TakesFiles = true;

		await _pane.ActivateRefAsync(_pane.Focus.ClosedItems.Single().Refs.Single().Reference);

		var (item, _) = Assert.Single(_router.Files);
		Assert.Equal(("plan.md", "a", null), (item.FileName, item.SourceNodeId, item.SourceFocusItemId));
	}

	[Fact]
	public async Task ClearingTheFocusView_NeverAnnouncesClosedItems()
	{
		AddSession("a", 5);
		_client.Attention["a"] =
		[
			new AttentionItemDto { Id = "A1", Number = 1, Kind = AttentionKind.Ask, Status = AttentionStatus.Answered, Title = "Q", CreatedUtc = DateTimeOffset.UnixEpoch, ClosedUtc = DateTimeOffset.UnixEpoch },
		];
		await _pane.OpenAsync("a");
		await _pane.Focus.WhenIdleAsync();
		_router.ClosedFocus.Clear();

		_pane.Focus.Clear();

		Assert.Empty(_router.ClosedFocus);
	}
}
