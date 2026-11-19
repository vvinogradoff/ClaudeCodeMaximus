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
	private static PreviewWindowsHarness Harness(string? previewsJson = null)
	{
		var h = new PreviewWindowsHarness(previewsJson: previewsJson);
		var html = h.Env.WriteFile("web/page.html", PageHtml(h.WebPort));
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
	public async Task TC_LK_36_to_47_an_html_file_is_rendered_static_in_a_sandboxed_frame_the_source_toggles_and_its_links_go_through_the_preview_routing()
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
			Assert.Equal("0", await EvalFrameAsync(preview, "d.getElementById('remote').naturalWidth"));     // the remote image was not fetched

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

	[Fact]
	public async Task TC_LK_48_an_htm_file_is_rendered_too_and_an_svg_stays_text()
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
	public async Task TC_LK_49_a_group_window_on_a_second_monitor_renders_the_html_and_has_the_Source_button()
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
