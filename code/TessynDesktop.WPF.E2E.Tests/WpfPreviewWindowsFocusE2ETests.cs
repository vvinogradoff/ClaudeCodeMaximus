using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for preview windows opened from Focus references and for their restart (TC.PW.16, 37, 41, 55, 61, 63): the BUILT WPF app with two scripted monitors against a
/// real loopback daemon whose attention service holds Focus items with a file reference and a web reference (<c>post_attention</c> through the real MCP server).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfPreviewWindowsFocusE2ETests
{
	private const string Plan1 = "plan1.md - Text and code";
	private const string Plan2 = "plan2.md - Text and code";

	private static JsonObject UrlRef(string url, string label) => new() { ["kind"] = "url", ["url"] = url, ["label"] = label };

	private static async Task PostAsync(PreviewWindowsHarness h, JsonObject item)
	{
		var result = await h.Env.PostAsync(item);
		Assert.DoesNotContain("error", result.ToJsonString(), StringComparison.OrdinalIgnoreCase);
	}

	private static async Task ShowFocusAsync(PreviewWindowsHarness h, System.Diagnostics.Process process, int openItems)
	{
		await h.WaitForLogAsync(process, "the Focus items to load", $"Focus: {openItems} open item(s) for {h.Main}");
		Assert.True(await new UiAutomationClicker().ClickAsync(process, "Focus", PreviewWindowsHarness.StepTimeout), "The Focus view switch was not found. Log:\n" + h.App.ReadLog());
	}

	[Fact]
	public async Task TC_PW_16_a_Focus_file_reference_and_a_Focus_url_reference_go_to_their_group_windows()
	{
		using var h = new PreviewWindowsHarness(splitGroups: true);
		await PostAsync(h, FocusRefsEnv.ItemWithRefs("Which one?", FocusRefsEnv.FileRef(h.Md1, "the plan"), UrlRef(h.WebUrlA, "the page")));
		var process = await h.StartAsync(monitors: 2);
		try
		{
			await ShowFocusAsync(h, process, 1);
			var main = h.MainWindow(process);

			Assert.True(await h.PressAsync(process, main, "the plan"), "The file reference was not found. Log:\n" + h.App.ReadLog());
			await h.WaitForTitlesAsync(process, Plan1);

			Assert.True(await h.PressAsync(process, main, "the page"), "The url reference was not found. Log:\n" + h.App.ReadLog());
			await h.WaitForTitlesAsync(process, Plan1, "Page A - Local");
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task TC_PW_37_a_window_opened_from_a_Focus_reference_closes_when_the_item_is_resolved_through_the_daemon_only_with_Auto_close(bool autoClose)
	{
		using var h = new PreviewWindowsHarness(splitGroups: true, autoClose: autoClose);
		await PostAsync(h, FocusRefsEnv.ItemWithRefs("Approve the plan?", FocusRefsEnv.FileRef(h.Md1, "the plan")));
		var process = await h.StartAsync(monitors: 2);
		try
		{
			await ShowFocusAsync(h, process, 1);
			Assert.True(await h.PressAsync(process, h.MainWindow(process), "the plan"), "The file reference was not found. Log:\n" + h.App.ReadLog());
			await h.WaitForTitlesAsync(process, Plan1);

			var item = h.Env.Attention.GetItems(h.Main).Single();
			h.Env.Attention.ResolveBySession(h.Env.Session, item.Id, AttentionStatus.Resolved, "done");
			h.PublishAttentionChanged();
			if (autoClose)
			{
				await h.WaitForTitlesAsync(process);
				await h.WaitForLogAsync(process, "the window to close with its item", "auto-close: its Focus item closed");
			}
			else
			{
				await h.WaitForLogAsync(process, "the client to see the item closed", $"Focus: 0 open item(s) for {h.Main}");
				await Task.Delay(1500);
				await h.WaitForTitlesAsync(process, Plan1);
			}

			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_PW_41_55_61_63_open_windows_come_back_after_a_restart_a_window_closed_by_hand_does_not()
	{
		using var h = new PreviewWindowsHarness();
		var (process, page) = await h.StartWithPageAsync(monitors: 2);
		try
		{
			// A pinned window (plan1) and a group window (plan2).
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 0);
			await h.WaitForTitlesAsync(process, Plan1);
			Assert.True(await h.PressAsync(process, h.PreviewWindowTitled(process, Plan1), "Pin preview"));
			await h.WaitForLogAsync(process, "the pin", "Preview window of group text-code pinned");
			await PreviewWindowsHarness.CtrlClickAsync(page, ".pth", 1);
			await h.WaitForTitlesAsync(process, Plan1, Plan2);

			// TC.PW.41: the list is saved as windows open and are pinned.
			var data = await h.WaitForSectionAsync("previewWindows", d => d?["openWindows"]?.AsArray().Count == 2, "both windows in the saved list");
			Assert.Equal(1, data!["openWindows"]!.AsArray().Count(w => w!["pinned"]!.GetValue<bool>()));
		}
		finally
		{
			page.Dispose();
		}

		h.CloseApp(process);
		Assert.True(process.HasExited);

		// TC.PW.63: under --test-window the list survives, the placements are not saved.
		var afterClose = h.Section("previewWindows")!;
		Assert.Equal(2, afterClose["openWindows"]!.AsArray().Count);
		Assert.True(afterClose["placements"] is not JsonObject { Count: > 0 }, "A test window saved a placement: " + afterClose["placements"]);

		// TC.PW.55: both reopen, with their items.
		process = await h.StartAsync(monitors: 2);
		try
		{
			await h.WaitForLogAsync(process, "both windows to be restored", "Preview windows restored: 2 open");
			await h.WaitForTitlesAsync(process, Plan1, Plan2);
			Assert.Contains("Restore: reopened a pinned window of group text-code", h.App.ReadLog());
			Assert.Contains("Restore: reopened a group window of group text-code", h.App.ReadLog());

			// TC.PW.61: close the group window by hand, quit, start again: only the pinned window comes back.
			Assert.True(await h.PressAsync(process, h.PreviewWindowTitled(process, Plan2), "Close preview"));
			await h.WaitForTitlesAsync(process, Plan1);
			await h.WaitForSectionAsync("previewWindows", d => d?["openWindows"]?.AsArray().Count == 1, "the closed window removed from the saved list");
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}

		process = await h.StartAsync(monitors: 2);
		try
		{
			await h.WaitForLogAsync(process, "one window to be restored", "Preview windows restored: 1 open");
			await h.WaitForTitlesAsync(process, Plan1);
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}
}
