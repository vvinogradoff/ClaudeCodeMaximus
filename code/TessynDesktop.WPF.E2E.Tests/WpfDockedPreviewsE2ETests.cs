namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the docked preview panels, stage 1 (TC.PW.77–TC.PW.84 area, FR.29.17–FR.29.20): the BUILT WPF app with ONE scripted monitor (docked mode) against a real loopback
/// daemon. A Ctrl+click on a path in the transcript opens the group's panel inside the main window (no window of its own), found through UI Automation by its Pin button's name and
/// through the log line the dock layer writes; a file with no group opens the default Preview panel; the transcript keeps working.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfDockedPreviewsE2ETests
{
	private const string PinTextCode = "Pin panel Text and code";

	[Fact]
	public async Task TC_PW_77_a_ctrl_click_opens_the_Text_and_code_panel_inside_the_main_window_and_a_second_md_replaces_it_and_the_transcript_keeps_working()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		try
		{
			// A plain click opens nothing.
			await PreviewWindowsHarness.ClickAsync(page, ".pth", 0);
			await Task.Delay(1500);
			Assert.DoesNotContain("Preview panel opened", h.App.ReadLog());

			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
			await h.WaitForLogAsync(process, "the panel to open", "Preview panel opened: group text-code");
			var main = h.MainWindow(process);
			await h.WaitUntilAsync("the panel's Pin button in the main window", () => Task.FromResult(PreviewWindowsHarness.FindElement(main, PinTextCode) != null));
			Assert.Equal("Pin panel Text and code", PreviewWindowsHarness.FindElement(main, PinTextCode)!.Current.Name);
			Assert.NotNull(PreviewWindowsHarness.FindElement(main, "Auto-close panel Text and code"));
			Assert.Empty(h.PreviewWindows(process));   // docked mode: no window of its own (FR.29.17)

			// A second markdown file replaces the content of the same panel: still one Pin button, no second panel.
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 1);
			await h.WaitForLogAsync(process, "the second file to render", "plan2.md");
			Assert.Equal(1, h.App.ReadLog().Split("Preview panel opened: group text-code").Length - 1);
			Assert.Empty(h.PreviewWindows(process));

			// The transcript keeps working: its paths are still there and answer a click.
			Assert.Equal(4, (int)(await page.EvalAsync("document.querySelectorAll('.pth').length"))!);
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_78_a_file_with_no_group_opens_the_default_Preview_panel_without_Pin_or_Auto_close_and_a_group_file_opens_its_own_panel_beside_it()
	{
		// Only the Images group exists: the markdown file belongs to no group.
		const string onlyImages = "{\"groups\":[{\"id\":\"images\",\"name\":\"Images\",\"patterns\":\"tif tiff\",\"autoClose\":false}]}";
		using var h = new PreviewWindowsHarness(previewsJson: onlyImages);
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
			await h.WaitForLogAsync(process, "the default panel to open", "Preview panel opened: group default");
			var main = h.MainWindow(process);
			await h.WaitUntilAsync("the default panel's Source button", () => Task.FromResult(PreviewWindowsHarness.FindElement(main, "Open in default app from panel Preview") != null));
			Assert.Null(PreviewWindowsHarness.FindElement(main, "Pin panel Preview"));   // no Pin, no Auto-close on the default panel (FR.29.18)
			Assert.Empty(h.PreviewWindows(process));

			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 3);
			await h.WaitForLogAsync(process, "the Images panel to open", "Preview panel opened: group images");
			await h.WaitUntilAsync("the Images panel's Pin button", () => Task.FromResult(PreviewWindowsHarness.FindElement(main, "Pin panel Images") != null));
			Assert.NotNull(PreviewWindowsHarness.FindElement(main, "Open in default app from panel Preview"));   // the default panel is still there
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_79_Pin_in_a_docked_panel_is_one_way_and_the_groups_next_item_opens_a_new_panel()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
			await h.WaitForLogAsync(process, "the panel to open", "Preview panel opened: group text-code");
			var main = h.MainWindow(process);
			Assert.True(await h.PressAsync(process, main, PinTextCode));
			await h.WaitForLogAsync(process, "the panel to be pinned", "Preview panel pinned: group text-code");

			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 1);
			await h.WaitUntilAsync("a second panel of the group", () => Task.FromResult(h.App.ReadLog().Split("Preview panel opened: group text-code").Length - 1 == 2));
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}
}
