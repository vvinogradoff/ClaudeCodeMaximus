using System.Windows.Automation;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the Architect's feedback on preview windows: TC.PW.69 (a typed address, FR.27.10) and TC.PW.70 (file controls in the title bar, FR.29.14). The BUILT WPF app with two
/// scripted monitors against a real loopback daemon; the windows are found by title and driven through UI Automation.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfPreviewWindowsPolishE2ETests
{
	private const string Plan1 = "plan1.md - Text and code";
	private const string SourceName = "Show markdown source";
	private const string OpenName = "Open in default app";
	private const string AddressName = "Page address";

	[Fact]
	public async Task TC_PW_70_a_md_window_has_no_header_row_and_Source_and_Open_in_default_app_in_its_title_bar()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 2);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
			await h.WaitForTitlesAsync(process, Plan1);
			var window = h.PreviewWindowTitled(process, Plan1);

			// Both controls are in the window and nowhere else: the pane's own header (with the file name) is gone.
			Assert.True(await h.PressAsync(process, window, OpenName), "Open in default app is in the title bar");
			await h.WaitForLogAsync(process, "the test launcher logs the open", "External open requested (test window, nothing started)");
			Assert.Single(AllNamed(window, OpenName));
			Assert.Single(AllNamed(window, SourceName));
			Assert.Null(PreviewWindowsHarness.FindElement(window, "plan1.md"));   // no header row naming the file

			Assert.Equal(ToggleState.Off, PreviewWindowsHarness.ToggleStateOf(window, SourceName));
			Assert.True(await h.PressAsync(process, window, SourceName));
			await h.WaitUntilAsync("Source is on", () => Task.FromResult(PreviewWindowsHarness.ToggleStateOf(window, SourceName) == ToggleState.On));
			Assert.Equal(Plan1, PreviewWindowsHarness.TitleOf(window));
			Assert.NotNull(PreviewWindowsHarness.FindElement(window, OpenName));
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_70_a_cpp_window_shows_neither_control_because_neither_applies()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 2);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 2);
			await h.WaitForTitlesAsync(process, "main.cpp - Text and code");
			var window = h.PreviewWindowTitled(process, "main.cpp - Text and code");

			await h.WaitForLogAsync(process, "the file page rendered", "Preview opened: main.cpp");   // .cpp is not on the FR.23.18 list and is not markdown
			Assert.Null(PreviewWindowsHarness.FindElement(window, OpenName));
			Assert.Null(PreviewWindowsHarness.FindElement(window, SourceName));
			Assert.Null(PreviewWindowsHarness.FindElement(window, "main.cpp"));
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	/// <summary>
	/// The address box is editable through UI Automation and keeps what was typed. Enter itself cannot be delivered end to end: a test window never takes the keyboard focus (FR.29.13), so the
	/// Enter, Esc and focus-loss behaviour is covered by the view model tests and the manual check of TC.PW.69.
	/// </summary>
	[Fact]
	public async Task TC_PW_69_the_address_box_of_a_web_window_is_editable_and_keeps_the_typed_text()
	{
		using var h = new PreviewWindowsHarness(splitGroups: true);   // the page link matches the Local group
		var (process, page) = await h.StartWithPageAsync(monitors: 2);
		try
		{
			await PreviewWindowsHarness.CtrlClickAsync(page, ".b a", 0);
			await h.WaitForTitlesAsync(process, "Page A - Local");
			var window = h.PreviewWindowTitled(process, "Page A - Local");

			await h.WaitUntilAsync("the address box shows the page", () => Task.FromResult(ReadAddress(window) == h.WebUrlA));
			Assert.True(await SetAddressAsync(process, window, "ftp://x"), PreviewWindowsHarness.DumpEdits(process));
			await Task.Delay(1500);
			Assert.Equal("ftp://x", ReadAddress(window));   // typing is not overwritten, nothing was opened
			Assert.Equal("Page A - Local", PreviewWindowsHarness.TitleOf(window));
			h.AssertNoErrors();
		}
		finally
		{
			page.Dispose();
			h.CloseApp(process);
		}
	}

	private static List<AutomationElement> AllNamed(IntPtr window, string name) =>
		AutomationElement.FromHandle(window).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name)).Cast<AutomationElement>().ToList();

	private static string? ReadAddress(IntPtr window)
	{
		try
		{
			var edit = PreviewWindowsHarness.FindElement(window, AddressName);
			return edit != null && edit.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) ? ((ValuePattern)pattern).Current.Value : null;
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
		{
			return null;
		}
	}

	/// <summary>Sets the text of the window's address box through the Value pattern; false when the box is read-only or missing.</summary>
	private static async Task<bool> SetAddressAsync(System.Diagnostics.Process process, IntPtr window, string text)
	{
		var deadline = DateTime.UtcNow + PreviewWindowsHarness.StepTimeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			try
			{
				var edit = PreviewWindowsHarness.FindElement(window, AddressName);
				if (edit != null && edit.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && !((ValuePattern)pattern).Current.IsReadOnly)
				{
					((ValuePattern)pattern).SetValue(text);
					return true;
				}
			}
			catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException or InvalidOperationException)
			{
				// the window is changing: look again
			}

			await Task.Delay(200);
		}

		return false;
	}
}
