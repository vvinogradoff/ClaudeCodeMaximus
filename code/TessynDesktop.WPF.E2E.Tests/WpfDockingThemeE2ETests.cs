namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the docking theme (TC.DS.23, FR.6.21): the BUILT WPF app, one scripted monitor, a docked preview panel open. A day/night switch (the title bar's Toggle theme button,
/// twice) must not reload any page inside a panel: a marker variable set in the transcript page (the Session view) and in the preview panel's page before the switch is still there
/// after each one, the stored theme changed, and the app logged no error. The colours themselves (tabs, edge tabs, slide-out, floating frames, drop guides) are checked by the view
/// tests (TC.DS.24) and by looking (TC.DS.23, MANUAL): a pixel is not readable through UI Automation.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfDockingThemeE2ETests
{
	private const string Marker = "(window.__themeMarker = 'alive-42')";
	private const string ReadMarker = "window.__themeMarker || null";
	private const string ToggleTheme = "Toggle theme";

	[Fact]
	public async Task TC_DS_23_a_day_night_switch_with_a_docked_preview_open_reloads_no_page_and_logs_no_error()
	{
		using var h = new PreviewWindowsHarness();
		var port = CdpPage.FreePort();
		var process = await h.StartAsync(monitors: 1, port);
		CdpPage? transcript = null;
		CdpPage? preview = null;
		try
		{
			transcript = await CdpPage.ConnectAsync(port, "transcript.html", PreviewWindowsHarness.StartTimeout);
			await h.WaitUntilAsync("the four paths to render inline", async () => (int)(await transcript.EvalAsync("document.querySelectorAll('.pth').length"))! == 4);

			await PreviewWindowsHarness.CtrlClickAsync(transcript, ".pth", 0);
			await h.WaitForLogAsync(process, "the docked panel to open", "Preview panel opened: group text-code");
			// The page must have committed and rendered before the marker is set: on a cold start (the first run after a build) the DevTools target already
			// reports preview.html while the placeholder document is still the live one, and the marker would be set on a document the page then replaces.
			await h.WaitForLogAsync(process, "the preview page to render", "Preview page rendered");
			preview = await CdpPage.ConnectAsync(port, "preview.html", PreviewWindowsHarness.StartTimeout);
			var main = h.MainWindow(process);
			Assert.Empty(h.PreviewWindows(process));   // docked: the page is inside the main window

			Assert.Equal("alive-42", (await transcript.EvalAsync(Marker))?.GetValue<string>());
			Assert.Equal("alive-42", (await preview.EvalAsync(Marker))?.GetValue<string>());

			var before = Theme(h);
			Assert.True(await h.PressAsync(process, main, ToggleTheme));
			await h.WaitForSectionAsync("appearance", _ => Theme(h) is { } now && now != before, "the theme to change");
			var first = Theme(h);
			await Task.Delay(1500);   // the recolour and any reload would have happened by now
			Assert.Equal("alive-42", (await transcript.EvalAsync(ReadMarker))?.GetValue<string>());   // the Session view's page is not reloaded
			Assert.Equal("alive-42", (await preview.EvalAsync(ReadMarker))?.GetValue<string>());   // nor the docked preview's

			Assert.True(await h.PressAsync(process, main, ToggleTheme));
			await h.WaitForSectionAsync("appearance", _ => Theme(h) != first, "the theme to change back");
			await Task.Delay(1500);
			Assert.Equal("alive-42", (await transcript.EvalAsync(ReadMarker))?.GetValue<string>());
			Assert.Equal("alive-42", (await preview.EvalAsync(ReadMarker))?.GetValue<string>());
			Assert.Contains("Preview panel opened: group text-code", h.App.ReadLog());
			Assert.DoesNotContain("Preview panel closed", h.App.ReadLog());   // the panel was not recreated
			h.AssertNoErrors();
		}
		finally
		{
			transcript?.Dispose();
			preview?.Dispose();
			h.CloseApp(process);
		}
	}

	/// <summary>The stored theme choice (null while the user has not chosen one).</summary>
	private static string? Theme(PreviewWindowsHarness h) => h.Section("appearance")?["theme"]?.GetValue<string>();
}
