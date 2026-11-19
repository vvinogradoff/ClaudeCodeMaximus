namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>The layout rows of the docking test hook E2E tests (TC.DS.06, 07, 12, 14, 17, 19, TC.PW.88): refusals, floating panels, the tab row, auto-hide, close and show, reset, restart.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class WpfDockingHookE2ETests
{
	private static string Title(string id) => char.ToUpperInvariant(id[0]) + id[1..];

	[Fact]
	public async Task TC_DS_06_the_Session_view_cannot_be_closed_floated_docked_or_stacked_and_the_state_is_unchanged()
	{
		using var h = new PreviewWindowsHarness();
		var (process, hook) = await StartAsync(h);
		try
		{
			var before = await Ok(hook.StateAsync());
			foreach (var call in new Func<Task<DockingHookReply>>[] { () => hook.CloseAsync(Session), () => hook.FloatAsync(Session), () => hook.DockAsync(Session, "left"), () => hook.TabAsync(Session, Recent) })
			{
				var reply = await call();
				Assert.False(reply.Ok);
				Assert.False(string.IsNullOrEmpty(reply.Error));
			}

			var after = await Ok(hook.StateAsync());
			Assert.Equal(before.Panels.Select(p => (p.Id, p.Side, p.Pane, p.TabIndex, p.Shown)), after.Panels.Select(p => (p.Id, p.Side, p.Pane, p.TabIndex, p.Shown)));
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_DS_07_a_floated_Focus_is_off_screen_owned_without_a_taskbar_button_never_activated_and_the_foreground_stays_with_the_user()
	{
		using var h = new PreviewWindowsHarness();
		var (process, hook) = await StartAsync(h);
		try
		{
			var probe = new WindowProbe();
			var finder = new DockingTabFinder();
			var main = h.MainWindow(process);
			var docked = await Ok(hook.StateAsync());
			var foreground = probe.Foreground();
			await Ok(hook.FloatAsync(Focus, new DockingHookRect(100, 100, 480, 360)));
			var floated = await WaitStateAsync(hook, s => s.Panel(Focus).Floating, "Focus floating");
			Assert.Equal("floating", floated.Panel(Focus).Side);
			Assert.NotNull(floated.Panel(Focus).FloatRect);
			Assert.True(floated.Panel(Session).Width > docked.Panel(Session).Width, "The Session view did not widen when Focus left the docking area");

			List<IntPtr> frames = [];
			await h.WaitUntilAsync("the floating frame named Focus", () =>
			{
				frames = finder.FloatingFrames(process.Id, main, "Focus");
				return Task.FromResult(frames.Count == 1);
			});
			var frame = frames[0];
			await Task.Delay(1500);   // the focus guard runs on its own thread
			Assert.True(probe.IsOffEveryMonitor(frame), "The floating window is on a real monitor: " + probe.Bounds(frame));
			Assert.False(probe.HasTaskbarButton(frame));
			Assert.True(probe.HasOwner(frame));
			Assert.True(probe.IsNoActivate(frame));
			Assert.Equal(foreground, probe.Foreground());
			Assert.NotEqual(process.Id, probe.ProcessOf(probe.Foreground()));
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_DS_07_dock_after_float_returns_Focus_to_the_right_and_a_second_float_works()
	{
		using var h = new PreviewWindowsHarness();
		var (process, hook) = await StartAsync(h);
		try
		{
			await Ok(hook.FloatAsync(Focus));
			await WaitStateAsync(hook, s => s.Panel(Focus).Floating, "Focus floating");
			await Ok(hook.DockAsync(Focus, "right"));
			var docked = await WaitStateAsync(hook, s => !s.Panel(Focus).Floating && s.Panel(Focus).Side == "right", "Focus docked right again");
			Assert.True(docked.Panel(Focus).Width > 100);
			await Ok(hook.FloatAsync(Focus, new DockingHookRect(50, 50, 400, 300)));
			await WaitStateAsync(hook, s => s.Panel(Focus).Floating, "Focus floating again");
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_DS_14_Focus_stacked_into_the_session_area_shows_a_tab_row_and_leaving_hides_it_again()
	{
		using var h = new PreviewWindowsHarness();
		var (process, hook) = await StartAsync(h);
		try
		{
			var finder = new DockingTabFinder();
			var main = h.MainWindow(process);
			await Ok(hook.TabAsync(Focus, Session));
			var tabbed = await WaitStateAsync(hook, s => s.Panel(Focus).SharesPaneWith(s.Panel(Session)), "Focus in the session area");
			Assert.Equal("center", tabbed.Panel(Focus).Side);
			Assert.True(tabbed.Panel(Focus).Selected);
			await h.WaitUntilAsync("the tab row with Session and Focus", () => Task.FromResult(finder.Exists(main, DockingTabFinder.Tab, "Session") && finder.Exists(main, DockingTabFinder.Tab, "Focus")));

			await Ok(hook.DockAsync(Focus, "right"));
			var back = await WaitStateAsync(hook, s => s.Panel(Focus).Side == "right", "Focus back at the right");
			Assert.False(back.Panel(Focus).SharesPaneWith(back.Panel(Session)));
			Assert.Equal(1, back.Panels.Count(p => p.Pane == back.Panel(Session).Pane && p.Shown));   // the Session view is alone in its pane again: the strip is hidden (the strip itself is covered by the view tests)
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task The_panel_tabs_edge_tabs_and_floating_frames_carry_the_panel_title_as_their_UI_Automation_name()
	{
		using var h = new PreviewWindowsHarness();
		var (process, hook) = await StartAsync(h);
		try
		{
			var finder = new DockingTabFinder();
			var main = h.MainWindow(process);
			await h.WaitUntilAsync("the named tabs of the left pane and Focus", () => Task.FromResult(new[] { Sessions, Recent, Tags, Focus }.Select(Title).All(t => finder.Exists(main, DockingTabFinder.Tab, t))));

			await Ok(hook.AutoHideAsync(Sessions, true));
			await WaitStateAsync(hook, s => s.Panel(Sessions).AutoHide, "the left pane auto-hidden");
			// The edge tabs show the panel titles as text (the library gives the tab itself no UI Automation peer); the pane's tab items are gone.
			await h.WaitUntilAsync("the edge tabs with the panel titles", () => Task.FromResult(new[] { Sessions, Recent, Tags }.Select(Title).All(t => !finder.IsShown(main, DockingTabFinder.Tab, t) && finder.ExistsNamed(main, t))));

			await Ok(hook.FloatAsync(Focus));
			await h.WaitUntilAsync("the floating frame named Focus", () => Task.FromResult(finder.FloatingFrames(process.Id, main, "Focus").Count == 1));
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_88_auto_hide_turns_a_pane_into_edge_tabs_the_slide_out_opens_and_toggling_restores_the_docked_place()
	{
		using var h = new PreviewWindowsHarness();
		var (process, hook) = await StartAsync(h);
		try
		{
			var docked = await Ok(hook.StateAsync());
			await Ok(hook.AutoHideAsync(Sessions, true));
			var hidden = await WaitStateAsync(hook, s => s.Panel(Sessions).AutoHide, "the pane auto-hidden");
			Assert.Equal("left", hidden.Panel(Sessions).Edge);
			Assert.True(hidden.Panel(Recent).AutoHide && hidden.Panel(Tags).AutoHide, "The whole pane is auto-hidden");
			Assert.True(hidden.Panel(Session).Width > docked.Panel(Session).Width, "The Session view did not take the freed width");

			await Ok(hook.SlideOutAsync(Recent));
			var refused = await hook.SlideOutAsync(Focus);
			Assert.False(refused.Ok);   // Focus is docked, not auto-hidden

			await Ok(hook.AutoHideAsync(Sessions, false));
			var restored = await WaitStateAsync(hook, s => !s.Panel(Sessions).AutoHide, "the pane docked again");
			Assert.Equal("left", restored.Panel(Sessions).Side);
			Assert.True(restored.Panel(Sessions).SharesPaneWith(restored.Panel(Recent)));
			Assert.Equal(0, restored.Panel(Sessions).TabIndex);
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_DS_12_close_and_show_bring_a_panel_back_to_its_last_place_and_bring_forward_selects_a_tab()
	{
		using var h = new PreviewWindowsHarness();
		var (process, hook) = await StartAsync(h);
		try
		{
			await Ok(hook.CloseAsync(Tags));
			var closed = await WaitStateAsync(hook, s => !s.Panel(Tags).Shown, "Tags closed");
			Assert.Equal("closed", closed.Panel(Tags).Side);
			Assert.True(closed.Panel(Sessions).Shown && closed.Panel(Recent).Shown);
			Assert.False((await hook.CloseAsync(Tags)).Ok);   // already closed

			await Ok(hook.ShowAsync(Tags));
			var shown = await WaitStateAsync(hook, s => s.Panel(Tags).Shown, "Tags shown again");
			Assert.Equal("left", shown.Panel(Tags).Side);
			Assert.True(shown.Panel(Tags).SharesPaneWith(shown.Panel(Sessions)));
			Assert.Equal(2, shown.Panel(Tags).TabIndex);
			Assert.True(shown.Panel(Tags).Selected);

			await Ok(hook.ShowAsync(Recent));   // open already: brought forward
			var forward = await WaitStateAsync(hook, s => s.Panel(Recent).Selected, "Recent selected");
			Assert.False(forward.Panel(Tags).Selected);
			await Ok(hook.BringForwardAsync(Sessions));
			await WaitStateAsync(hook, s => s.Panel(Sessions).Selected, "Sessions selected");

			await Ok(hook.CloseAsync(Focus));
			var focusClosed = await WaitStateAsync(hook, s => !s.Panel(Focus).Shown, "Focus closed");
			Assert.Equal("closed", focusClosed.Panel(Focus).Side);
			await Ok(hook.ShowAsync(Focus));
			var focusShown = await WaitStateAsync(hook, s => s.Panel(Focus).Shown, "Focus shown again");
			Assert.Equal("right", focusShown.Panel(Focus).Side);
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_DS_19_reset_after_floating_auto_hiding_and_closing_restores_the_default_layout()
	{
		using var h = new PreviewWindowsHarness();
		var (process, hook) = await StartAsync(h);
		try
		{
			var first = await Ok(hook.StateAsync());
			await Ok(hook.FloatAsync(Focus));
			await Ok(hook.AutoHideAsync(Sessions, true));
			await Ok(hook.CloseAsync(Tags));
			await WaitStateAsync(hook, s => s.Panel(Focus).Floating && s.Panel(Sessions).AutoHide && !s.Panel(Tags).Shown, "the changed layout");

			await Ok(hook.ResetAsync());
			var reset = await WaitStateAsync(hook, s => !s.Panel(Focus).Floating && !s.Panel(Sessions).AutoHide && s.Panel(Tags).Shown, "the default layout");
			Assert.Equal(first.Panels.Select(p => (p.Id, p.Side, p.TabIndex, p.Shown)), reset.Panels.Select(p => (p.Id, p.Side, p.TabIndex, p.Shown)));
			Assert.True(reset.Panel(Sessions).SharesPaneWith(reset.Panel(Tags)));
			Assert.True(reset.Panel(Sessions).Selected);
			Assert.Empty(new DockingTabFinder().FloatingFrames(process.Id, h.MainWindow(process), "Focus"));
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_DS_17_a_floated_Focus_a_closed_panel_and_a_moved_panel_are_remembered_after_a_restart()
	{
		using var h = new PreviewWindowsHarness();
		var (process, hook) = await StartAsync(h);
		try
		{
			await Ok(hook.FloatAsync(Focus, new DockingHookRect(120, 80, 520, 380)));
			await Ok(hook.CloseAsync(Tags));
			await Ok(hook.DockAsync(Recent, "bottom"));
			await WaitStateAsync(hook, s => s.Panel(Focus).Floating && !s.Panel(Tags).Shown && s.Panel(Recent).Side == "bottom", "the changed layout");
			await Task.Delay(1500);   // the layout is saved 500 ms after the last change
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}

		(process, hook) = await StartAsync(h);
		try
		{
			var state = await Ok(hook.StateAsync());
			Assert.True(state.Panel(Focus).Floating, "Focus did not come back floating");
			Assert.False(state.Panel(Tags).Shown);
			Assert.Equal("bottom", state.Panel(Recent).Side);
			Assert.Equal("left", state.Panel(Sessions).Side);
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}
	}
}
