using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for inline video (FR.26.6): the BUILT WPF app against a real loopback daemon (real file access rules) whose newest assistant entry
/// mentions a real 3 KB H.264 clip, a file named .mp4 that is not a video, and a clip in a credentials folder. The page is driven through the
/// DevTools port. Covers: the first frame (made by the Windows Shell from the installed codecs) appears directly under the video path with a play
/// badge; a plain click on the path or the frame asks for nothing; Ctrl+click on either asks the host to open the file in the default media player
/// (a test window starts no program, it logs the request); a file that gives no frame shows "Preview unavailable" and a refused one "Video not allowed".
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfInlineVideoE2ETests : IDisposable
{
	private const int Ctrl = 2;   // DevTools modifier bit

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(40);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-video-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly string _main;

	public WpfInlineVideoE2ETests()
	{
		Directory.CreateDirectory(_root);
		var clip = _env.WriteFile("clips/clip.mp4", File.ReadAllBytes(TinyVideo()));
		var broken = _env.WriteFile("clips/broken.mp4", "this is not a video");
		var secret = Path.Combine(_env.UserProfile, ".ssh", "secret.mp4");
		Directory.CreateDirectory(Path.GetDirectoryName(secret)!);
		File.WriteAllBytes(secret, File.ReadAllBytes(TinyVideo()));

		_env.WriteBodies("what did you record?", $"The recording is {clip} and it shows the run; a damaged one was {broken} and a private one {secret}. Done.");
		_main = _env.Session.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree, new CatalogTestModule([])]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_main}\",\"expandedNodeIds\":[\"{_env.Base.Directory.NodeId}\"]}}");
		_app = new AppProcessHarness(_root, daemonFile);
	}

	private static string TinyVideo([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "TestData", "tiny.mp4");

	public void Dispose()
	{
		_host.Dispose();
		_env.Dispose();
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
			// WebView2 helper processes can hold the data folder for a moment; the folder is in %TEMP%.
		}
	}

	[Fact]
	public async Task The_first_frame_appears_under_the_path_and_only_ctrl_click_opens_the_video_in_the_default_player()
	{
		var (process, page) = await StartAsync();
		try
		{
			await WaitAsync("the first frame to load below its path", async () => (bool)(await page.EvalAsync("(() => { const i = document.querySelector('.pvid img.pimg'); return !!i && i.complete && i.naturalWidth > 20; })()"))!);

			// The frame is a PNG from the preview cache, directly under the path, with the play badge over it, fitted to the column.
			Assert.StartsWith("https://preview.local/", (string)(await page.EvalAsync("document.querySelector('.pvid img.pimg').src"))!);
			Assert.True((bool)(await page.EvalAsync("(() => { const p = document.querySelectorAll('.pth')[0], s = p.nextElementSibling; return s.classList.contains('pslot') && s.firstElementChild.classList.contains('pvid'); })()"))!, "the video slot does not follow its path");
			Assert.True((bool)(await page.EvalAsync("(() => { const p = document.querySelectorAll('.pth')[0], r = p.getClientRects(), last = r[r.length - 1], i = document.querySelector('.pvid img.pimg').getBoundingClientRect(); return i.top >= last.bottom - 1 && i.top - last.bottom < 40; })()"))!, "the frame is not right below its path");
			Assert.True((bool)(await page.EvalAsync("(() => { const i = document.querySelector('.pvid img.pimg').getBoundingClientRect(), b = document.querySelector('.pvid .pplay').getBoundingClientRect(); return b.width > 20 && Math.abs((b.left + b.right) / 2 - (i.left + i.right) / 2) < 2 && Math.abs((b.top + b.bottom) / 2 - (i.top + i.bottom) / 2) < 2; })()"))!, "the play badge is not centred on the frame: " + await page.EvalAsync("JSON.stringify([document.querySelector('.pvid').getBoundingClientRect(), document.querySelector('.pvid img.pimg').getBoundingClientRect(), document.querySelector('.pvid .pplay').getBoundingClientRect()])"));
			Assert.True((bool)(await page.EvalAsync("(() => { const i = document.querySelector('.pvid img.pimg').getBoundingClientRect(), c = document.querySelector('.e .b').getBoundingClientRect(); return i.right <= c.right + 1 && i.height <= 361; })()"))!, "the frame does not fit the column");
			await WaitAsync("the entry to be measured with its frame", async () => (bool)(await page.EvalAsync("(() => { const k = S.b - 1, el = items.lastElementChild; return Math.abs(S.items[k].h - el.offsetHeight) < 1.5; })()"))!);

			// A plain click on the path or the frame asks for nothing; the badge does not catch the click.
			Assert.Equal("none", await Style(page, ".pvid .pplay", "pointerEvents"));
			var path = await RectOfAsync(page, ".pth", 0);
			await page.ClickAsync(path.Left + 5, path.Y);
			var frame = await RectOfAsync(page, ".pvid img.pimg", 0);
			await page.ClickAsync(frame.Left + frame.Width / 2, (frame.Top + frame.Bottom) / 2);
			await Task.Delay(1500);
			Assert.DoesNotContain("Opening an inline video", _app.ReadLog());
			Assert.DoesNotContain("External open requested", _app.ReadLog());
			Assert.DoesNotContain("Preview opened", _app.ReadLog());

			// The pointer cursor on the frame only with Ctrl held.
			frame = await RectOfAsync(page, ".pvid img.pimg", 0);
			await page.MouseMoveAsync(frame.Left + 5, frame.Top + 5);
			Assert.NotEqual("pointer", await Style(page, ".pvid img.pimg", "cursor"));
			await page.MouseMoveAsync(frame.Left + 6, frame.Top + 5, modifiers: Ctrl);
			Assert.Equal("pointer", await Style(page, ".pvid img.pimg", "cursor"));

			// Ctrl+click on the frame asks the host to open the video in the default media player (the test window only logs the request).
			await page.ClickAsync(frame.Left + frame.Width / 2, (frame.Top + frame.Bottom) / 2, modifiers: Ctrl);
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "Opening an inline video in the default player: clip.mp4", "External open requested (test window, nothing started): .mp4"),
				"The Ctrl+click on the frame did not ask to open the video. Log:\n" + _app.ReadLog());
			Assert.DoesNotContain("Preview opened", _app.ReadLog());   // the pane stays closed: a video opens in the player

			// ... and so does Ctrl+click on the path (a second request: the cached copy is opened).
			path = await RectOfAsync(page, ".pth", 0);
			await page.ClickAsync(path.Left + 5, path.Y, modifiers: Ctrl);
			Assert.True(await WaitUntilAsync(async () => await Task.FromResult(CountOf(_app.ReadLog(), "External open requested (test window, nothing started): .mp4") == 2)),
				"The Ctrl+click on the path did not ask to open the video. Log:\n" + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			Stop(process, page);
		}
	}

	[Fact]
	public async Task A_file_without_a_frame_shows_preview_unavailable_and_a_refused_one_video_not_allowed()
	{
		var (process, page) = await StartAsync();
		try
		{
			await WaitAsync("both hints", async () => (bool)(await page.EvalAsync("(() => { const t = Array.from(document.querySelectorAll('.pnote')).map(n => n.textContent); return t.includes('Video not allowed') && t.includes('Preview unavailable'); })()"))!);

			await WaitAsync("the one real frame", async () => (int)(await page.EvalAsync("document.querySelectorAll('.pvid img.pimg').length"))! == 1);
			Assert.Equal("Preview unavailable", (string)(await page.EvalAsync("document.querySelectorAll('.pth')[1].nextElementSibling.textContent"))!);
			Assert.Equal("Video not allowed", (string)(await page.EvalAsync("document.querySelectorAll('.pth')[2].nextElementSibling.textContent"))!);
			Assert.Equal(1, (int)(await page.EvalAsync("document.querySelectorAll('.pvid img.pimg').length"))!);
			Assert.DoesNotContain("Blocked a request to a foreign host", _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			Stop(process, page);
		}
	}

	private static int CountOf(string text, string what)
	{
		var n = 0;
		for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
			n++;

		return n;
	}

	private async Task<(Process Process, CdpPage Page)> StartAsync()
	{
		var port = CdpPage.FreePort();
		var process = _app.Start(CdpPage.EnvironmentFor(port));
		Assert.True(await _app.WaitForLogAsync(process, StartTimeout, $"Session {_main} opened"), "The session did not open. Log:\n" + _app.ReadLog());
		var page = await CdpPage.ConnectAsync(port, "transcript.html", StartTimeout);
		await WaitAsync("the paths to render inline", async () => (int)(await page.EvalAsync("document.querySelectorAll('.pth').length"))! == 3);
		return (process, page);
	}

	private void Stop(Process process, CdpPage page)
	{
		page.Dispose();
		_app.Stop(process);
	}

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
	}

	private static async Task<string> Style(CdpPage page, string selector, string property) =>
		(string)(await page.EvalAsync($"getComputedStyle(document.querySelector({JsonValue.Create(selector)!.ToJsonString()})).{property}"))!;

	private sealed record Rect(double Left, double Right, double Top, double Bottom)
	{
		public double Width => Right - Left;

		public double Y => (Top + Bottom) / 2;
	}

	/// <summary>The first line box of the element, scrolled into view (the position is read again after the scroll was applied).</summary>
	private static async Task<Rect> RectOfAsync(CdpPage page, string selector, int index)
	{
		var script = $"(() => {{ const e = document.querySelectorAll({JsonValue.Create(selector)!.ToJsonString()})[{index}]; e.scrollIntoView({{ block: 'center' }}); const r = e.getClientRects()[0]; return [r.left, r.right, r.top, r.bottom]; }})()";
		await page.EvalAsync(script);
		await Task.Delay(150);
		var a = (JsonArray)(await page.EvalAsync(script))!;
		return new Rect(a[0]!.GetValue<double>(), a[1]!.GetValue<double>(), a[2]!.GetValue<double>(), a[3]!.GetValue<double>());
	}

	private async Task WaitAsync(string what, Func<Task<bool>> condition)
	{
		if (!await WaitUntilAsync(condition))
			Assert.Fail($"Expected: {what}. Log:\n{_app.ReadLog()}");
	}

	private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline)
		{
			if (await condition())
				return true;

			await Task.Delay(100);
		}

		return false;
	}
}
