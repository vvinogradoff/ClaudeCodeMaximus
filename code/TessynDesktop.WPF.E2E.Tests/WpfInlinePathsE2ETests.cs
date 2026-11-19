using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for local paths and links inside the transcript text (FR.26.5): the BUILT WPF app against a real loopback daemon (real file access
/// rules) whose newest assistant entry mentions a text file, a markdown file, an image, a missing image, an image in a credentials folder and a
/// web link. The page is driven through the DevTools port. Covers: the paths stay inline as text (no chip row), Ctrl+left-click opens them through
/// the daemon's file.preview, a plain click opens nothing, a drag still selects text and the image, an image is previewed directly under its path
/// (and the page re-measured), a refused image shows "not allowed", and web links follow the same Ctrl+click rule.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfInlinePathsE2ETests : IDisposable
{
	private const int Ctrl = 2;   // DevTools modifier bit

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-inline-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly string _main;
	private readonly string _notes;
	private readonly string _plan;
	private readonly string _picture;

	public WpfInlinePathsE2ETests()
	{
		Directory.CreateDirectory(_root);
		_notes = _env.WriteFile("out/notes.txt", "Some notes.\n");
		_plan = _env.WriteFile("out/plan.md", "# The inline plan\n\nOpened by a Ctrl+click.\n");
		_picture = _env.WriteFile("shots/pic.png", MakePng(240, 120));
		_env.WriteFile("docs/rel-plan.md", "# The relative plan\n\nOpened by a Ctrl+click on a relative path.\n");
		_env.WriteFile("shots/rel.png", MakePng(200, 100));
		var missing = Path.Combine(_env.WorkDir, "shots", "gone.png");
		var secret = Path.Combine(_env.UserProfile, ".ssh", "key.png");
		Directory.CreateDirectory(Path.GetDirectoryName(secret)!);
		File.WriteAllBytes(secret, MakePng(8, 8));

		_env.WriteBodies(
			"what did you produce?",
			$"The notes are in {_notes} and the plan is `{_plan}` as well. The screenshot is {_picture} and it shows the result; " +
			$"a stale one was {missing} and a private one {secret}. Relative: docs/rel-plan.md and shots/rel.png (FR.26.7). Docs: [the docs](https://example.com/docs) end.");
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
	public async Task Paths_stay_inline_open_on_ctrl_click_only_and_do_not_disturb_selection()
	{
		var (process, page) = await StartAsync();
		try
		{
			// No chip row any more: the paths are spans inside the paragraph, with the text around them.
			Assert.Null(await page.EvalAsync("document.querySelector('.chips, button.chip')"));
			Assert.Equal(7, (int)(await page.EvalAsync("document.querySelectorAll('.pth').length"))!);
			Assert.True((bool)(await page.EvalAsync("document.querySelector('.pth').parentElement.textContent.startsWith('The notes are in ')"))!, "the path is not inside the sentence");
			Assert.Contains("dotted", (string)(await page.EvalAsync("getComputedStyle(document.querySelector('.pth')).textDecorationStyle + ' ' + getComputedStyle(document.querySelector('.pth')).textDecorationLine"))!);

			// The pointer cursor appears only while Ctrl is held.
			var notes = await PointOfAsync(page, ".pth", 0);
			await page.MouseMoveAsync(notes.X, notes.Y);
			Assert.NotEqual("pointer", await Style(page, ".pth", "cursor"));
			await page.MouseMoveAsync(notes.X + 1, notes.Y, modifiers: Ctrl);
			Assert.Equal("pointer", await Style(page, ".pth", "cursor"));
			await page.MouseMoveAsync(notes.X + 2, notes.Y);
			Assert.NotEqual("pointer", await Style(page, ".pth", "cursor"));

			// A plain click opens nothing (the host would log "Preview opened").
			await page.ClickAsync(notes.X, notes.Y);
			await Task.Delay(1500);
			Assert.DoesNotContain("Preview opened", _app.ReadLog());

			// A drag over the markdown path selects its text and still opens nothing.
			var plan = await RectOfAsync(page, ".pth", 1);
			await page.DragAsync(plan.Left + 1, plan.Y, plan.Right - 1, plan.Y);
			var selected = (string)(await page.EvalAsync("getSelection().toString()"))!;
			Assert.True(selected.Length > 5 && _plan.Contains(selected.Trim(), StringComparison.Ordinal), "the drag did not select path text: '" + selected + "'");
			await Task.Delay(800);
			Assert.DoesNotContain("Preview opened", _app.ReadLog());

			// Ctrl+left-click opens it through the daemon (file.preview) in the preview pane.
			notes = await PointOfAsync(page, ".pth", 1);
			await page.ClickAsync(notes.X, notes.Y, modifiers: Ctrl);
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "Preview opened: plan.md (\"Markdown\",", "Preview page rendered markdown: true"), "The Ctrl+click did not open the preview. Log:\n" + _app.ReadLog());

			// ... and so does a text file.
			var text = await PointOfAsync(page, ".pth", 0);
			await page.ClickAsync(text.X, text.Y, modifiers: Ctrl);
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "Preview opened: notes.txt"), "The Ctrl+click on the text file did not open the preview. Log:\n" + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			Stop(process, page);
		}
	}

	[Fact]
	public async Task An_image_path_shows_the_image_right_below_it_and_it_opens_and_selects_like_the_path()
	{
		var (process, page) = await StartAsync();
		try
		{
			await WaitAsync("the image to load below its path", async () => (bool)(await page.EvalAsync("(() => { const i = document.querySelector('img.pimg'); return !!i && i.complete && i.naturalWidth === 240; })()"))!);

			// Directly under the path: the slot is the path's next sibling, a block of its own, and starts below the path's last line.
			Assert.True((bool)(await page.EvalAsync("(() => { const p = document.querySelectorAll('.pth')[2], s = p.nextElementSibling; return s.classList.contains('pslot') && s.firstElementChild.tagName === 'IMG'; })()"))!, "the image slot does not follow its path");
			Assert.True((bool)(await page.EvalAsync("(() => { const p = document.querySelectorAll('.pth')[2], r = p.getClientRects(), last = r[r.length - 1], i = document.querySelector('img.pimg').getBoundingClientRect(); return i.top >= last.bottom - 1 && i.top - last.bottom < 40; })()"))!, "the image is not right below its path");
			Assert.True((bool)(await page.EvalAsync("(() => { const i = document.querySelector('img.pimg').getBoundingClientRect(), c = document.querySelector('.e .b').getBoundingClientRect(); return i.right <= c.right + 1 && i.height <= 361; })()"))!, "the image does not fit the column");

			// Loading the image re-measured the entry: the stored height is the real one.
			await WaitAsync("the entry to be measured with its image", async () => (bool)(await page.EvalAsync("(() => { const k = S.b - 1, el = items.lastElementChild; return Math.abs(S.items[k].h - el.offsetHeight) < 1.5 && el.offsetHeight > 200; })()"))!);

			// The image is selectable: a drag from the path down past the image selects it (so it can be copied), and opens nothing.
			var path = await RectOfAsync(page, ".pth", 2);
			var image = await RectOfAsync(page, "img.pimg", 0);
			await page.DragAsync(path.Left + 1, path.Y, image.Left + image.Width / 2, image.Bottom + 6);
			Assert.True((bool)(await page.EvalAsync("getSelection().containsNode(document.querySelector('img.pimg'), true)"))!, "the image was not selected by the drag");
			await Task.Delay(800);
			Assert.DoesNotContain("Preview opened", _app.ReadLog());
			await page.ClickAsync(image.Left + 5, image.Top + 5);   // a plain click on the image does nothing either
			await Task.Delay(800);
			Assert.DoesNotContain("Preview opened", _app.ReadLog());

			// Ctrl+click on the image opens it (the same as on the path).
			image = await RectOfAsync(page, "img.pimg", 0);
			await page.MouseMoveAsync(image.Left + 5, image.Top + 5, modifiers: Ctrl);
			Assert.Equal("pointer", await Style(page, "img.pimg", "cursor"));
			await page.ClickAsync(image.Left + 5, image.Top + 5, modifiers: Ctrl);
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "Preview opened: pic.png"), "The Ctrl+click on the image did not open the preview. Log:\n" + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			Stop(process, page);
		}
	}

	[Fact]
	public async Task A_refused_image_shows_not_allowed_and_a_missing_one_unavailable_instead_of_a_preview()
	{
		var (process, page) = await StartAsync();
		try
		{
			await WaitAsync("both hints", async () => (bool)(await page.EvalAsync("(() => { const t = Array.from(document.querySelectorAll('.pnote')).map(n => n.textContent); return t.includes('Image not allowed') && t.includes('Image unavailable'); })()"))!);

			// Each hint sits in the slot of its own path, and only the allowed image became an <img>.
			await WaitAsync("both allowed images to load", async () => (int)(await page.EvalAsync("document.querySelectorAll('img.pimg').length"))! == 2);
			Assert.Equal(2, (int)(await page.EvalAsync("document.querySelectorAll('img.pimg').length"))!);   // the absolute picture and the relative one
			Assert.Equal("Image unavailable", (string)(await page.EvalAsync("document.querySelectorAll('.pth')[3].nextElementSibling.textContent"))!);
			Assert.Equal("Image not allowed", (string)(await page.EvalAsync("document.querySelectorAll('.pth')[4].nextElementSibling.textContent"))!);
			Assert.DoesNotContain("Blocked a request to a foreign host", _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			Stop(process, page);
		}
	}

	[Fact]
	public async Task A_relative_path_opens_on_ctrl_click_through_the_daemon_and_a_relative_image_shows_inline()
	{
		var (process, page) = await StartAsync();
		try
		{
			// Marked like an absolute path (dotted underline), written exactly as in the text.
			Assert.Equal("docs/rel-plan.md", (string)(await page.EvalAsync("document.querySelectorAll('.pth')[5].textContent"))!);
			Assert.Contains("dotted", (string)(await page.EvalAsync("getComputedStyle(document.querySelectorAll('.pth')[5]).textDecorationStyle"))!);

			// A plain click opens nothing; Ctrl+left-click resolves it against the working directory and opens it.
			var plan = await PointOfAsync(page, ".pth", 5);
			await page.ClickAsync(plan.X, plan.Y);
			await Task.Delay(1000);
			Assert.DoesNotContain("Preview opened", _app.ReadLog());
			await page.ClickAsync(plan.X, plan.Y, modifiers: Ctrl);
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "Preview opened: rel-plan.md (\"Markdown\",", "Preview page rendered markdown: true"), "The Ctrl+click on the relative path did not open the preview. Log:\n" + _app.ReadLog());

			// The relative image shows inline, directly under its path.
			await WaitAsync("the relative image to load", async () => (bool)(await page.EvalAsync("(() => { const i = document.querySelectorAll('img.pimg')[1]; return !!i && i.complete && i.naturalWidth === 200; })()"))!);
			Assert.True((bool)(await page.EvalAsync("(() => { const p = document.querySelectorAll('.pth')[6], s = p.nextElementSibling; return s.classList.contains('pslot') && s.firstElementChild.tagName === 'IMG'; })()"))!, "the relative image slot does not follow its path");
			AssertNoErrors();
		}
		finally
		{
			Stop(process, page);
		}
	}

	[Fact]
	public async Task A_web_link_opens_on_ctrl_click_only()
	{
		var (process, page) = await StartAsync();
		try
		{
			// The page's messages to the host are recorded; a "link" one is swallowed so the test never starts a browser.
			await page.EvalAsync("(() => { window.__posts = []; const original = post; window.post = m => { window.__posts.push(m); if (m.ev === 'link') return; return original(m); }; return 1; })()");
			var link = await PointOfAsync(page, ".b a", 0);

			await page.MouseMoveAsync(link.X, link.Y);
			Assert.NotEqual("pointer", await Style(page, ".b a", "cursor"));
			await page.ClickAsync(link.X, link.Y);
			await Task.Delay(500);
			Assert.Equal(0, (int)(await page.EvalAsync("window.__posts.filter(m => m.ev === 'link').length"))!);

			await page.MouseMoveAsync(link.X + 1, link.Y, modifiers: Ctrl);
			Assert.Equal("pointer", await Style(page, ".b a", "cursor"));
			await page.ClickAsync(link.X, link.Y, modifiers: Ctrl);
			Assert.True(await WaitUntilAsync(async () => (int)(await page.EvalAsync("window.__posts.filter(m => m.ev === 'link' && m.href === 'https://example.com/docs').length"))! == 1), "The Ctrl+click did not post the link to the host");

			AssertNoErrors();
		}
		finally
		{
			Stop(process, page);
		}
	}

	private async Task<(Process Process, CdpPage Page)> StartAsync()
	{
		var port = CdpPage.FreePort();
		var process = _app.Start(CdpPage.EnvironmentFor(port));
		Assert.True(await _app.WaitForLogAsync(process, StartTimeout, $"Session {_main} opened"), "The session did not open. Log:\n" + _app.ReadLog());
		var page = await CdpPage.ConnectAsync(port, "transcript.html", StartTimeout);
		await WaitAsync("the paths to render inline", async () => (int)(await page.EvalAsync("document.querySelectorAll('.pth').length"))! == 7);
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

	private static async Task<(double X, double Y)> PointOfAsync(CdpPage page, string selector, int index)
	{
		var r = await RectOfAsync(page, selector, index);
		return (r.Left + Math.Min(6, r.Width / 2), r.Y);
	}

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

	/// <summary>A solid-colour PNG (RGB, filter 0) built by hand, so the test needs no imaging library.</summary>
	private static byte[] MakePng(int width, int height)
	{
		var raw = new byte[(1 + (width * 3)) * height];
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var at = (y * (1 + (width * 3))) + 1 + (x * 3);
				raw[at] = 200;
				raw[at + 1] = (byte)(x * 255 / Math.Max(1, width - 1));
				raw[at + 2] = 60;
			}
		}

		using var deflated = new MemoryStream();
		using (var zlib = new ZLibStream(deflated, CompressionLevel.Fastest, leaveOpen: true))
			zlib.Write(raw);

		using var png = new MemoryStream();
		png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
		var header = new byte[13];
		WriteInt(header, 0, width);
		WriteInt(header, 4, height);
		header[8] = 8;    // bit depth
		header[9] = 2;    // RGB
		Chunk(png, "IHDR", header);
		Chunk(png, "IDAT", deflated.ToArray());
		Chunk(png, "IEND", []);
		return png.ToArray();
	}

	private static void WriteInt(byte[] target, int at, int value)
	{
		target[at] = (byte)(value >> 24);
		target[at + 1] = (byte)(value >> 16);
		target[at + 2] = (byte)(value >> 8);
		target[at + 3] = (byte)value;
	}

	private static void Chunk(Stream output, string type, byte[] data)
	{
		var length = new byte[4];
		WriteInt(length, 0, data.Length);
		output.Write(length);
		var body = new byte[4 + data.Length];
		Encoding.ASCII.GetBytes(type).CopyTo(body, 0);
		data.CopyTo(body, 4);
		output.Write(body);
		var crc = new byte[4];
		WriteInt(crc, 0, (int)Crc32(body));
		output.Write(crc);
	}

	private static uint Crc32(byte[] bytes)
	{
		var crc = 0xFFFFFFFFu;
		foreach (var b in bytes)
		{
			crc ^= b;
			for (var k = 0; k < 8; k++)
				crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
		}

		return ~crc;
	}
}
