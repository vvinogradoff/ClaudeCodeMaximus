namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the docked preview panels through the docking test hook (FR.29.19 to FR.29.21, FR.29.26; TC.DS.26, TC.PW.86 to TC.PW.88, TC.PW.102): a Ctrl+click opens the group's panel, then the hook
/// docks, stacks, floats and auto-hides it by its content id (the group id) like a shell panel, and the group's layout lands in the <c>previewDock</c> section, apart from <c>previewWindows</c>.
/// Restart and mode-change rows (FR.29.22, FR.29.23) are not covered here.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfDockingHookPreviewE2ETests
{
	private const string Group = "text-code";
	private const string Focus = "focus";

	private static async Task<DockingHookReply> WaitStateAsync(WpfDockingHookClient hook, Func<DockingHookReply, bool> predicate, string what)
	{
		var deadline = DateTime.UtcNow + PreviewWindowsHarness.StepTimeout;
		DockingHookReply? last = null;
		while (DateTime.UtcNow < deadline)
		{
			last = await hook.StateAsync();
			if (predicate(last))
				return last;

			await Task.Delay(200);
		}

		Assert.Fail($"Expected {what}. Panels: " + string.Join(" | ", last?.Panels.Select(p => $"{p.Id}={p.Side},pane{p.Pane},tab{p.TabIndex}") ?? []));
		return last!;
	}

	private static async Task OpenPanelAsync(PreviewWindowsHarness h, System.Diagnostics.Process process, CdpPage page)
	{
		await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
		await h.WaitForLogAsync(process, "the panel to open", "Preview panel opened: group text-code");
	}

	[Fact]
	public async Task TC_DS_26_a_preview_panel_opens_as_a_tab_in_the_pane_with_Focus_and_the_hook_moves_it_like_a_shell_panel()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		try
		{
			await OpenPanelAsync(h, process, page);
			var opened = await WaitStateAsync(hook, s => s.HasPanel(Group) && s.Panel(Group).Shown, "the preview panel in the state");
			Assert.Equal("preview", opened.Panel(Group).Kind);
			Assert.True(opened.Panel(Group).SharesPaneWith(opened.Panel(Focus)), "A first-time preview panel is a tab in the pane that holds Focus");
			Assert.Equal("right", opened.Panel(Group).Side);

			Assert.True((await hook.DockAsync(Group, "bottom")).Ok);
			var bottom = await WaitStateAsync(hook, s => s.Panel(Group).Side == "bottom", "the panel docked at the bottom");
			Assert.False(bottom.Panel(Group).SharesPaneWith(bottom.Panel(Focus)));

			Assert.True((await hook.TabAsync(Group, Focus)).Ok);
			await WaitStateAsync(hook, s => s.Panel(Group).SharesPaneWith(s.Panel(Focus)), "the panel stacked with Focus");

			Assert.True((await hook.TabAsync(Group, "session")).Ok);
			var inSession = await WaitStateAsync(hook, s => s.Panel(Group).SharesPaneWith(s.Panel("session")), "the panel stacked into the session area");
			Assert.Equal("center", inSession.Panel(Group).Side);

			Assert.True((await hook.DockAsync(Group, "left")).Ok);
			await WaitStateAsync(hook, s => s.Panel(Group).Side == "left", "the panel docked left");
			Assert.True((await hook.AutoHideAsync(Group, true)).Ok);
			var hidden = await WaitStateAsync(hook, s => s.Panel(Group).AutoHide, "the panel auto-hidden");
			Assert.Equal("left", hidden.Panel(Group).Edge);
			Assert.True((await hook.AutoHideAsync(Group, false)).Ok);
			await WaitStateAsync(hook, s => !s.Panel(Group).AutoHide, "the panel docked again");

			Assert.True((await hook.FloatAsync(Group)).Ok);
			var floating = await WaitStateAsync(hook, s => s.Panel(Group).Floating, "the panel floating");
			Assert.Equal("floating", floating.Panel(Group).Side);
			Assert.Empty(h.PreviewWindows(process));   // a floating panel is not an external preview window
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
	public async Task TC_DS_26_with_Focus_closed_a_preview_panel_docks_right_at_about_40_percent_of_the_area()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		try
		{
			Assert.True((await hook.CloseAsync(Focus)).Ok);
			await WaitStateAsync(hook, s => !s.Panel(Focus).Shown, "Focus closed");
			await OpenPanelAsync(h, process, page);
			var opened = await WaitStateAsync(hook, s => s.HasPanel(Group) && s.Panel(Group).Shown && s.Panel(Group).Width > 100, "the panel docked");
			Assert.Equal("right", opened.Panel(Group).Side);
			var total = opened.Panels.Where(p => p.Shown && p.Side is "left" or "right" or "center" && !p.AutoHide).GroupBy(p => p.Pane).Sum(g => g.First().Width);
			var share = opened.Panel(Group).Width / total;
			Assert.InRange(share, 0.3, 0.5);
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
	public async Task TC_PW_94_a_moved_panel_is_saved_in_previewDock_for_its_group_apart_from_previewWindows()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 1);
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		try
		{
			await OpenPanelAsync(h, process, page);
			await WaitStateAsync(hook, s => s.HasPanel(Group) && s.Panel(Group).Shown, "the preview panel");
			Assert.True((await hook.DockAsync(Group, "bottom")).Ok);
			await WaitStateAsync(hook, s => s.Panel(Group).Side == "bottom", "the panel at the bottom");
			var dock = await h.WaitForSectionAsync("previewDock", n => n != null && n.ToJsonString().Contains(Group, StringComparison.Ordinal) && n["layouts"]?[Group]?["side"]?.GetValue<int>() == 3, "the group's layout in previewDock");
			Assert.NotNull(dock);
			var windows = h.Section("previewWindows");
			Assert.False(windows != null && windows.ToJsonString().Contains("ottom", StringComparison.OrdinalIgnoreCase), "The dock layout leaked into previewWindows: " + windows?.ToJsonString());
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			page.Dispose();
			h.CloseApp(process);
		}
	}
}
