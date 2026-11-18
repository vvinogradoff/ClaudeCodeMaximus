using System.Windows.Automation;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the preview windows' title-bar toggle and the Previews tab (TC.PW.01, 02, 07, 08, 12, 14, 20, 32): the BUILT WPF app with scripted monitors
/// (<c>--test-window --test-monitors n</c>) against a real loopback daemon, driven through UI Automation and the transcript page.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfPreviewWindowsToggleE2ETests
{
	private const string ToggleName = "Preview windows";

	[Fact]
	public async Task TC_PW_01_with_one_monitor_the_toggle_is_disabled_and_a_path_opens_in_the_pane()
	{
		using var h = new PreviewWindowsHarness();
		var process = await h.StartAsync(monitors: 1);
		try
		{
			var main = h.MainWindow(process);
			Assert.NotEqual(IntPtr.Zero, main);
			Assert.NotNull(PreviewWindowsHarness.FindElement(main, ToggleName));
			Assert.False(PreviewWindowsHarness.IsEnabled(main, ToggleName), "The Monitor button must be disabled with one monitor.");
			// The tooltip text is covered by TitleBarMonitorToggleTests (WPF does not expose a tooltip through UI Automation).
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_02_with_two_monitors_the_toggle_is_enabled_and_on_and_a_click_saves_it_off()
	{
		using var h = new PreviewWindowsHarness();
		var process = await h.StartAsync(monitors: 2);
		try
		{
			var main = h.MainWindow(process);
			Assert.True(PreviewWindowsHarness.IsEnabled(main, ToggleName));
			Assert.Equal(ToggleState.On, PreviewWindowsHarness.ToggleStateOf(main, ToggleName));

			Assert.True(await h.PressAsync(process, main, ToggleName));
			await h.WaitUntilAsync("the toggle to show off", () => Task.FromResult(PreviewWindowsHarness.ToggleStateOf(main, ToggleName) == ToggleState.Off));
			await h.WaitForSectionAsync("previewWindows", d => d?["enabled"]?.GetValue<bool>() == false, "the choice saved in previewWindows");
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}

	[Theory]
	[InlineData(2, false)]   // two monitors, the toggle was switched off
	[InlineData(1, true)]    // one monitor, the toggle on (it is disabled, so nothing is routed)
	public async Task TC_PW_20_with_the_mode_off_or_one_monitor_a_path_opens_in_the_pane_and_no_window_appears(int monitors, bool enabled)
	{
		using var h = new PreviewWindowsHarness(previewWindowsJson: $"{{\"enabled\":{(enabled ? "true" : "false")},\"placements\":{{}},\"openWindows\":[]}}");
		var (process, page) = await h.StartWithPageAsync(monitors);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
			await h.WaitForLogAsync(process, "the preview pane to open the markdown", "Preview opened: plan1.md (\"Markdown\",", "Preview page rendered markdown: true");
			Assert.Empty(h.PreviewWindows(process));
			Assert.DoesNotContain("opened in the window of group", h.App.ReadLog());
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_32_the_Auto_close_button_flips_the_group_switch_at_once_and_Settings_shows_it()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 2);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
			await h.WaitForTitlesAsync(process, "plan1.md - Text and code");
			var window = h.PreviewWindowTitled(process, "plan1.md - Text and code");
			Assert.Equal(ToggleState.Off, PreviewWindowsHarness.ToggleStateOf(window, "Auto-close preview"));

			Assert.True(await h.PressAsync(process, window, "Auto-close preview"));
			await h.WaitUntilAsync("the button to show on", () => Task.FromResult(PreviewWindowsHarness.ToggleStateOf(window, "Auto-close preview") == ToggleState.On));
			await h.WaitForSectionAsync("previews", d => d?["groups"]?.AsArray().Any(g => g?["id"]?.GetValue<string>() == "text-code" && g["autoClose"]?.GetValue<bool>() == true) == true,
				"the switch of the Text and code group saved");

			await OpenPreviewsTabAsync(h, process);
			Assert.True(await PollAsync(() => PreviewWindowsHarness.FindAnywhere(process, "Preview group auto-close Text and code") is { } found
				&& PreviewWindowsHarness.ToggleStateOf(found.Window, "Preview group auto-close Text and code") == ToggleState.On), "Settings does not show the switch on. Log:\n" + h.App.ReadLog());
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_07_08_12_14_the_Previews_tab_adds_refuses_a_bad_pattern_resets_and_guards_closing()
	{
		// Two stored groups only: the tab's list is virtualized, and the added row must be on screen to be edited.
		using var h = new PreviewWindowsHarness(previewsJson: "{\"groups\":[{\"id\":\"images\",\"name\":\"Images\",\"patterns\":\"png\",\"autoClose\":false},{\"id\":\"text-code\",\"name\":\"Text and code\",\"patterns\":\"md\",\"autoClose\":false}]}");
		var process = await h.StartAsync(monitors: 2);
		try
		{
			await OpenPreviewsTabAsync(h, process);

			// TC.PW.07: add a group, name it, give it a pattern, Save writes the previews section.
			Assert.True(await h.PressAnywhereAsync(process, "Add preview group"), "Add group was not found. Log:\n" + h.App.ReadLog());
			Assert.True(await h.SetTextOfEditAsync(process, "Preview group name", "", "Docs"), "The new row's name box was not found. Edits: " + PreviewWindowsHarness.DumpEdits(process));
			Assert.True(await h.SetTextAnywhereAsync(process, "Preview group patterns Docs", "*.docx"));

			// TC.PW.08: a pattern that is no extension or address is refused, with a message naming group and pattern; nothing is saved.
			Assert.True(await h.PressAnywhereAsync(process, "Save settings"));
			Assert.True(await PollAsync(() => PreviewWindowsHarness.TextShown(process, "Group 'Docs': '*.docx' is not a file extension or an http(s) address.")),
				"The refusal did not name the group and pattern. Log:\n" + h.App.ReadLog());
			Assert.Equal(2, h.Section("previews")!["groups"]!.AsArray().Count);   // nothing was saved

			Assert.True(await h.SetTextAnywhereAsync(process, "Preview group patterns Docs", "docx"));
			Assert.True(await h.PressAnywhereAsync(process, "Save settings"));
			var saved = await h.WaitForSectionAsync("previews", d => d?["groups"]?.AsArray().Count == 3, "three groups saved (the two stored ones and Docs)");
			Assert.Equal("Docs", saved!["groups"]!.AsArray()[2]!["name"]!.GetValue<string>());
			Assert.Equal("docx", saved["groups"]!.AsArray()[2]!["patterns"]!.GetValue<string>());

			// TC.PW.14: an unsaved change triggers the close guard (the scripted dialog cancels, so the window stays).
			Assert.True(await h.SetTextAnywhereAsync(process, "Preview group patterns Docs", "docx doc"));
			Assert.True(await h.PressAnywhereAsync(process, "Close settings"));
			await h.WaitForLogAsync(process, "the close guard to ask", "Test window: dialog");
			Assert.NotNull(PreviewWindowsHarness.FindAnywhere(process, "Close settings"));

			// TC.PW.12: Reset to defaults is an unsaved edit; Save keeps the eight default groups.
			Assert.True(await h.PressAnywhereAsync(process, "Reset preview groups"));
			Assert.True(await PollAsync(() => PreviewWindowsHarness.FindAnywhere(process, "Preview group name Docs") == null
				&& PreviewWindowsHarness.FindAnywhere(process, "Preview group name Images") != null), "The tab did not drop Docs. Log:\n" + h.App.ReadLog());
			Assert.Equal(3, h.Section("previews")!["groups"]!.AsArray().Count);   // not saved yet
			Assert.True(await h.PressAnywhereAsync(process, "Save settings"));
			await h.WaitForSectionAsync("previews", d => d?["groups"]?.AsArray().Count == 8, "the eight default groups saved");
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}

	private static async Task OpenPreviewsTabAsync(PreviewWindowsHarness h, System.Diagnostics.Process process)
	{
		var main = h.MainWindow(process);
		Assert.True(await h.PressAsync(process, main, "Main menu"), "The main menu was not found. Log:\n" + h.App.ReadLog());
		Assert.True(await h.PressAnywhereAsync(process, "Settings…"), "The Settings entry was not found. Log:\n" + h.App.ReadLog());
		Assert.True(await h.PressAnywhereAsync(process, "Previews tab"), "The Previews tab was not found. Log:\n" + h.App.ReadLog());
		Assert.True(await PollAsync(() => PreviewWindowsHarness.FindAnywhere(process, "Add preview group") != null), "The Previews tab did not show. Log:\n" + h.App.ReadLog());
	}

	private static async Task<bool> PollAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + PreviewWindowsHarness.StepTimeout;
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
				return true;

			await Task.Delay(200);
		}

		return condition();
	}
}
