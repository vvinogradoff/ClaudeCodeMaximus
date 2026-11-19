using System.Diagnostics;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the programmatic docking test hook (FR.6.22, FR.29.26; TC.DS.03, 06, 07, 12, 14, 19, TC.PW.86 to TC.PW.88, TC.PW.102): the BUILT WPF app with ONE scripted monitor, driven by calls
/// on the named pipe <c>tessyn-wpf-test-&lt;pid&gt;</c> (<see cref="WpfDockingHookClient"/>) instead of mouse drags. The floating panel windows are checked with Win32 (off every monitor, no taskbar button,
/// the user's foreground window unchanged) and the tabs through their UI Automation names. Mouse drags and real key presses stay MANUAL.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed partial class WpfDockingHookE2ETests
{
	private const string Sessions = "sessions";
	private const string Recent = "recent";
	private const string Tags = "tags";
	private const string Focus = "focus";
	private const string Session = "session";

	private static async Task<(Process Process, WpfDockingHookClient Hook)> StartAsync(PreviewWindowsHarness h, params string[] extraArguments)
	{
		var arguments = new[] { "--test-monitors", "1" }.Concat(extraArguments).ToArray();
		var process = h.App.Start(new Dictionary<string, string>(), arguments);
		Assert.True(await h.App.WaitForLogAsync(process, PreviewWindowsHarness.StartTimeout, $"Session {h.Main} opened"), "The session did not open. Log:\n" + h.App.ReadLog());
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		return (process, hook);
	}

	private static async Task<DockingHookReply> Ok(Task<DockingHookReply> call)
	{
		var reply = await call;
		Assert.True(reply.Ok, "The hook refused the call: " + reply.Error);
		return reply;
	}

	/// <summary>Polls the state until the predicate holds (the library lays out a moment after a call); returns it.</summary>
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

		Assert.Fail($"Expected {what}. Last state: " + string.Join(" | ", last?.Panels.Select(p => $"{p.Id}={p.Side},pane{p.Pane},tab{p.TabIndex}{(p.AutoHide ? ",autohide" : string.Empty)}{(p.Shown ? string.Empty : ",closed")}") ?? []));
		return last!;
	}

	[Fact]
	public async Task The_hook_pipe_exists_only_for_the_test_window_and_reports_the_shell_panels()
	{
		using var h = new PreviewWindowsHarness();
		var (process, hook) = await StartAsync(h);
		try
		{
			Assert.True(WpfDockingHookClient.PipeExists(process.Id));
			var state = await Ok(hook.StateAsync());
			Assert.Equal(new[] { Focus, Recent, Session, Sessions, Tags }, state.Panels.Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal));
			Assert.Equal(("left", 0, true), (state.Panel(Sessions).Side, state.Panel(Sessions).TabIndex, state.Panel(Sessions).Selected));
			Assert.Equal(1, state.Panel(Recent).TabIndex);
			Assert.Equal(2, state.Panel(Tags).TabIndex);
			Assert.True(state.Panel(Sessions).SharesPaneWith(state.Panel(Tags)));
			Assert.Equal("right", state.Panel(Focus).Side);
			Assert.False(state.Panel(Focus).SharesPaneWith(state.Panel(Sessions)));
			Assert.Equal("center", state.Panel(Session).Side);
			Assert.True(state.Panel(Sessions).Width > 100 && state.Panel(Focus).Width > 100 && state.Panel(Session).Width > 100, "The panes report no size");
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}

		Assert.False(WpfDockingHookClient.PipeExists(process.Id), "The pipe outlived the app.");
	}

	[Fact]
	public async Task The_hook_answers_a_bad_request_with_an_error_and_keeps_working()
	{
		using var h = new PreviewWindowsHarness();
		var (process, hook) = await StartAsync(h);
		try
		{
			var garbage = await hook.SendRawAsync("this is not json");
			Assert.False(garbage.Ok);
			Assert.NotNull(garbage.Error);
			var refused = await hook.DockAsync("nothing", "left");
			Assert.False(refused.Ok);
			Assert.Contains("nothing", refused.Error);
			var badSide = await hook.DockAsync(Recent, "diagonal");
			Assert.False(badSide.Ok);
			Assert.True((await hook.StateAsync()).Ok);
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_DS_03_dock_Recent_to_the_bottom_and_stack_Tags_on_it_the_state_report_follows()
	{
		using var h = new PreviewWindowsHarness();
		var (process, hook) = await StartAsync(h);
		try
		{
			await Ok(hook.DockAsync(Recent, "bottom"));
			var docked = await WaitStateAsync(hook, s => s.Panel(Recent).Side == "bottom", "Recent docked at the bottom");
			Assert.False(docked.Panel(Recent).SharesPaneWith(docked.Panel(Sessions)));
			Assert.True(docked.Panel(Sessions).SharesPaneWith(docked.Panel(Tags)));
			await Ok(hook.TabAsync(Tags, Recent));
			var stacked = await WaitStateAsync(hook, s => s.Panel(Tags).SharesPaneWith(s.Panel(Recent)), "Tags stacked on Recent");
			Assert.Equal("bottom", stacked.Panel(Tags).Side);
			Assert.Equal(1, stacked.Panel(Tags).TabIndex);
			Assert.Equal(0, stacked.Panel(Recent).TabIndex);
			Assert.True(stacked.Panel(Tags).Selected);
			Assert.Equal("left", stacked.Panel(Sessions).Side);
			h.AssertNoErrors();
		}
		finally
		{
			hook.Dispose();
			h.CloseApp(process);
		}
	}
}
