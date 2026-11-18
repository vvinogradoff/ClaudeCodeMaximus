using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for routing transcript paths and links to preview windows (TC.PW.05, 15, 22, 25, 33, 36, 42, 44, and the transcript link of 16): the BUILT WPF app with two scripted
/// monitors against a real loopback daemon; the paths are Ctrl+clicked on the transcript page (DevTools), the windows are found by title (<c>item - group</c>) and pressed through UI Automation.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfPreviewWindowsRoutingE2ETests
{
	private const string Plan1 = "plan1.md - Text and code";
	private const string Plan2 = "plan2.md - Text and code";

	[Fact]
	public async Task TC_PW_15_22_44_a_ctrl_click_opens_the_Text_and_code_window_a_second_md_replaces_it_and_the_window_is_off_screen_and_never_active()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 2);
		try
		{
			// A plain click opens nothing.
			await PreviewWindowsHarness.ClickAsync(page, ".pth", 0);
			await Task.Delay(1500);
			Assert.Empty(h.PreviewWindows(process));

			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
			await h.WaitForTitlesAsync(process, Plan1);
			await h.WaitForLogAsync(process, "the window to open", "opened in the window of group text-code");

			// TC.PW.44: off-screen, not in the taskbar, never the foreground window.
			var window = h.PreviewWindowTitled(process, Plan1);
			GetWindowRect(window, out var rect);
			Assert.True(rect.Left <= -30000 && rect.Top <= -30000, $"The preview window is not off-screen: {rect.Left},{rect.Top}");
			var exStyle = GetWindowLong(window, -20);
			var owner = GetWindow(window, 4);   // GW_OWNER
			Assert.True((exStyle & 0x80) != 0 || owner != IntPtr.Zero, $"The preview window has a taskbar button: extended style {exStyle:X}, owner {owner}");   // WS_EX_TOOLWINDOW or owned: no taskbar button
			Assert.Equal(0, exStyle & 0x40000);   // no WS_EX_APPWINDOW
			Assert.NotEqual(window, GetForegroundWindow());

			// TC.PW.22: the second markdown file replaces the content of the same window.
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 1);
			await h.WaitForTitlesAsync(process, Plan2);
			Assert.Single(h.PreviewWindows(process));
			Assert.NotEqual(h.PreviewWindows(process).Single().Handle, GetForegroundWindow());
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_25_md_and_cpp_in_two_groups_give_two_windows_and_a_new_md_changes_only_the_md_window()
	{
		using var h = new PreviewWindowsHarness(splitGroups: true);
		var (process, page) = await h.StartWithPageAsync(monitors: 2);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
			await h.WaitForTitlesAsync(process, Plan1);
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 2);
			await h.WaitForTitlesAsync(process, Plan1, "main.cpp - C++");

			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 1);
			await h.WaitForTitlesAsync(process, Plan2, "main.cpp - C++");
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_33_05_Pin_keeps_the_window_the_next_md_opens_a_new_one_and_turning_the_mode_off_closes_only_the_group_window()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 2);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
			await h.WaitForTitlesAsync(process, Plan1);
			var first = h.PreviewWindowTitled(process, Plan1);
			Assert.True(await h.PressAsync(process, first, "Pin preview"));
			await h.WaitForLogAsync(process, "the window to be pinned", "Preview window of group text-code pinned");
			await h.WaitUntilAsync("Auto-close to disappear from the pinned window", () => Task.FromResult(PreviewWindowsHarness.FindElement(first, "Auto-close preview") == null
				|| AutomationElement.FromHandle(first).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Auto-close preview"))?.Current.IsOffscreen == true));

			// The pinned window keeps its item; the group's next item opens a new window.
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 1);
			await h.WaitForTitlesAsync(process, Plan1, Plan2);
			Assert.Equal(first, h.PreviewWindowTitled(process, Plan1));

			// TC.PW.05: the toggle off closes the group window and keeps the pinned one.
			Assert.True(await h.PressAsync(process, h.MainWindow(process), "Preview windows"));
			await h.WaitForTitlesAsync(process, Plan1);
			await h.WaitForSectionAsync("previewWindows", d => d?["enabled"]?.GetValue<bool>() == false, "the choice saved");
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task TC_PW_36_selecting_another_session_closes_an_auto_close_window_and_keeps_the_others(bool autoClose)
	{
		using var h = new PreviewWindowsHarness(splitGroups: true, autoClose: autoClose);
		var (process, page) = await h.StartWithPageAsync(monitors: 2);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
			await h.WaitForTitlesAsync(process, Plan1);

			Assert.True(await h.SelectSessionAsync(process, "Second session"), "The second session was not found. Log:\n" + h.App.ReadLog());
			await h.WaitForLogAsync(process, "the second session to open", $"Session {h.Second} opened");
			if (autoClose)
			{
				await h.WaitForTitlesAsync(process);
				await h.WaitForLogAsync(process, "the auto-close", "auto-close: another session is shown");
			}
			else
			{
				await Task.Delay(2000);
				await h.WaitForTitlesAsync(process, Plan1);
				Assert.DoesNotContain("auto-close: another session is shown", h.App.ReadLog());
			}

			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_42_a_tiff_path_opens_in_the_Images_window_and_is_shown()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 2);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 3);
			await h.WaitForTitlesAsync(process, "pic.tiff - Images");
			await h.WaitForLogAsync(process, "the TIFF to be opened and rendered", "Preview opened: pic.tiff");
			Assert.DoesNotContain("could not be decoded", h.App.ReadLog());
			Assert.DoesNotContain("Preview unavailable", h.App.ReadLog());
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_16_a_transcript_web_link_goes_to_the_window_of_its_group()
	{
		using var h = new PreviewWindowsHarness(splitGroups: true);   // the page link matches the Local group; the default Web group (https only) would not take http://127.0.0.1
		var (process, page) = await h.StartWithPageAsync(monitors: 2);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".b a", 0);
			await h.WaitForTitlesAsync(process, "Page A - Local");
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
	private static extern int GetWindowLong(IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

	[StructLayout(LayoutKind.Sequential)]
	private struct Rect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}
}
