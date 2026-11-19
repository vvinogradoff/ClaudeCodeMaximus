using System.Diagnostics;
using System.Text.Json.Nodes;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the docked shell (TC.DS.01, 10, 12, 13, 17, 19, 20, 25; FR.6.11–FR.6.22): the BUILT WPF app against a real loopback daemon, one scripted monitor. The layout state is read from
/// the <c>Shell layout: ...</c> log line the shell writes after every real change; menus and buttons are driven through UI Automation (the hamburger menu's Panels group, the chevron, the Focus
/// button). The saved layout lives in the test data root, so a restart restores it. Mouse drags, floating by drag, the look and the keyboard shortcuts in a real focus stay MANUAL.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfDockedShellE2ETests
{
	private const string StatePrefix = "Shell layout: sessions=";
	private const string DefaultState = "sessions=left,tab0,open,selected recent=left,tab1,open tags=left,tab2,open session=center focus=right,tab0,open,selected";
	private const string ResetTitle = "Reset layout…";
	private const string DefaultLayoutXml =
		"<LayoutRoot xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><RootPanel Orientation=\"Horizontal\">" +
		"<LayoutAnchorablePane DockWidth=\"320\"><LayoutAnchorable Title=\"Sessions\" ContentId=\"sessions\" IsSelected=\"true\" CanClose=\"true\" /><LayoutAnchorable Title=\"Recent\" ContentId=\"recent\" CanClose=\"true\" />" +
		"<LayoutAnchorable Title=\"Tags\" ContentId=\"tags\" CanClose=\"true\" /></LayoutAnchorablePane>" +
		"<LayoutDocumentPane><LayoutDocument Title=\"Session\" ContentId=\"session\" IsSelected=\"true\" IsLastFocusedDocument=\"true\" CanClose=\"false\" CanFloat=\"false\" /></LayoutDocumentPane>" +
		"</RootPanel><TopSide /><RightSide /><LeftSide /><BottomSide /><FloatingWindows />@HIDDEN@</LayoutRoot>";

	private readonly UiAutomationClicker _clicker = new();

	private static string[] Dialogs(params string[] answers) => answers.SelectMany(a => new[] { "--test-dialog", a }).ToArray();

	/// <summary>The saved layout of a user who closed the Focus panel: the default one without Focus and with it in the hidden list.</summary>
	private static string LayoutWithFocusClosed() => DefaultLayoutXml.Replace("@HIDDEN@", "<Hidden><LayoutAnchorable Title=\"Focus\" ContentId=\"focus\" CanClose=\"true\" /></Hidden>");

	/// <summary>Writes a <c>shellLayout</c> section into the harness's wpf.json (the file the app reads at start).</summary>
	private static void SeedShellLayout(PreviewWindowsHarness h, string layoutXml)
	{
		var root = JsonNode.Parse(File.ReadAllText(h.StateFile))!.AsObject();
		var sections = root["sections"]?.AsObject() ?? new JsonObject();
		sections["shellLayout"] = new JsonObject { ["version"] = 1, ["data"] = new JsonObject { ["version"] = 1, ["layout"] = layoutXml } };
		root["sections"] = sections;
		File.WriteAllText(h.StateFile, root.ToJsonString());
	}

	private static async Task<Process> StartAsync(PreviewWindowsHarness h, params string[] extraArguments)
	{
		var arguments = new[] { "--test-monitors", "1" }.Concat(extraArguments).ToArray();
		var process = h.App.Start(new Dictionary<string, string>(), arguments);
		Assert.True(await h.App.WaitForLogAsync(process, PreviewWindowsHarness.StartTimeout, $"Session {h.Main} opened"), "The session did not open. Log:\n" + h.App.ReadLog());
		return process;
	}

	/// <summary>Every layout state the shell logged so far (one per real change, across restarts: the log file is shared).</summary>
	private static List<string> States(PreviewWindowsHarness h) => h.App.ReadLog().ReplaceLineEndings("\n").Split('\n')
		.Where(l => l.Contains(StatePrefix, StringComparison.Ordinal))
		.Select(l => l[(l.IndexOf(StatePrefix, StringComparison.Ordinal) + "Shell layout: ".Length)..].Trim())
		.ToList();

	/// <summary>Waits until the shell has logged more than <paramref name="before"/> states and the newest satisfies the predicate; returns it.</summary>
	private static async Task<string> WaitForStateAsync(PreviewWindowsHarness h, int before, Func<string, bool> predicate, string what)
	{
		var deadline = DateTime.UtcNow + PreviewWindowsHarness.StepTimeout;
		while (DateTime.UtcNow < deadline)
		{
			var states = States(h);
			if (states.Count > before && predicate(states[^1]))
				return states[^1];

			await Task.Delay(250);
		}

		Assert.Fail($"Expected {what}. States: [{string.Join(" | ", States(h))}]. Log:\n{h.App.ReadLog()}");
		return string.Empty;
	}

	[Fact]
	public async Task TC_DS_01_first_start_shows_Sessions_Recent_Tags_left_the_Session_view_in_the_centre_and_Focus_right_and_TC_DS_25_the_state_report_covers_every_panel()
	{
		using var h = new PreviewWindowsHarness();
		var process = await StartAsync(h);
		try
		{
			var state = await WaitForStateAsync(h, 0, _ => true, "the first layout state");
			Assert.Equal(DefaultState, state);
			Assert.Contains("Shell layout: default layout used", h.App.ReadLog());
			Assert.True(await _clicker.WaitForButtonAsync(process, "Focus", PreviewWindowsHarness.StepTimeout), "The Focus button was not found. Log:\n" + h.App.ReadLog());
			Assert.True(_clicker.Exists(process, "Send"), "The composer of the Session view is missing.");
			Assert.True(_clicker.Exists(process, "Toggle session tree"));
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_DS_10_the_chevron_auto_hides_the_pane_with_Sessions_and_a_second_click_restores_it()
	{
		using var h = new PreviewWindowsHarness();
		var process = await StartAsync(h);
		try
		{
			var first = await WaitForStateAsync(h, 0, _ => true, "the first layout state");
			Assert.Equal(DefaultState, first);

			Assert.True(await _clicker.ClickAsync(process, "Toggle session tree", PreviewWindowsHarness.StepTimeout));
			var hidden = await WaitForStateAsync(h, 1, s => s.Contains("sessions=left,tab0,autohide"), "the left pane auto-hidden");
			Assert.Contains("focus=right,tab0,open", hidden);

			Assert.True(await _clicker.ClickAsync(process, "Toggle session tree", PreviewWindowsHarness.StepTimeout));
			var shown = await WaitForStateAsync(h, 2, s => s.StartsWith("sessions=left,tab0,open", StringComparison.Ordinal), "the left pane back");
			Assert.Contains("recent=left,tab1,open", shown);
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_DS_10_an_auto_hidden_left_pane_is_remembered_after_a_restart()
	{
		using var h = new PreviewWindowsHarness();
		var first = await StartAsync(h);
		try
		{
			await WaitForStateAsync(h, 0, _ => true, "the first layout state");
			Assert.True(await _clicker.ClickAsync(first, "Toggle session tree", PreviewWindowsHarness.StepTimeout));
			await WaitForStateAsync(h, 1, s => s.Contains("sessions=left,tab0,autohide"), "the left pane auto-hidden");
		}
		finally
		{
			h.CloseApp(first);
		}

		var before = States(h).Count;
		var second = await StartAsync(h);
		try
		{
			var state = await WaitForStateAsync(h, before, _ => true, "the layout state of the second start");
			Assert.Contains("sessions=left,tab0,autohide", state);
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(second);
		}
	}

	[Fact]
	public async Task TC_DS_12_and_13_a_closed_Focus_panel_is_shown_again_by_the_Focus_button()
	{
		using var h = new PreviewWindowsHarness();
		SeedShellLayout(h, LayoutWithFocusClosed());
		var process = await StartAsync(h);
		try
		{
			var closed = await WaitForStateAsync(h, 0, _ => true, "the restored layout with Focus closed");
			Assert.Contains("focus=closed", closed);
			Assert.Contains("sessions=left,tab0,open", closed);

			Assert.True(await _clicker.ClickAsync(process, "Focus", PreviewWindowsHarness.StepTimeout), "The Focus button was not found. Log:\n" + h.App.ReadLog());
			var opened = await WaitForStateAsync(h, 1, s => s.Contains("focus=right"), "Focus shown at the right again");
			Assert.Contains("focus=right,tab0,open", opened);
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_DS_12_the_Panels_menu_lists_the_panels_and_Reset_layout_and_its_Focus_entry_shows_a_closed_Focus_panel()
	{
		using var h = new PreviewWindowsHarness();
		SeedShellLayout(h, LayoutWithFocusClosed());
		var process = await StartAsync(h);
		try
		{
			await WaitForStateAsync(h, 0, s => s.Contains("focus=closed"), "the restored layout with Focus closed");
			Assert.True(await _clicker.ClickAsync(process, "Main menu", PreviewWindowsHarness.StepTimeout));
			foreach (var entry in new[] { "Sessions", "Recent", "Tags", "Focus", ResetTitle })
				Assert.True(await _clicker.WaitForButtonAsync(process, entry, PreviewWindowsHarness.StepTimeout), $"The Panels entry '{entry}' was not found. Log:\n" + h.App.ReadLog());

			Assert.True(await _clicker.ClickMainMenuItemAsync(process, "Focus", PreviewWindowsHarness.StepTimeout));
			await WaitForStateAsync(h, 1, s => s.Contains("focus=right,tab0,open"), "Focus shown through the menu");
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_DS_19_Reset_layout_asks_first_cancelling_changes_nothing_and_confirming_restores_the_default_layout()
	{
		using var h = new PreviewWindowsHarness();
		SeedShellLayout(h, LayoutWithFocusClosed());
		var process = await StartAsync(h, Dialogs("cancel", "accept"));
		try
		{
			await WaitForStateAsync(h, 0, s => s.Contains("focus=closed"), "the restored layout with Focus closed");

			Assert.True(await _clicker.ClickMainMenuItemAsync(process, ResetTitle, PreviewWindowsHarness.StepTimeout));
			Assert.True(await h.App.WaitForLogAsync(process, PreviewWindowsHarness.StepTimeout, "dialog 'Reset layout'", "answered with cancel"), "The Reset dialog was not shown. Log:\n" + h.App.ReadLog());
			await Task.Delay(1000);
			Assert.Single(States(h));   // cancelling changed nothing

			Assert.True(await _clicker.ClickMainMenuItemAsync(process, ResetTitle, PreviewWindowsHarness.StepTimeout));
			Assert.True(await h.App.WaitForLogAsync(process, PreviewWindowsHarness.StepTimeout, "answered with accept"));
			var state = await WaitForStateAsync(h, 1, _ => true, "the default layout after Reset");
			Assert.Equal(DefaultState, state);
			Assert.True(_clicker.Exists(process, "Send"), "The session view was lost by the reset.");
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}
	}

	[Fact]
	public async Task TC_DS_17_Reset_layout_is_saved_so_a_restart_shows_the_default_layout()
	{
		using var h = new PreviewWindowsHarness();
		SeedShellLayout(h, LayoutWithFocusClosed());
		var first = await StartAsync(h, Dialogs("accept"));
		try
		{
			await WaitForStateAsync(h, 0, s => s.Contains("focus=closed"), "Focus closed at the first start");
			Assert.True(await _clicker.ClickMainMenuItemAsync(first, ResetTitle, PreviewWindowsHarness.StepTimeout));
			await WaitForStateAsync(h, 1, s => s == DefaultState, "the default layout after Reset");
		}
		finally
		{
			h.CloseApp(first);
		}

		var before = States(h).Count;
		var second = await StartAsync(h);
		try
		{
			var restored = await WaitForStateAsync(h, before, _ => true, "the layout of the second start");
			Assert.Equal(DefaultState, restored);
		}
		finally
		{
			h.CloseApp(second);
		}
	}

	[Fact]
	public async Task TC_DS_17_a_layout_with_Focus_closed_and_the_left_pane_auto_hidden_is_restored_at_the_next_start()
	{
		using var h = new PreviewWindowsHarness();
		SeedShellLayout(h, LayoutWithFocusClosed());
		var first = await StartAsync(h);
		try
		{
			await WaitForStateAsync(h, 0, s => s.Contains("focus=closed"), "Focus closed at the first start");
			Assert.True(await _clicker.ClickAsync(first, "Toggle session tree", PreviewWindowsHarness.StepTimeout));
			await WaitForStateAsync(h, 1, s => s.Contains("sessions=left,tab0,autohide") && s.Contains("focus=closed"), "the left pane auto-hidden, Focus still closed");
		}
		finally
		{
			h.CloseApp(first);
		}

		var before = States(h).Count;
		var second = await StartAsync(h);
		try
		{
			var state = await WaitForStateAsync(h, before, _ => true, "the layout of the second start");
			Assert.Contains("focus=closed", state);
			Assert.Contains("sessions=left,tab0,autohide", state);
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(second);
		}
	}

	[Fact]
	public async Task TC_DS_20_the_old_collapsed_tree_setting_becomes_an_auto_hidden_left_pane_at_the_first_start()
	{
		using var h = new PreviewWindowsHarness();
		var root = JsonNode.Parse(File.ReadAllText(h.StateFile))!.AsObject();
		root["treeWidth"] = 400;
		var sections = root["sections"]?.AsObject() ?? new JsonObject();
		sections["layout"] = new JsonObject { ["version"] = 1, ["data"] = new JsonObject { ["treeCollapsed"] = true } };
		root["sections"] = sections;
		File.WriteAllText(h.StateFile, root.ToJsonString());

		var process = await StartAsync(h);
		try
		{
			var state = await WaitForStateAsync(h, 0, _ => true, "the first layout state");
			Assert.Contains("sessions=left,tab0,autohide", state);
			Assert.Contains("focus=right,tab0,open", state);
			Assert.Contains("Shell layout: default layout used (nothing saved)", h.App.ReadLog());
			h.AssertNoErrors();
		}
		finally
		{
			h.CloseApp(process);
		}

		var saved = JsonNode.Parse(File.ReadAllText(h.StateFile))!;
		Assert.True(saved["sections"]?["layout"]?["data"]?["treeCollapsed"]?.GetValue<bool>());   // the old values are kept (FR.6.18)
	}
}
