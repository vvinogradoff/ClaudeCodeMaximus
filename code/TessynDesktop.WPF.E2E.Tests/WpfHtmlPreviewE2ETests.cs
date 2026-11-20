using System.Diagnostics;
using System.Globalization;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for html files previewed rendered (FR.26.8, UC.LK.08, TC.LK.36–53): the BUILT WPF app against a real loopback daemon. A session entry names an html file with a heading, an inline
/// style, a data: image, a remote image, a script that would rewrite the heading, a meta refresh, an http link and a #anchor; Ctrl+click opens it in the default Preview panel (one monitor) or in a
/// group window (two monitors). The preview page is driven over DevTools: the html frame is inspected through its same-origin document.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfHtmlPreviewE2ETests
{
	private const string SourceInPanel = "Show source of panel Preview";
	private const string SourceInWindow = "Show source";
	private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

	private const string Frame = "document.querySelector('iframe.hframe')";

	private static string PageHtml(int port) =>
		"<!DOCTYPE html><html><head><meta http-equiv=\"refresh\" content=\"1;url=http://127.0.0.1:" + port + "/b\"><style>h1{color:rgb(200,0,0)}</style></head><body>" +
		"<h1 id=\"top\">Heading</h1><img id=\"data\" src=\"data:image/png;base64," + OnePixelPng + "\"><img id=\"remote\" src=\"http://127.0.0.1:" + port + "/remote.png\">" +
		"<script>document.querySelector('h1').textContent='HACKED';</script>" +
		"<a id=\"web\" href=\"http://127.0.0.1:" + port + "/a\">the web</a> <a id=\"anc\" href=\"#bottom\">down</a> <a id=\"odd\" href=\"file:///c:/windows/win.ini\">odd</a>" +
		"<div style=\"height:3000px\"></div><p id=\"bottom\">The end</p></body></html>";

	/// <summary>The harness with the session entry rewritten to name an .html, an .htm and an .svg file (three paths).</summary>
	private static PreviewWindowsHarness Harness(string? previewsJson = null, Func<int, string>? page = null)
	{
		var h = new PreviewWindowsHarness(previewsJson: previewsJson);
		var html = h.Env.WriteFile("web/page.html", (page ?? PageHtml)(h.WebPort));
		var htm = h.Env.WriteFile("web/other.htm", "<html><body><h2>Other page</h2></body></html>");
		var svg = h.Env.WriteFile("web/drawing.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");
		h.Env.WriteBodies("what did you produce?", $"The page is {html}, another is {htm} and the drawing is {svg}.");
		return h;
	}

	private static async Task<(Process Process, CdpPage Transcript, CdpPage Preview)> StartAsync(PreviewWindowsHarness h, int monitors, int pathIndex)
	{
		var port = CdpPage.FreePort();
		var process = await h.StartAsync(monitors, port);
		var transcript = await CdpPage.ConnectAsync(port, "transcript.html", PreviewWindowsHarness.StartTimeout);
		await h.WaitUntilAsync("the three paths to render inline", async () => (int)(await transcript.EvalAsync("document.querySelectorAll('.pth').length"))! == 3);
		await PreviewWindowsHarness.CtrlClickAsync(transcript, ".pth", pathIndex);
		var preview = await CdpPage.ConnectAsync(port, "preview.html", PreviewWindowsHarness.StartTimeout);
		return (process, transcript, preview);
	}

	private static async Task<string> EvalFrameAsync(CdpPage preview, string expression) =>
		(await preview.EvalAsync($"(() => {{ const f = {Frame}; const d = f && f.contentDocument; if (!d) return ''; return String({expression}); }})()"))?.ToString() ?? string.Empty;

	private static Task WaitForFrameAsync(PreviewWindowsHarness h, CdpPage preview, string selector) =>
		h.WaitUntilAsync("the html frame to show " + selector, async () => await EvalFrameAsync(preview, $"d.querySelector('{selector}') ? 'yes' : ''") == "yes");

	private static async Task<int> CountAsync(CdpPage preview, string selector) => (int)(await preview.EvalAsync($"document.querySelectorAll('{selector}').length"))!;

	[Fact]
	public async Task TC_LK_36_37_38_39_40_42_43_44_an_html_file_is_rendered_static_in_a_sandboxed_frame_the_source_toggles_and_its_links_go_through_the_preview_routing()
	{
		const string groups = "{\"groups\":[{\"id\":\"local\",\"name\":\"Local\",\"patterns\":\"http://127.0.0.1:*\",\"autoClose\":false}]}";
		using var h = Harness(groups);
		var (process, transcript, preview) = await StartAsync(h, monitors: 1, pathIndex: 0);
		try
		{
			await h.WaitForLogAsync(process, "the default panel to open", "Preview panel opened: group default");
			await WaitForFrameAsync(h, preview, "h1");

			// Rendered, in a sandbox without scripts, forms, popups, modals or top navigation.
			Assert.Equal("allow-same-origin", (await preview.EvalAsync($"{Frame}.getAttribute('sandbox')"))!.ToString());
			Assert.Equal("Heading", await EvalFrameAsync(preview, "d.querySelector('h1').textContent"));   // the script did not run
			Assert.Equal("rgb(200, 0, 0)", await EvalFrameAsync(preview, "f.contentWindow.getComputedStyle(d.querySelector('h1')).color"));   // inline <style> applies
			Assert.Equal("1", await EvalFrameAsync(preview, "d.getElementById('data').naturalWidth"));       // the data: image loaded
			Assert.Equal("0", await EvalFrameAsync(preview, "d.getElementById('remote').naturalWidth"));     // the remote image is not shown
			Assert.DoesNotContain("/remote.png", h.WebRequests);                                             // and was never requested

			// The meta refresh would navigate the frame after a second: it stays cancelled, the page and the content are unchanged.
			await Task.Delay(2500);
			Assert.Equal("Heading", await EvalFrameAsync(preview, "d.querySelector('h1').textContent"));
			Assert.Contains("preview.html", (await preview.EvalAsync("location.href"))!.ToString());

			// The source, and back.
			var main = h.MainWindow(process);
			Assert.True(await h.PressAsync(process, main, SourceInPanel), "The Source button is missing for an html file.");
			await h.WaitUntilAsync("the source as text", async () => await CountAsync(preview, "pre.src") == 1);
			Assert.Contains("<script>document.querySelector('h1')", (await preview.EvalAsync("document.querySelector('pre.src').textContent"))!.ToString());
			Assert.Equal(0, await CountAsync(preview, "iframe.hframe"));
			Assert.True(await h.PressAsync(process, main, SourceInPanel));
			await WaitForFrameAsync(h, preview, "h1");

			// An #anchor scrolls the frame; an odd link does nothing; an http link goes through the preview routing (the Local group's panel).
			await preview.EvalAsync($"{Frame}.contentDocument.getElementById('anc').click()");
			await h.WaitUntilAsync("the frame to scroll to the anchor", async () => double.Parse(await EvalFrameAsync(preview, "d.scrollingElement.scrollTop"), CultureInfo.InvariantCulture) > 500);
			await preview.EvalAsync($"{Frame}.contentDocument.getElementById('odd').click()");
			await Task.Delay(500);
			Assert.DoesNotContain("win.ini", h.App.ReadLog());
			await preview.EvalAsync($"{Frame}.contentDocument.getElementById('web').click()");
			await h.WaitForLogAsync(process, "the http link to be routed to the Local group", "Preview panel opened: group local");
			Assert.Contains("preview.html", (await preview.EvalAsync("location.href"))!.ToString());
			h.AssertNoErrors();
		}
		finally
		{
			preview.Dispose();
			transcript.Dispose();
			h.CloseApp(process);
		}
	}

	private static string RefreshPage(string content) =>
		"<!DOCTYPE html><html><head><meta http-equiv=\"refresh\" content=\"" + content + "\"></head><body><h1 id=\"top\">Refresh</h1><a id=\"web\" href=\"http://127.0.0.1:1/a\">the web</a></body></html>";

	[Theory]
	[InlineData("0")]
	[InlineData("0;url=about:srcdoc")]
	public async Task TC_LK_41_44_a_refresh_meta_to_the_same_document_does_not_reload_the_frame_and_the_links_still_work(string refresh)
	{
		const string groups = "{\"groups\":[{\"id\":\"local\",\"name\":\"Local\",\"patterns\":\"http://127.0.0.1:*\",\"autoClose\":false}]}";
		using var h = Harness(groups, _ => RefreshPage(refresh));
		var (process, transcript, preview) = await StartAsync(h, monitors: 1, pathIndex: 0);
		try
		{
			await WaitForFrameAsync(h, preview, "h1");
			await preview.EvalAsync($"window.__doc = {Frame}.contentDocument; window.__loads = 0; {Frame}.addEventListener('load', () => window.__loads++)");

			// A reload loop would replace the frame's document within these seconds.
			await Task.Delay(3000);
			Assert.Equal("true", (await preview.EvalAsync($"{Frame}.contentDocument === window.__doc"))!.ToString(), ignoreCase: true);
			Assert.Equal("0", (await preview.EvalAsync("window.__loads"))!.ToString());
			Assert.Equal("Refresh", await EvalFrameAsync(preview, "d.querySelector('h1').textContent"));

			await preview.EvalAsync($"{Frame}.contentDocument.getElementById('web').click()");
			await h.WaitForLogAsync(process, "the http link to be routed to the Local group", "Preview panel opened: group local");
			h.AssertNoErrors();
		}
		finally
		{
			preview.Dispose();
			transcript.Dispose();
			h.CloseApp(process);
		}
	}

	private static string PolicyPage(int port) =>
		"<!-- a comment --><!DOCTYPE html><html><head></head><body><h1>Policy</h1>" +
		"<img id=\"cached\" src=\"https://preview.local/probe.png\"><img id=\"remote\" src=\"http://127.0.0.1:" + port + "/remote.png\">" +
		"<img id=\"map\" usemap=\"#m\" src=\"data:image/png;base64," + OnePixelPng + "\" width=\"40\" height=\"40\"><map name=\"m\"><area id=\"area\" shape=\"rect\" coords=\"0,0,40,40\" href=\"http://127.0.0.1:" + port + "/a\"></map>" +
		"<a id=\"bad\" href=\"#%E0%A4%A\">bad</a> <a id=\"anc\" href=\"#bottom\">down</a><div style=\"height:3000px\"></div><p id=\"bottom\">The end</p></body></html>";

	[Fact]
	public async Task TC_LK_39_41_42_43_the_frame_shows_no_cached_media_requests_nothing_and_handles_area_links_and_malformed_anchors()
	{
		const string groups = "{\"groups\":[{\"id\":\"local\",\"name\":\"Local\",\"patterns\":\"http://127.0.0.1:*\",\"autoClose\":false}]}";
		using var h = Harness(groups, PolicyPage);
		var (process, transcript, preview) = await StartAsync(h, monitors: 1, pathIndex: 0);
		try
		{
			await WaitForFrameAsync(h, preview, "h1");
			h.WriteCacheFile("probe.png", Convert.FromBase64String(OnePixelPng));   // after the start: the cache folder is emptied then
			await preview.EvalAsync($"{Frame}.contentDocument.getElementById('cached').src = 'https://preview.local/probe.png?again'");   // try the frame again, now that the file exists
			await Task.Delay(1000);

			// Control: the preview page itself can show the cached file; the html frame, under its own stricter policy, cannot.
			Assert.Equal("1", (await preview.EvalAsync("new Promise(r => { const i = new Image(); i.onload = () => r(i.naturalWidth); i.onerror = () => r(0); i.src = 'https://preview.local/probe.png'; })"))!.ToString());
			Assert.Equal("0", await EvalFrameAsync(preview, "d.getElementById('cached').naturalWidth"));
			Assert.Equal("0", await EvalFrameAsync(preview, "d.getElementById('remote').naturalWidth"));
			Assert.DoesNotContain("/remote.png", h.WebRequests);

			// The policy meta came after the doctype (a comment before it was kept): the page is still in standards mode.
			Assert.Equal("CSS1Compat", await EvalFrameAsync(preview, "d.compatMode"));
			Assert.Equal("1", await EvalFrameAsync(preview, "d.querySelectorAll('meta[http-equiv=\"Content-Security-Policy\"]').length"));

			// A malformed anchor does nothing (the frame keeps working); a good anchor still scrolls.
			await preview.EvalAsync($"{Frame}.contentDocument.getElementById('bad').click()");
			await preview.EvalAsync($"{Frame}.contentDocument.getElementById('anc').click()");
			await h.WaitUntilAsync("the frame to scroll to the anchor", async () => double.Parse(await EvalFrameAsync(preview, "d.scrollingElement.scrollTop"), CultureInfo.InvariantCulture) > 500);

			// An <area href> is intercepted like an <a href>: it goes through the preview routing.
			await preview.EvalAsync($"{Frame}.contentDocument.getElementById('area').click()");
			await h.WaitForLogAsync(process, "the area link to be routed to the Local group", "Preview panel opened: group local");
			Assert.Contains("preview.html", (await preview.EvalAsync("location.href"))!.ToString());
			h.AssertNoErrors();
		}
		finally
		{
			preview.Dispose();
			transcript.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_LK_45_46_an_htm_file_is_rendered_too_and_an_svg_stays_text()
	{
		using var h = Harness();
		var (process, transcript, preview) = await StartAsync(h, monitors: 1, pathIndex: 1);
		try
		{
			await WaitForFrameAsync(h, preview, "h2");
			Assert.Equal("Other page", await EvalFrameAsync(preview, "d.querySelector('h2').textContent"));

			await PreviewWindowsHarness.CtrlClickAsync(transcript, ".pth", 2);
			await h.WaitUntilAsync("the svg as text", async () => await CountAsync(preview, "pre.src") == 1);
			Assert.Equal(0, await CountAsync(preview, "iframe.hframe"));
			Assert.Contains("<script>alert(1)</script>", (await preview.EvalAsync("document.querySelector('pre.src').textContent"))!.ToString());
			Assert.Null(PreviewWindowsHarness.FindElement(h.MainWindow(process), SourceInPanel));   // no Source toggle for text
			h.AssertNoErrors();
		}
		finally
		{
			preview.Dispose();
			transcript.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_LK_48_a_group_window_on_a_second_monitor_renders_the_html_and_has_the_Source_and_Open_in_default_app_buttons()
	{
		const string groups = "{\"groups\":[{\"id\":\"web-pages\",\"name\":\"Web pages\",\"patterns\":\"html htm\",\"autoClose\":false}]}";
		using var h = Harness(groups);
		var (process, transcript, preview) = await StartAsync(h, monitors: 2, pathIndex: 0);
		try
		{
			await h.WaitForTitlesAsync(process, "page.html - Web pages");
			await WaitForFrameAsync(h, preview, "h1");
			Assert.Equal("Heading", await EvalFrameAsync(preview, "d.querySelector('h1').textContent"));

			var window = h.PreviewWindowTitled(process, "page.html - Web pages");
			Assert.True(await h.PressAsync(process, window, SourceInWindow), "The window has no Source button for an html file.");
			await h.WaitUntilAsync("the source as text", async () => await CountAsync(preview, "pre.src") == 1);
			Assert.True(await h.PressAsync(process, window, SourceInWindow));
			await WaitForFrameAsync(h, preview, "h1");
			// TC.LK.51: Open in default app is offered for the page; the test launcher receives the cache copy (content.html), nothing is started.
			Assert.True(await h.PressAsync(process, window, "Open in default app"), "The window has no Open in default app button for an html file.");
			await h.WaitForLogAsync(process, "the test launcher logs the html open", "External open requested (test window, nothing started): .html file");
			h.AssertNoErrors();
		}
		finally
		{
			preview.Dispose();
			transcript.Dispose();
			h.CloseApp(process);
		}
	}
}
