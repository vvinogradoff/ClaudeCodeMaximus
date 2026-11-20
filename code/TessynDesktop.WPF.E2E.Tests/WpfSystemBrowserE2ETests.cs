using System.Text.RegularExpressions;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the forced system-browser open of a transcript link (FR.26.9, TC.LK.55, 56, 60, 61): the BUILT WPF app with two scripted monitors and a group that matches the
/// link's address (the preview windows mode is on), against a real loopback daemon. The transcript page is driven through the DevTools port with real mouse events
/// (<c>Input.dispatchMouseEvent</c> with the modifiers). Under <c>--test-window</c> the system launcher logs the address instead of starting a browser.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfSystemBrowserE2ETests
{
	private const int Shift = 8;   // DevTools modifier bit

	private static int SystemOpens(PreviewWindowsHarness h, string url) =>
		Regex.Matches(h.App.ReadLog(), Regex.Escape("System browser (test window): " + url)).Count;

	[Fact]
	public async Task TC_LK_55_60_61_Ctrl_Shift_click_and_the_menu_entry_open_the_system_browser_and_Ctrl_click_still_opens_the_group_window()
	{
		using var h = new PreviewWindowsHarness(splitGroups: true);
		var (process, page) = await h.StartWithPageAsync(monitors: 2);
		try
		{
			var url = h.WebUrlA;

			// TC.LK.60: a plain click on the link posts and opens nothing.
			await PreviewWindowsHarness.ClickAsync(page, ".b a", 0);
			await Task.Delay(800);
			Assert.Equal(0, SystemOpens(h, url));
			await h.WaitForTitlesAsync(process);

			// TC.LK.55 / TC.LK.56: Ctrl+Shift+click goes to the system launcher; no preview window opens although a group matches and the mode is on.
			var (x, y) = await PointAsync(page, ".b a");
			await page.MouseMoveAsync(x, y, modifiers: PreviewWindowsHarness.Ctrl | Shift);
			await page.ClickAsync(x, y, modifiers: PreviewWindowsHarness.Ctrl | Shift);
			await h.WaitUntilAsync("the system launcher to log the link", () => Task.FromResult(SystemOpens(h, url) == 1));
			await Task.Delay(800);
			await h.WaitForTitlesAsync(process);
			Assert.DoesNotContain("Preview: ", h.App.ReadLog());

			// TC.LK.61: a right-click on the link shows only "Open in system browser" (no selection, so no Copy entries); choosing it uses the system launcher.
			await page.RightClickAsync(x, y);
			await h.WaitUntilAsync("the page menu for the link", async () => (bool)(await page.EvalAsync("!document.querySelector('.cm').hidden"))!);
			Assert.False((bool)(await page.EvalAsync("document.querySelector('.cm [data-act=system]').hidden"))!);
			Assert.Equal("Open in system browser", (string)(await page.EvalAsync("document.querySelector('.cm [data-act=system]').firstChild.textContent"))!);
			Assert.Equal("Ctrl+Shift+click", (string)(await page.EvalAsync("document.querySelector('.cm [data-act=system] .k').textContent"))!);
			Assert.True((bool)(await page.EvalAsync("document.querySelector('.cm [data-act=text]').hidden && document.querySelector('.cm [data-act=markdown]').hidden"))!);
			var entry = await PointAsync(page, ".cm [data-act=system]");
			await page.ClickAsync(entry.X, entry.Y);
			await h.WaitUntilAsync("the menu entry to reach the system launcher", () => Task.FromResult(SystemOpens(h, url) == 2));
			await h.WaitForTitlesAsync(process);

			// A right-click on text that is not a link (and not on a selection) shows no menu.
			var text = await PointAsync(page, ".b");
			await page.RightClickAsync(text.X + 2, text.Y);
			await Task.Delay(400);
			Assert.True((bool)(await page.EvalAsync("document.querySelector('.cm').hidden"))!);

			// TC.LK.60: Ctrl+click alone is still the routed open: the group window shows the page and the system launcher is not used again.
			await PreviewWindowsHarness.CtrlClickAsync(page, ".b a", 0);
			await h.WaitForTitlesAsync(process, "Page A - Local");
			Assert.Equal(2, SystemOpens(h, url));
			Assert.Contains("Preview: ", h.App.ReadLog());
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	private static async Task<(double X, double Y)> PointAsync(CdpPage page, string selector)
	{
		var script = $"(() => {{ const e = document.querySelector({System.Text.Json.JsonSerializer.Serialize(selector)}); e.scrollIntoView({{ block: 'center' }}); const r = e.getClientRects()[0]; return [r.left, r.right, r.top, r.bottom]; }})()";
		await page.EvalAsync(script);
		await Task.Delay(150);
		var a = (System.Text.Json.Nodes.JsonArray)(await page.EvalAsync(script))!;
		return (a[0]!.GetValue<double>() + Math.Min(6, (a[1]!.GetValue<double>() - a[0]!.GetValue<double>()) / 2), (a[2]!.GetValue<double>() + a[3]!.GetValue<double>()) / 2);
	}
}
