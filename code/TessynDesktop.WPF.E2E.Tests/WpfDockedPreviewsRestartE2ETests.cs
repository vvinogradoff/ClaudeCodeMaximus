using System.Text.Json.Nodes;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the docked preview panels across a restart and a mode change (FR.29.22, FR.29.23): the BUILT WPF app against a real loopback daemon. The open panels are saved in
/// <c>previewDock</c> (<c>openPanels</c>) in the test data root, so a restart reopens them with their items; the mode changes with the Monitor toggle. State is read from the log lines and the
/// saved section. A panel closed by the user, a floating panel's rectangle and a layout dragged to another place need the programmatic dock hook (FR.29.26) and stay for the QA stage.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfDockedPreviewsRestartE2ETests
{
	private const string ToggleName = "Preview windows";
	private const string PinTextCode = "Pin panel Text and code";
	private const string Docked = "{\"enabled\":false,\"placements\":{},\"openWindows\":[]}";

	private static int Occurrences(PreviewWindowsHarness h, string needle) => h.App.ReadLog().Split(needle).Length - 1;

	private static int OpenPanelCount(PreviewWindowsHarness h) => h.Section("previewDock")?["openPanels"]?.AsArray().Count ?? 0;

	[Fact]
	public async Task TC_PW_98_the_open_docked_panels_come_back_with_their_items_after_a_restart_and_the_list_follows_what_is_open()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);   // plan1.md: the Text and code panel
			await h.WaitForLogAsync(process, "the Text and code panel to open", "Preview panel opened: group text-code");
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 3);   // the tiff: the Images panel
			await h.WaitForLogAsync(process, "the Images panel to open", "Preview panel opened: group images");
			var saved = await h.WaitForSectionAsync("previewDock", d => d?["openPanels"]?.AsArray().Count == 2, "both panels in the saved list");
			Assert.Equal(["images", "text-code"], saved!["openPanels"]!.AsArray().Select(p => p!["groupId"]!.GetValue<string>()).Order().ToArray());
			Assert.Contains("plan1.md", saved["openPanels"]!.AsArray().Single(p => p!["groupId"]!.GetValue<string>() == "text-code")!["item"]!["fileName"]!.GetValue<string>());
			Assert.True(h.PreviewWindows(process).Count == 0);   // docked mode: panels, not windows
		}
		finally
		{
			page.Dispose();
		}

		h.CloseApp(process);
		Assert.True(process.HasExited);
		Assert.Equal(2, OpenPanelCount(h));   // the list survives the exit: closing the panels at exit does not empty it

		process = await h.StartAsync(monitors: 1);
		try
		{
			await h.WaitForLogAsync(process, "both panels to be restored", "Preview windows restored: 2 open");
			var log = h.App.ReadLog();
			Assert.Contains("Restore: reopened a group panel of group text-code", log);
			Assert.Contains("Restore: reopened a group panel of group images", log);
			Assert.Equal(2, Occurrences(h, "Preview panel opened: group text-code"));   // once per run
			Assert.Equal(2, Occurrences(h, "Preview panel opened: group images"));
			Assert.Equal(2, OpenPanelCount(h));
			Assert.Empty(h.PreviewWindows(process));
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_99_when_external_windows_become_active_group_panels_close_pinned_panels_stay_nothing_reopens_and_a_restart_brings_the_pinned_panel_back_docked()
	{
		using var h = new PreviewWindowsHarness(previewWindowsJson: Docked);
		var (process, page) = await h.StartWithPageAsync(monitors: 2);   // two monitors, the toggle off: docked mode
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
			await h.WaitForLogAsync(process, "the Text and code panel to open", "Preview panel opened: group text-code");
			var main = h.MainWindow(process);
			Assert.True(await h.PressAsync(process, main, PinTextCode));
			await h.WaitForLogAsync(process, "the panel to be pinned", "Preview panel pinned: group text-code");
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 3);
			await h.WaitForLogAsync(process, "the Images panel to open", "Preview panel opened: group images");
			await h.WaitForSectionAsync("previewDock", d => d?["openPanels"]?.AsArray().Count == 2, "both panels in the saved list");

			Assert.True(await h.PressAsync(process, main, ToggleName));   // external windows become active
			await h.WaitForLogAsync(process, "the group panel to close", "Closing the preview window of group images: the mode turned on", "Preview panel closed: group images (by user false)");
			await h.WaitForSectionAsync("previewDock", d => d?["openPanels"]?.AsArray().Count == 1, "only the pinned panel in the saved list");
			Assert.DoesNotContain("Preview panel closed: group text-code", h.App.ReadLog());   // the pinned panel stays
			Assert.Single(h.Section("previewDock")!["openPanels"]!.AsArray().Where(p => p!["pinned"]!.GetValue<bool>()));

			// Nothing reopens by itself, and the next item of a group goes to a window.
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 1);
			await h.WaitForTitlesAsync(process, "plan2.md - Text and code");
			Assert.Equal(1, Occurrences(h, "Preview panel opened: group images"));
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
		}

		h.CloseApp(process);
		Assert.True(process.HasExited);

		process = await h.StartAsync(monitors: 2);   // the toggle is on now: external windows
		try
		{
			await h.WaitForLogAsync(process, "the restore to finish", "Restore: reopened a pinned panel of group text-code");
			await h.WaitForTitlesAsync(process, "plan2.md - Text and code");   // the group window comes back as a window
			Assert.Equal(2, Occurrences(h, "Preview panel opened: group text-code"));   // the pinned panel stays a docked panel in either mode
			Assert.Equal(1, Occurrences(h, "Preview panel opened: group images"));   // the closed group panel did not come back
			Assert.Equal(1, OpenPanelCount(h));
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}
}
