using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows.Automation;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the docked preview panels that need both the panel behaviours and the programmatic docking test hook (FR.29.22, FR.29.23, FR.29.17, decision 3; TC.PW.90, TC.PW.93, TC.PW.96,
/// TC.PW.98, TC.PW.99): the BUILT WPF app against a real loopback daemon. A Ctrl+click in the transcript opens the group's panel, the hook (<see cref="WpfDockingHookClient"/>) closes, docks,
/// floats, tabs and auto-hides it, and the state reply (with the slide-out flag) tells where everything is. Restarts reuse the test data root, so the saved <c>previewDock</c> list is what comes back.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfDockedPanelsStage3E2ETests
{
	private const string TextCode = "text-code";
	private const string Images = "images";
	private const string DefaultPanel = "default";
	private const string Focus = "focus";
	private const string ToggleName = "Preview windows";
	private const string PinTextCode = "Pin panel Text and code";
	private const string PinImages = "Pin panel Images";
	private const string Docked = "{\"enabled\":false,\"placements\":{},\"openWindows\":[]}";
	private const string TwoGroups = "{\"groups\":[{\"id\":\"images\",\"name\":\"Images\",\"patterns\":\"tif tiff\",\"autoClose\":false},{\"id\":\"text-code\",\"name\":\"Text and code\",\"patterns\":\"md txt\",\"autoClose\":false}]}";   // main.cpp belongs to no group: the default panel
	private const int Plan1 = 0;
	private const int Plan2 = 1;
	private const int Cpp = 2;
	private const int Tiff = 3;

	private static int Occurrences(PreviewWindowsHarness h, string needle) => h.App.ReadLog().Split(needle).Length - 1;

	private static JsonArray OpenPanels(PreviewWindowsHarness h) => h.Section("previewDock")?["openPanels"]?.AsArray() ?? [];

	/// <summary>Ctrl+clicks the path and waits until the router has opened an item for it.</summary>
	private static async Task OpenAsync(PreviewWindowsHarness h, CdpPage page, int index, string what)
	{
		var before = Occurrences(h, "Preview: ");
		await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", index);
		await h.WaitUntilAsync($"an item opened for {what}", () => Task.FromResult(Occurrences(h, "Preview: ") > before));
	}

	/// <summary>A Ctrl+click made by a script in the page: no mouse move or real click, so nothing in the app takes the keyboard focus.</summary>
	private static async Task ScriptCtrlClickAsync(PreviewWindowsHarness h, CdpPage page, int index)
	{
		var before = Occurrences(h, "Preview: ");
		await page.EvalAsync($"document.querySelectorAll('.pth')[{index}].dispatchEvent(new MouseEvent('click', {{ ctrlKey: true, bubbles: true, cancelable: true, view: window }}))");
		await h.WaitUntilAsync("an item opened by the scripted click", () => Task.FromResult(Occurrences(h, "Preview: ") > before));
	}

	/// <summary>Closes the app the way the user does and starts it again on the same data root.</summary>
	private static async Task<Process> RestartAsync(PreviewWindowsHarness h, Process process, int monitors)
	{
		h.CloseApp(process);
		Assert.True(process.HasExited);
		return await h.StartAsync(monitors);
	}

	private static string? PinnedId(DockingHookReply state, string group) => state.Panels.FirstOrDefault(p => p.Shown && p.Id.StartsWith(group + "#pinned", StringComparison.Ordinal))?.Id;

	/// <summary>What has the keyboard focus right now, for a failure message.</summary>
	private static string FocusedDescription()
	{
		try
		{
			var focused = AutomationElement.FocusedElement;
			return focused == null ? "(none)" : $"{focused.Current.ProcessId}:{focused.Current.ControlType.ProgrammaticName}:{focused.Current.Name}";
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
		{
			return "(gone)";
		}
	}

	// ---- FR.29.22: a panel the user closed does not come back ----

	[Fact]
	public async Task TC_PW_98_a_panel_the_user_closed_is_not_reopened_after_a_restart_one_left_open_comes_back_with_its_item()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		try
		{
			await OpenAsync(h, page, Plan1, "the Text and code panel");
			await OpenAsync(h, page, Tiff, "the Images panel");
			await h.WaitForSectionAsync("previewDock", d => d?["openPanels"]?.AsArray().Count == 2, "both panels in the saved list");
			await hook.WaitStateAsync(s => s.HasPanel(TextCode) && s.Panel(TextCode).Shown && s.HasPanel(Images) && s.Panel(Images).Shown, "both panels shown");

			Assert.True((await hook.CloseAsync(Images)).Ok);   // the tab's Close: the user closed it
			await hook.WaitStateAsync(s => !s.HasPanel(Images) || !s.Panel(Images).Shown, "the Images panel closed");
			var saved = await h.WaitForSectionAsync("previewDock", d => d?["openPanels"]?.AsArray().Count == 1, "only the Text and code panel in the saved list");
			Assert.Equal(TextCode, saved!["openPanels"]![0]!["groupId"]!.GetValue<string>());
		}
		finally
		{
			hook.Dispose();
			page.Dispose();
		}

		var next = await RestartAsync(h, process, monitors: 1);
		var hook2 = await WpfDockingHookClient.ConnectAsync(next);
		try
		{
			await h.WaitForLogAsync(next, "the one panel to be restored", "Preview windows restored: 1 open", "Restore: reopened a group panel of group text-code");
			var state = await hook2.WaitStateAsync(s => s.HasPanel(TextCode) && s.Panel(TextCode).Shown, "the Text and code panel back");
			Assert.False(state.HasPanel(Images) && state.Panel(Images).Shown, "The closed Images panel came back");
			Assert.Equal(1, Occurrences(h, "Preview panel opened: group images"));   // the first run only
			Assert.Equal(2, Occurrences(h, "Preview panel opened: group text-code"));
			var list = OpenPanels(h);
			Assert.Single(list);
			Assert.Contains("plan1.md", list[0]!["item"]!["fileName"]!.GetValue<string>());
			h.AssertNoErrors();
		}
		finally
		{
			hook2.Dispose();
			h.CloseApp(next);
		}
	}

	// ---- FR.29.23: external windows turn on ----

	[Fact]
	public async Task TC_PW_99_group_panels_docked_elsewhere_or_floating_close_when_external_windows_turn_on_keep_their_places_and_come_back_there_with_the_next_item_pinned_and_default_panels_stay()
	{
		using var h = new PreviewWindowsHarness(previewWindowsJson: Docked, previewsJson: TwoGroups);
		var (process, page) = await h.StartWithPageAsync(monitors: 2);   // two monitors, the toggle off: docked mode
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		try
		{
			await OpenAsync(h, page, Plan1, "the Text and code panel");
			var main = h.MainWindow(process);
			Assert.True(await h.PressAsync(process, main, PinTextCode));
			await h.WaitForLogAsync(process, "the panel to be pinned", "Preview panel pinned: group text-code");
			await OpenAsync(h, page, Plan2, "a new Text and code group panel");   // next to the pinned one
			await OpenAsync(h, page, Tiff, "the Images panel");
			await OpenAsync(h, page, Cpp, "the default panel");
			var all = await hook.WaitStateAsync(s => s.HasPanel(TextCode) && s.HasPanel(Images) && s.HasPanel(DefaultPanel) && PinnedId(s, TextCode) != null
				&& s.Panel(TextCode).Shown && s.Panel(Images).Shown && s.Panel(DefaultPanel).Shown, "the group panels, the pinned panel and the default panel shown");
			var pinned = PinnedId(all, TextCode)!;

			Assert.True((await hook.DockAsync(TextCode, "bottom")).Ok);
			Assert.True((await hook.FloatAsync(Images)).Ok);
			await hook.WaitStateAsync(s => s.Panel(TextCode).Side == "bottom" && s.Panel(Images).Floating, "the Text and code panel at the bottom, the Images panel floating");
			await h.WaitForSectionAsync("previewDock", d => d?["layouts"]?[TextCode]?["side"]?.GetValue<int>() == 3 && d["layouts"]?[Images]?["floating"]?.GetValue<bool>() == true, "both layouts saved");

			Assert.True(await h.PressAsync(process, main, ToggleName));   // external windows become active
			await h.WaitForLogAsync(process, "the group panels to close", "Closing the preview window of group images: the mode turned on", "Closing the preview window of group text-code: the mode turned on");
			var closed = await hook.WaitStateAsync(s => !(s.HasPanel(TextCode) && s.Panel(TextCode).Shown) && !(s.HasPanel(Images) && s.Panel(Images).Shown), "the group panels closed");
			Assert.True(closed.Panel(pinned).Shown, "The pinned panel closed");
			Assert.True(closed.Panel(DefaultPanel).Shown, "The default Preview panel closed");
			await h.WaitForSectionAsync("previewDock", d => d?["layouts"]?[TextCode]?["side"]?.GetValue<int>() == 3 && d["layouts"]?[Images]?["floating"]?.GetValue<bool>() == true, "the layouts kept");

			Assert.True(await h.PressAsync(process, main, ToggleName));   // and off again
			await Task.Delay(2000);
			var idle = await hook.StateAsync();
			Assert.False((idle.HasPanel(TextCode) && idle.Panel(TextCode).Shown) || (idle.HasPanel(Images) && idle.Panel(Images).Shown), "A group panel reopened by itself");
			Assert.Equal(1, Occurrences(h, "Preview panel opened: group images"));

			await OpenAsync(h, page, Plan1, "the Text and code panel again");
			await OpenAsync(h, page, Tiff, "the Images panel again");
			var back = await hook.WaitStateAsync(s => s.Panel(TextCode).Shown && s.Panel(Images).Shown, "both group panels open again");
			Assert.Equal("bottom", back.Panel(TextCode).Side);
			Assert.True(back.Panel(Images).Floating);
			Assert.True(back.Panel(pinned).Shown);
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			page.Dispose();
			h.CloseApp(process);
		}
	}

	// ---- FR.29.17, decision 3: a new item reveals the panel without taking the focus ----

	[Fact]
	public async Task TC_PW_90_a_new_item_slides_an_auto_hidden_panel_out_without_taking_the_foreground_or_the_focus()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		try
		{
			await OpenAsync(h, page, Plan1, "the Text and code panel");
			await hook.WaitStateAsync(s => s.HasPanel(TextCode) && s.Panel(TextCode).Shown, "the panel shown");
			Assert.True((await hook.AutoHideAsync(TextCode, true)).Ok);
			var hidden = await hook.WaitStateAsync(s => s.Panel(TextCode).AutoHide, "the panel auto-hidden");
			Assert.False(hidden.Panel(TextCode).SlidOut);

			// The test window never becomes the active window (the focus guard), so the composer cannot hold the keyboard focus here: the check is that nothing in the app takes the user's
			// foreground or focus. Where the composer's own focus stays is MANUAL (TC.PW.90).
			var probe = new WindowProbe();
			var foreground = probe.Foreground();
			var focused = FocusedDescription();

			await ScriptCtrlClickAsync(h, page, Plan2);
			var slid = await hook.WaitStateAsync(s => s.Panel(TextCode).SlidOut, "the panel slid out by the new item");
			Assert.True(slid.Panel(TextCode).AutoHide);   // still auto-hidden: it slid out, it did not dock
			await Task.Delay(1000);
			Assert.Equal(foreground, probe.Foreground());
			Assert.NotEqual(process.Id, probe.ProcessOf(probe.Foreground()));
			Assert.Equal(focused, FocusedDescription());
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_90_a_new_item_selects_a_panel_that_is_tabbed_behind_another_one()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		try
		{
			await OpenAsync(h, page, Plan1, "the Text and code panel");
			var opened = await hook.WaitStateAsync(s => s.HasPanel(TextCode) && s.Panel(TextCode).Shown, "the panel shown");
			Assert.True(opened.Panel(TextCode).SharesPaneWith(opened.Panel(Focus)), "The panel is not a tab beside Focus");
			Assert.True((await hook.BringForwardAsync(Focus)).Ok);   // Focus in front: the preview tab is behind it
			await hook.WaitStateAsync(s => s.Panel(Focus).Selected && !s.Panel(TextCode).Selected, "Focus selected, the preview tab behind");

			await OpenAsync(h, page, Plan2, "a new item for the panel");
			var front = await hook.WaitStateAsync(s => s.Panel(TextCode).Selected && !s.Panel(Focus).Selected, "the preview tab selected by the new item");
			Assert.True(front.Panel(TextCode).SharesPaneWith(front.Panel(Focus)));
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_90_a_new_item_raises_a_floating_panel_above_the_other_floating_panels_without_changing_the_foreground_window()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		try
		{
			var probe = new WindowProbe();
			var finder = new DockingTabFinder();
			var main = h.MainWindow(process);
			await OpenAsync(h, page, Plan1, "the Text and code panel");
			await hook.WaitStateAsync(s => s.HasPanel(TextCode) && s.Panel(TextCode).Shown, "the panel shown");
			Assert.True((await hook.FloatAsync(TextCode)).Ok);
			await hook.WaitStateAsync(s => s.Panel(TextCode).Floating, "the preview panel floating");
			Assert.True((await hook.FloatAsync(Focus)).Ok);   // Focus floats after it: its frame is above the preview frame
			await hook.WaitStateAsync(s => s.Panel(Focus).Floating, "Focus floating");
			await Task.Delay(1500);   // the focus guard runs on its own thread

			var focusFrame = IntPtr.Zero;
			var previewFrames = new List<IntPtr>();
			await h.WaitUntilAsync("a floating frame for Focus and one for the preview panel", () =>
			{
				focusFrame = finder.FloatingFrames(process.Id, main, "Focus").FirstOrDefault();
				previewFrames = new AppWindowFinder().FindAll(process.Id).Where(w => w != main && w != focusFrame).ToList();
				return Task.FromResult(focusFrame != IntPtr.Zero && previewFrames.Count == 1);
			});
			var previewFrame = previewFrames[0];
			var order = new AppWindowFinder().FindAll(process.Id);   // top-level windows, top of the z-order first
			Assert.True(order.IndexOf(focusFrame) < order.IndexOf(previewFrame), "Precondition: the Focus frame is above the preview frame");
			var foreground = probe.Foreground();

			await OpenAsync(h, page, Plan2, "a new item for the floating panel");
			await h.WaitUntilAsync("the preview frame above the Focus frame", () =>
			{
				var now = new AppWindowFinder().FindAll(process.Id);
				return Task.FromResult(now.Contains(previewFrame) && now.IndexOf(previewFrame) < now.IndexOf(focusFrame));
			});
			Assert.Equal(foreground, probe.Foreground());
			Assert.NotEqual(process.Id, probe.ProcessOf(probe.Foreground()));
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			page.Dispose();
			h.CloseApp(process);
		}
	}

	// ---- FR.29.22, FR.29.26: a floating panel comes back floating ----

	[Fact]
	public async Task TC_PW_96_a_floating_preview_panel_comes_back_floating_after_a_restart()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		try
		{
			await OpenAsync(h, page, Plan1, "the Text and code panel");
			await hook.WaitStateAsync(s => s.HasPanel(TextCode) && s.Panel(TextCode).Shown, "the panel shown");
			Assert.True((await hook.FloatAsync(TextCode)).Ok);
			await hook.WaitStateAsync(s => s.Panel(TextCode).Floating, "the panel floating");
			await h.WaitForSectionAsync("previewDock", d => d?["layouts"]?[TextCode]?["floating"]?.GetValue<bool>() == true, "the floating layout saved");
		}
		finally
		{
			hook.Dispose();
			page.Dispose();
		}

		var next = await RestartAsync(h, process, monitors: 1);
		var hook2 = await WpfDockingHookClient.ConnectAsync(next);
		try
		{
			await h.WaitForLogAsync(next, "the panel to be restored", "Restore: reopened a group panel of group text-code");
			var back = await hook2.WaitStateAsync(s => s.HasPanel(TextCode) && s.Panel(TextCode).Shown, "the panel back");
			Assert.True(back.Panel(TextCode).Floating, "The panel came back docked: " + back.Panel(TextCode).Side);   // its rectangle is not saved under --test-window (FR.29.26)
			h.AssertNoErrors();
		}
		finally
		{
			hook2.Dispose();
			h.CloseApp(next);
		}
	}

	// ---- pinned panels keep their id across a restart ----

	[Fact]
	public async Task TC_PW_93_two_pinned_panels_tabbed_together_are_still_one_tab_stack_after_a_restart_with_the_ids_they_had()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		string textId;
		string imagesId;
		try
		{
			await OpenAsync(h, page, Plan1, "the Text and code panel");
			var main = h.MainWindow(process);
			Assert.True(await h.PressAsync(process, main, PinTextCode));
			await h.WaitForLogAsync(process, "the Text and code panel to be pinned", "Preview panel pinned: group text-code");
			await OpenAsync(h, page, Tiff, "the Images panel");
			Assert.True(await h.PressAsync(process, main, PinImages));
			await h.WaitForLogAsync(process, "the Images panel to be pinned", "Preview panel pinned: group images");
			var both = await hook.WaitStateAsync(s => PinnedId(s, TextCode) != null && PinnedId(s, Images) != null, "both pinned panels in the state");
			textId = PinnedId(both, TextCode)!;
			imagesId = PinnedId(both, Images)!;

			Assert.True((await hook.DockAsync(imagesId, "bottom")).Ok);
			await hook.WaitStateAsync(s => s.Panel(imagesId).Side == "bottom" && !s.Panel(imagesId).SharesPaneWith(s.Panel(textId)), "the Images panel apart at the bottom");
			Assert.True((await hook.TabAsync(imagesId, textId)).Ok);
			await hook.WaitStateAsync(s => s.Panel(imagesId).SharesPaneWith(s.Panel(textId)), "the two pinned panels in one tab stack");
			await h.WaitForSectionAsync("previewDock", d => d?["openPanels"]?.AsArray().Count(p => p!["pinned"]!.GetValue<bool>() && p["panelId"] != null) == 2
				&& d["openPanels"]!.AsArray().Any(p => p!["layout"]?["stackedWith"]?.AsArray().Any(n => n!.GetValue<string>() == textId) == true), "both pinned panels saved with their ids and the stack");
		}
		finally
		{
			hook.Dispose();
			page.Dispose();
		}

		var next = await RestartAsync(h, process, monitors: 1);
		var hook2 = await WpfDockingHookClient.ConnectAsync(next);
		try
		{
			await h.WaitForLogAsync(next, "both pinned panels to be restored", "Preview windows restored: 2 open");
			var back = await hook2.WaitStateAsync(s => s.HasPanel(textId) && s.HasPanel(imagesId) && s.Panel(textId).Shown && s.Panel(imagesId).Shown, "both pinned panels back with their ids");
			Assert.True(back.Panel(imagesId).SharesPaneWith(back.Panel(textId)), "The two pinned panels are not one tab stack any more");
			h.AssertNoErrors();
		}
		finally
		{
			hook2.Dispose();
			h.CloseApp(next);
		}
	}
}
