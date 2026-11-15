using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>The preview pane (FR.23.18, FR.26.5): what each file type shows, the source toggle, cancel, limits and "Open in default app".</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FilePreviewViewModelTests : IDisposable
{
	private readonly FakeTessynClient _client = new();
	private readonly FakePreviewSurface _surface = new();
	private readonly FakeExternalFileLauncher _external = new();
	private readonly FakeUrlLauncher _urls = new();
	private readonly TempAppPaths _temp = new();
	private readonly FilePreviewViewModel _preview;

	public FilePreviewViewModelTests()
	{
		var policy = new ExternalOpenPolicy();
		var store = new PreviewFileStore(_temp.Paths);
		_preview = new FilePreviewViewModel(
			new FilePreviewLoader(_client, new FileKindClassifier(), store, policy), _surface, new MarkdigEntryRenderer(new UrlPolicy()), _external,
			policy, store, _urls, new ImmediateUiDispatcher(), NullLogger<FilePreviewViewModel>.Instance);
	}

	public void Dispose()
	{
		_preview.Dispose();
		_temp.Dispose();
	}

	private Task Open(string handle, string name, string mime, string content) => Open(handle, name, mime, Encoding.UTF8.GetBytes(content));

	private Task Open(string handle, string name, string mime, byte[] bytes)
	{
		_client.Files[handle] = (name, mime, bytes);
		return _preview.OpenAsync(new PreviewTarget(handle, name));
	}

	[Fact]
	public async Task Markdown_IsRenderedSafely_AndTheSourceCanBeShownInstead()
	{
		await Open("h", "plan.md", "text/markdown", "# Title\n\nSome **bold** text with <script>alert(1)</script> and [a](https://example.com) and [b](javascript:alert(1)).\n\n![img](https://evil.example/x.png)");

		Assert.True(_preview.IsOpen);
		Assert.Equal(PreviewKind.Markdown, _preview.Kind);
		Assert.True(_preview.CanToggleSource);
		var html = _surface.LastHtml!;
		Assert.Contains("<h1>Title</h1>", html);
		Assert.Contains("<strong>bold</strong>", html);
		Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);   // raw HTML is escaped
		Assert.Contains("&lt;script&gt;", html);
		Assert.Contains("href=\"https://example.com\"", html);
		Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);       // images are inert text

		_preview.ShowSource = true;
		Assert.Contains("# Title", _surface.LastText!);
		Assert.Equal("text", _surface.Calls[^1]);

		_preview.ShowSource = false;
		Assert.Equal("markdown", _surface.Calls[^1]);
	}

	[Fact]
	public async Task Links_InRenderedMarkdown_OpenInTheSystemBrowserOnlyWhenHttp()
	{
		await Open("h", "a.md", "text/markdown", "[x](https://example.com)");

		_surface.RaiseLinkClicked("https://example.com/");
		_surface.RaiseLinkClicked("file:///C:/Windows/System32/cmd.exe");
		_surface.RaiseLinkClicked("javascript:alert(1)");
		_surface.RaiseLinkClicked("C:\\Windows\\notepad.exe");

		Assert.Equal(["https://example.com/"], _urls.Opened);
	}

	[Theory]
	[InlineData("code.cs", "text/plain")]
	[InlineData("drawing.svg", "image/svg+xml")]
	[InlineData("page.html", "text/html")]
	[InlineData("run.ps1", "text/plain")]
	public async Task TextCodeSvgHtmlAndScripts_AreShownAsTextNeverRendered(string name, string mime)
	{
		var content = "<svg onload=\"alert(1)\"><script>bad()</script></svg>";

		await Open("h", name, mime, content);

		Assert.Equal(PreviewKind.Text, _preview.Kind);
		Assert.Equal(content, _surface.LastText);
		Assert.Equal("text", _surface.Calls[^1]);
		Assert.Null(_surface.LastHtml);
		Assert.False(_preview.CanToggleSource);
	}

	[Fact]
	public async Task Image_IsServedFromThePreviewCacheHost()
	{
		await Open("h", "pic.png", "image/png", new byte[] { 1, 2, 3, 4 });

		Assert.Equal(PreviewKind.Image, _preview.Kind);
		Assert.StartsWith("https://preview.local/", _surface.LastUrl);
		Assert.EndsWith("/content.png", _surface.LastUrl);       // the cache file has a fixed name; the title keeps the real one
		Assert.Equal("pic.png", _preview.Title);
		Assert.Equal("image", _surface.Calls[^1]);
		Assert.True(_preview.CanOpenExternally);
	}

	[Fact]
	public async Task Video_IsShown()
	{
		await Open("h", "clip.mp4", "video/mp4", new byte[] { 1, 2, 3, 4 });

		Assert.Equal("video", _surface.Calls[^1]);
		Assert.StartsWith("https://preview.local/", _surface.LastUrl);
	}

	[Fact]
	public async Task Pdf_OffersOnlyOpenInDefaultApp_AndDownloadsOnlyWhenAsked()
	{
		await Open("h", "doc.pdf", "application/pdf", new byte[1_000_000]);

		Assert.True(_preview.IsPdf);
		Assert.Equal(Constants.Texts.PdfHint, _preview.Message);
		Assert.DoesNotContain("image", _surface.Calls);
		Assert.DoesNotContain("text", _surface.Calls);
		Assert.True(_preview.CanOpenExternally);
		Assert.Empty(_external.Opened);

		await _preview.OpenExternalCommand.ExecuteAsync(null);

		var opened = Assert.Single(_external.Opened);
		Assert.EndsWith("content.pdf", opened);
		Assert.Equal(1_000_000, new FileInfo(opened).Length);
	}

	[Fact]
	public async Task MarkdownAndText_CanBeOpenedExternally_ByDownloadingTheFile()
	{
		await Open("h", "plan.md", "text/markdown", "# hi");

		await _preview.OpenExternalCommand.ExecuteAsync(null);

		var opened = Assert.Single(_external.Opened);
		Assert.Equal("# hi", await File.ReadAllTextAsync(opened));
	}

	[Theory]
	[InlineData("setup.exe", "application/octet-stream")]
	[InlineData("run.ps1", "text/plain")]
	[InlineData("run.bat", "text/plain")]
	[InlineData("run.cmd", "text/plain")]
	[InlineData("shortcut.lnk", "application/octet-stream")]
	[InlineData("code.js", "text/javascript")]
	[InlineData("code.vbs", "text/plain")]
	[InlineData("installer.msi", "application/octet-stream")]
	[InlineData("saver.scr", "application/octet-stream")]
	[InlineData("page.html", "text/html")]
	public async Task ExecutablesAndScripts_CanNeverBeOpenedExternally(string name, string mime)
	{
		await Open("h", name, mime, "MZ payload");

		Assert.False(_preview.CanOpenExternally);
		Assert.False(_preview.OpenExternalCommand.CanExecute(null));
		await _preview.OpenExternalCommand.ExecuteAsync(null);

		Assert.Empty(_external.Opened);
		Assert.False(Directory.Exists(_temp.Paths.PreviewCacheDirectory) && Directory.EnumerateFiles(_temp.Paths.PreviewCacheDirectory, "*", SearchOption.AllDirectories).Any());
	}

	[Fact]
	public async Task ASpoofedName_IsShownWithItsRealExtension_AndNeverOpenedExternally()
	{
		// "annual<RLO>fdp.png" would display as "annualgnp.pdf" (V8).
		await Open("h", "annual‮fdp.png", "image/png", new byte[] { 1, 2 });

		Assert.DoesNotContain('‮', _preview.Title);
		Assert.EndsWith("fdp.png", _preview.Title);
		Assert.False(_preview.CanOpenExternally);
		Assert.Empty(_external.Opened);
	}

	[Fact]
	public async Task FilesOverTheLimit_ShowAMessageNotAPreview()
	{
		await Open("h", "huge.png", "image/png", new byte[(int)Constants.Limits.MaxPreviewImageBytes + 1]);

		Assert.Contains(Constants.Texts.PreviewTooLarge, _preview.Error);
		Assert.DoesNotContain("image", _surface.Calls);
		Assert.False(_preview.CanOpenExternally);
		Assert.False(_preview.IsLoading);
	}

	[Fact]
	public async Task AnUnreadableHandle_ShowsANonBlockingError()
	{
		await _preview.OpenAsync(new PreviewTarget("unknown", "gone.md"));

		Assert.True(_preview.IsOpen);
		Assert.StartsWith("Could not read the file", _preview.Error);
		Assert.False(_preview.IsLoading);
	}

	[Fact]
	public async Task Cancel_StopsALoadInProgress()
	{
		_client.Files["h"] = ("movie.mp4", "video/mp4", new byte[3_000_000]);
		_client.FileReadGate = new TaskCompletionSource();

		var load = _preview.OpenAsync(new PreviewTarget("h", "movie.mp4"));
		Assert.True(_preview.IsLoading);
		Assert.True(_preview.CancelCommand.CanExecute(null));

		_preview.CancelCommand.Execute(null);
		await load;

		Assert.False(_preview.IsLoading);
		Assert.Equal("Cancelled.", _preview.Message);
		Assert.DoesNotContain("video", _surface.Calls);
	}

	[Fact]
	public async Task OpeningAnotherFile_SupersedesTheFirstOne()
	{
		_client.Files["slow"] = ("slow.mp4", "video/mp4", new byte[3_000_000]);
		_client.FileReadGate = new TaskCompletionSource();
		var first = _preview.OpenAsync(new PreviewTarget("slow", "slow.mp4"));

		_client.FileReadGate = null;
		await Open("fast", "fast.txt", "text/plain", "quick");
		await first;

		Assert.Equal("fast.txt", _preview.Title);
		Assert.Equal("quick", _surface.LastText);
		Assert.DoesNotContain("video", _surface.Calls);
	}

	[Fact]
	public async Task Close_EmptiesThePane_AndDropsTheCachedFile()
	{
		await Open("h", "pic.png", "image/png", new byte[] { 1, 2 });
		Assert.True(Directory.EnumerateFiles(_temp.Paths.PreviewCacheDirectory, "*", SearchOption.AllDirectories).Any());

		_preview.CloseCommand.Execute(null);

		Assert.False(_preview.IsOpen);
		Assert.Equal("clear", _surface.Calls[^1]);
		Assert.False(Directory.EnumerateFiles(_temp.Paths.PreviewCacheDirectory, "*", SearchOption.AllDirectories).Any());
	}

	[Fact]
	public async Task APageThatCannotShowTheContent_IsReportedWithoutBlocking()
	{
		await Open("h", "pic.png", "image/png", new byte[] { 1, 2 });

		_surface.RaiseRendered("image", ok: false);

		Assert.Equal("The preview could not be shown.", _preview.Error);
	}

	[Fact]
	public void ShowError_ReplacesWhatTheWindowShows()
	{
		_preview.ShowError("report.md", "The path does not occur in this entry");

		Assert.True(_preview.IsOpen);
		Assert.Equal("report.md", _preview.Title);
		Assert.Equal("The path does not occur in this entry", _preview.Error);
	}
}
