using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// Inline video in the transcript (FR.26.6): the host answers a video path with the cache URL of the first frame (the shell makes it; a fake stands in
/// here), applies the same access policy as for images, and a Ctrl+click opens the file in the default media player instead of the preview pane.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class InlineVideoTests : IDisposable
{
	private const string Video = @"C:\work\clip.mp4";

	private readonly FakeTessynClient _client = new();
	private readonly FakeTranscriptSurface _surface = new();
	private readonly FakePreviewSurface _previewSurface = new();
	private readonly FakeExternalFileLauncher _external = new();
	private readonly FakeVideoThumbnailService _thumbnails = new();
	private readonly TempAppPaths _temp = new();
	private readonly SessionPaneViewModel _pane;
	private readonly FilePreviewViewModel _preview;

	public InlineVideoTests()
	{
		var ui = new ImmediateUiDispatcher();
		var policy = new ExternalOpenPolicy();
		var store = new PreviewFileStore(_temp.Paths);
		_preview = new FilePreviewViewModel(
			new FilePreviewLoader(_client, new FileKindClassifier(), store, policy), _previewSurface, new MarkdigEntryRenderer(new UrlPolicy()), _external,
			policy, store, new FakeUrlLauncher(), ui, NullLogger<FilePreviewViewModel>.Instance, _thumbnails);
		_pane = new SessionPaneViewModel(
			_client, _surface, new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), ui, new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance),
			NullLogger<SessionPaneViewModel>.Instance, Constants.Limits.MaxCachedTranscripts,
			new FocusViewModel(_client, ui, NullLogger<FocusViewModel>.Instance), _preview, new MediaPreviewPolicy());
	}

	public void Dispose()
	{
		_pane.Dispose();
		_temp.Dispose();
	}

	private async Task OpenSessionWithVideo(string path = Video, string name = "clip.mp4", string mime = "video/mp4")
	{
		var session = FakeSession.WithEntries(10, "Session a");
		session.Entries = [.. session.Entries.Select(e => e with { EntryId = $"fa:{e.Index}", Content = $"entry {e.Index}" })];
		session.Entries[3] = session.Entries[3] with { Content = $"Recorded {path} just now" };
		_client.Sessions["a"] = session;
		_client.Files["vid"] = (name, mime, [1, 2, 3, 4, 5, 6, 7, 8]);
		_client.PreviewHandles[$"a|fa:3|{path}"] = "vid";
		await _pane.OpenAsync("a");
	}

	private int CacheFolders() => Directory.Exists(_temp.Paths.PreviewCacheDirectory) ? Directory.GetDirectories(_temp.Paths.PreviewCacheDirectory).Length : 0;

	private int FileReads() => _client.Calls.Count(c => c.StartsWith("file.read", StringComparison.Ordinal));

	[Fact]
	public async Task AVideoPath_IsAnsweredWithTheFrameUrl_AfterTheDaemonCheck_WithoutOpeningThePane()
	{
		await OpenSessionWithVideo();

		await _pane.ShowPathImageAsync(3, Video);

		Assert.Contains($"file.preview:a:fa:3:{Video}", _client.Calls);
		var answer = Assert.Single(_surface.PathImages);
		Assert.Equal((3, Video, false), (answer.Entry, answer.Path, answer.Denied));
		Assert.StartsWith("https://preview.local/", answer.Src);
		Assert.EndsWith("/content.png", answer.Src);       // the frame, not the video
		Assert.False(_pane.Preview!.IsOpen);
		Assert.Empty(_external.Opened);
	}

	[Fact]
	public async Task TheShellReadsTheVideoFromTheCache_AndTheVideoIsNotKeptAfterTheFrameExists()
	{
		await OpenSessionWithVideo();

		await _pane.ShowPathImageAsync(3, Video);

		Assert.Equal([true], _thumbnails.VideoExisted);
		Assert.EndsWith("content.mp4", Assert.Single(_thumbnails.Videos));
		Assert.False(File.Exists(_thumbnails.Videos[0]));
		Assert.Equal(1, CacheFolders());                   // only the frame's folder is left
	}

	[Fact]
	public async Task TheSameVideoAgain_IsServedFromTheCache_WithoutAskingTheShellAgain()
	{
		await OpenSessionWithVideo();

		await _pane.ShowPathImageAsync(3, Video);
		await _pane.ShowPathImageAsync(3, Video);

		Assert.Equal(2, _surface.PathImages.Count);
		Assert.Equal(_surface.PathImages[0].Src, _surface.PathImages[1].Src);
		Assert.Single(_thumbnails.Videos);
	}

	[Fact]
	public async Task WhenNoFrameCanBeMade_TheAnswerIsUnavailable_NotNotAllowed_AndNothingStaysInTheCache()
	{
		_thumbnails.Succeeds = false;
		await OpenSessionWithVideo();

		await _pane.ShowPathImageAsync(3, Video);

		var answer = Assert.Single(_surface.PathImages);
		Assert.Null(answer.Src);
		Assert.False(answer.Denied);
		Assert.Equal(0, CacheFolders());
	}

	[Fact]
	public async Task WhenTheAccessRulesRefuse_TheAnswerIsNotAllowed_AndTheFileIsNeverRead()
	{
		await OpenSessionWithVideo();
		_client.FilePreviewRefusal = "This file cannot be read remotely: it is not inside an allowed folder.";

		await _pane.ShowPathImageAsync(3, Video);

		var answer = Assert.Single(_surface.PathImages);
		Assert.Null(answer.Src);
		Assert.True(answer.Denied);
		Assert.Equal(0, FileReads());
		Assert.Empty(_thumbnails.Videos);
	}

	[Fact]
	public async Task AVideoOverTheInlineLimit_IsNotFetchedJustForItsFrame()
	{
		await OpenSessionWithVideo();

		var src = await _preview.CacheVideoFrameAsync("vid", Constants.Limits.MaxInlineVideoBytes + 1, CancellationToken.None);

		Assert.Null(src);
		Assert.Empty(_thumbnails.Videos);
		Assert.Equal(0, FileReads());
	}

	[Theory]
	[InlineData(@"C:\work\clip.mkv", "clip.mkv", "video/x-matroska")]
	[InlineData(@"C:\work\clip.avi", "clip.avi", "video/x-msvideo")]
	[InlineData(@"C:\work\clip.wmv", "clip.wmv", "video/x-ms-wmv")]
	[InlineData(@"C:\work\clip.mov", "clip.mov", "video/quicktime")]
	public async Task TheOtherVideoTypes_GetAFrameToo(string path, string name, string mime)
	{
		await OpenSessionWithVideo(path, name, mime);

		await _pane.ShowPathImageAsync(3, path);

		Assert.StartsWith("https://preview.local/", Assert.Single(_surface.PathImages).Src);
	}

	[Fact]
	public async Task ACtrlClickOnAVideoPath_OpensItInTheDefaultPlayer_NotInThePane()
	{
		await OpenSessionWithVideo();

		await _pane.PreviewLocalPathAsync(3, Video);

		var opened = Assert.Single(_external.Opened);
		Assert.EndsWith("content.mp4", opened);
		Assert.StartsWith(Path.GetFullPath(_temp.Paths.PreviewCacheDirectory), Path.GetFullPath(opened));   // a file of the preview cache, never the original path
		Assert.False(_pane.Preview!.IsOpen);
		Assert.Null(_pane.Notice);
	}

	[Fact]
	public async Task ACtrlClickTwice_FetchesTheVideoOnce()
	{
		await OpenSessionWithVideo();

		await _pane.PreviewLocalPathAsync(3, Video);
		var reads = FileReads();
		await _pane.PreviewLocalPathAsync(3, Video);

		Assert.Equal(2, _external.Opened.Count);
		Assert.Equal(_external.Opened[0], _external.Opened[1]);
		Assert.Equal(reads, FileReads());
	}

	[Fact]
	public async Task ACtrlClickThatTheDaemonRefuses_ShowsANotice_AndOpensNothing()
	{
		await OpenSessionWithVideo();
		_client.FilePreviewRefusal = "This file cannot be read remotely.";

		await _pane.PreviewLocalPathAsync(3, Video);

		Assert.NotNull(_pane.Notice);
		Assert.Empty(_external.Opened);
	}

	[Fact]
	public async Task ACtrlClickOnANonVideoPath_StillOpensThePane()
	{
		var session = FakeSession.WithEntries(10, "Session a");
		session.Entries = [.. session.Entries.Select(e => e with { EntryId = $"fa:{e.Index}", Content = $"entry {e.Index}" })];
		session.Entries[3] = session.Entries[3] with { Content = @"Notes in C:\work\notes.txt ok" };
		_client.Sessions["a"] = session;
		_client.Files["t"] = ("notes.txt", "text/plain", System.Text.Encoding.UTF8.GetBytes("hello"));
		_client.PreviewHandles[@"a|fa:3|C:\work\notes.txt"] = "t";
		await _pane.OpenAsync("a");

		await _pane.PreviewLocalPathAsync(3, @"C:\work\notes.txt");
		await _preview.WhenIdleAsync();

		Assert.True(_pane.Preview!.IsOpen);
		Assert.Empty(_external.Opened);
	}

	[Theory]
	[InlineData("clip.mkv")]
	[InlineData("clip.avi")]
	[InlineData("clip.wmv")]
	public async Task AVideoTypeThePageCannotPlay_OpenedInThePane_PointsToTheDefaultPlayer(string name)
	{
		_client.Files["h"] = (name, "video/x-msvideo", [1, 2, 3, 4]);

		await _preview.OpenAsync(new PreviewTarget("h", name));

		Assert.Equal(Constants.Texts.VideoOpensExternallyHint, _preview.Message);
		Assert.DoesNotContain("video", _previewSurface.Calls);
		Assert.True(_preview.CanOpenExternally);
	}

	[Fact]
	public void ThePlayableVideoTypesOfThePaneAndOfRemoteLinks_AreUnchanged()
	{
		Assert.DoesNotContain(".mkv", Constants.FileTypes.Video);
		Assert.DoesNotContain(".avi", Constants.FileTypes.Video);
		Assert.DoesNotContain(".wmv", Constants.FileTypes.Video);
		Assert.All(new[] { ".mp4", ".mov", ".webm", ".mkv", ".avi", ".m4v", ".wmv" }, e => Assert.Contains(e, Constants.FileTypes.InlineVideo));
		Assert.All(new[] { ".mkv", ".avi", ".wmv" }, e => Assert.Contains(e, Constants.FileTypes.ExternallyOpenable));
	}
}
