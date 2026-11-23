using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for GPT sessions in the WPF client (FR.33, TC.CX.40, TC.CX.41): the BUILT app against <see cref="CodexE2EDaemon"/>, whose GPT side is
/// the real Codex services and engine over the in-process fake app-server (FR.33.19). The command bar, the Settings tabs and the sign-in dialog are
/// driven through UI Automation; what the transcript page shows is read from the app's log, what the daemon did from its settings and the fake's requests.
/// <b>Explicitly-run project:</b> run it only through <c>tools\e2e\Run-WpfE2E.ps1</c> (NR.6).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfCodexE2ETests : IAsyncLifetime
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);
	private static readonly Regex ToolCount = new(@"Live row shown by the page: [^,]*, \d+ chars, (\d+) tool\(s\)", RegexOptions.Compiled);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-codex-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly UiAutomationClicker _clicker = new();
	private readonly UiAutomationComboBox _combos = new();
	private readonly UiAutomationCommandBar _bar = new();
	private CodexE2EDaemon _daemon = null!;
	private AppProcessHarness _app = null!;

	public Task InitializeAsync()
	{
		Directory.CreateDirectory(_root);
		return Task.CompletedTask;
	}

	public async Task DisposeAsync()
	{
		if (_daemon != null)
			await _daemon.DisposeAsync();
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
			// WebView2 helper processes can hold the data folder for a moment; the folder is in %TEMP%.
		}
	}

	/// <param name="sessionOpened">Also wait until the session is open and its page painted (no message is queued behind the page's start).</param>
	private async Task<Process> StartAsync(bool toolTurns = false, bool sessionOpened = false)
	{
		_daemon = await CodexE2EDaemon.StartAsync(toolTurns);
		_app = new AppProcessHarness(_root, _daemon.WriteDiscovery(_root));
		var process = _app.Start();
		string[] ready = sessionOpened
			? ["Connected to daemon run", $"Session {_daemon.Session.NodeId} opened", "click-to-first-paint"]
			: ["Connected to daemon run", "Tree loaded:"];
		var started = await _app.WaitForLogAsync(process, StartTimeout, ready);
		Assert.True(started, "The app did not start against the Codex test daemon. Log:\n" + _app.ReadLog());
		return process;
	}

	private async Task WaitForAsync(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !condition())
			await Task.Delay(100);

		Assert.True(condition(), $"Expected: {what}. Log:\n{_app.ReadLog()}");
	}

	private int MostToolsShown() => ToolCount.Matches(_app.ReadLog()).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
	}

	/// <summary>TC.CX.40: picking a GPT model groups the picker, hides the Streaming and Tools toggles, filters the profiles and efforts; a send runs on the fake host and shows its tool lines.</summary>
	[Fact]
	public async Task TC_CX_40_PickingAGptModel_ReshapesTheCommandBar_AndASendRunsOnTheFakeCodexHost()
	{
		using var process = await StartAsync(toolTurns: true, sessionOpened: true);
		try
		{
			// The picker: Default, then the groups under disabled headers; Codex's default model first in its group.
			var models = await _combos.ReadLinesAsync(process, "Model", StepTimeout);
			var names = models.Select(l => l.Name).ToList();
			Assert.Contains(new UiAutomationComboBox.Line("Claude", false), models);
			Assert.Contains(new UiAutomationComboBox.Line("GPT (ChatGPT plan)", false), models);
			var gptHeader = names.IndexOf("GPT (ChatGPT plan)");
			Assert.Equal(["GPT-X-Sol", "GPT-X-Astra", "GPT-X-Luna"], names.Skip(gptHeader + 1).Take(3));
			Assert.True(_clicker.Exists(process, "Streaming Session Mode"), "A Claude session shows the Streaming toggle. Log:\n" + _app.ReadLog());

			Assert.True(await _combos.SelectAsync(process, "Model", "GPT-X-Sol", StepTimeout), "GPT-X-Sol could not be picked. Log:\n" + _app.ReadLog());

			await WaitForAsync(() => !_clicker.Exists(process, "Streaming Session Mode"), "the Streaming toggle hidden for a GPT model");
			Assert.False(_clicker.Exists(process, "Local tools"));
			var profiles = await _combos.ReadLinesAsync(process, "Profile", StepTimeout);
			Assert.Equal([CodexE2EDaemon.ChatGptName], profiles.Select(l => l.Name));
			var efforts = await _combos.ReadLinesAsync(process, "Effort", StepTimeout);
			Assert.Equal(["Default", "Low", "Medium", "High", "Xhigh", "Max"], efforts.Select(l => l.Name));   // no ultra (FR.33.3)
			Assert.True(await _combos.SelectAsync(process, "Effort", "High", StepTimeout));

			// A send: the turn runs on the fake app-server, streams its tool lines into the live row and writes the answer.
			Assert.True(await _clicker.SetTextAsync(process, "Prompt input", "run the checks", StepTimeout), "The prompt input was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Send", StepTimeout), "The Send button was not found. Log:\n" + _app.ReadLog());

			await WaitForAsync(() => MostToolsShown() > 0, "the live row to show the fake host's tool lines");
			var file = _daemon.Env.Base.Files.GetFullPath(_daemon.Session.FileName);
			await WaitForAsync(() => File.ReadAllText(file).Contains("All three checks passed.", StringComparison.Ordinal), "the GPT answer written to the transcript");
			await WaitForAsync(() => _app.ReadLog().Contains("Live row shown by the page: , 0 chars", StringComparison.Ordinal), "the live row removed when the turn ended");

			var turnStart = Assert.Single(_daemon.Codex.Requests("turn/start"));
			Assert.Equal("gpt-x-sol", turnStart.GetProperty("model").GetString());
			Assert.Equal("high", turnStart.GetProperty("effort").GetString());
			Assert.Equal("codex", _daemon.Session.EngineId);
			Assert.True(_clicker.Exists(process, "Send"));   // the selectors and Send stay usable after the turn (FR.33.8)
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	/// <summary>TC.CX.41 (General): the Codex status line, the catalog profile, and the tier table edited, saved and reset to the defaults.</summary>
	[Fact]
	public async Task TC_CX_41_SettingsGeneral_ShowsCodexStatus_AndSavesTheCatalogProfileAndTheTierTable()
	{
		using var process = await StartAsync();
		try
		{
			Assert.True(await _clicker.ClickMainMenuItemAsync(process, "Settings…", StepTimeout), "The Settings entry of the main menu was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.SelectTabAsync(process, "General tab", StepTimeout));
			await WaitForAsync(() => _clicker.TextExists(process, $"Codex 0.171.0 found at {CodexE2EDaemon.CodexExe}"), "the Codex status line");
			Assert.True(_clicker.TextExists(process, "Tested with Codex"));

			// The catalog profile: Automatic, then back to the signed-in ChatGPT profile.
			Assert.True(await _combos.SelectAsync(process, "Catalog profile", "Automatic", StepTimeout));
			Assert.True(await _clicker.ClickAsync(process, "Save settings", StepTimeout));
			await WaitForAsync(() => _daemon.Settings.Settings.CodexCatalogProfileId == null, "the catalog profile cleared (automatic)");
			Assert.True(await _combos.SelectAsync(process, "Catalog profile", CodexE2EDaemon.ChatGptName, StepTimeout));
			Assert.True(await _clicker.ClickAsync(process, "Save settings", StepTimeout));
			await WaitForAsync(() => _daemon.Settings.Settings.CodexCatalogProfileId == CodexE2EDaemon.ChatGptProfile, "the catalog profile stored");

			// The tier table: the first row edited and saved, then Reset to defaults saved.
			Assert.True(await _clicker.SetTextAsync(process, "Tier match", "astra-max", StepTimeout), "No tier row. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Save settings", StepTimeout));
			await WaitForAsync(() => _daemon.Settings.Settings.CodexModelTiers?.FirstOrDefault()?.Match == "astra-max", "the edited tier table stored");

			Assert.True(await _clicker.ClickAsync(process, "Reset tiers to defaults", StepTimeout));
			await WaitForAsync(() => _clicker.TextExists(process, "restored when you save"), "the pending reset shown");
			Assert.True(await _clicker.ClickAsync(process, "Save settings", StepTimeout));
			await WaitForAsync(() => _daemon.Settings.Settings.CodexModelTiers == null, "the stored tier table dropped (defaults)");
			await WaitForAsync(() => _clicker.TextExists(process, "This is the built-in table."), "the defaults shown again");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	/// <summary>TC.CX.41 (Profiles): the ChatGPT group with the Codex Default warning; Sign out, Sign in again (browser) with Cancel, New by code, and Delete.</summary>
	[Fact]
	public async Task TC_CX_41_SettingsProfiles_ChatGptGroup_SignOutSignInAgainNewAndDelete()
	{
		using var process = await StartAsync();
		try
		{
			Assert.True(await _clicker.ClickMainMenuItemAsync(process, "Settings…", StepTimeout), "The Settings entry of the main menu was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.SelectTabAsync(process, "Models and profiles tab", StepTimeout));
			Assert.True(await _clicker.WaitForButtonAsync(process, "New ChatGPT profile", StepTimeout), "The ChatGPT group did not load. Log:\n" + _app.ReadLog());
			Assert.True(_clicker.TextExists(process, @"Codex Default shares %USERPROFILE%\.codex"));
			Assert.True(_clicker.TextExists(process, "ChatGPT Plus"));

			// Sign out keeps the profile listed.
			Assert.True(await _clicker.ClickAsync(process, $"Sign out: {CodexE2EDaemon.ChatGptName} ({CodexE2EDaemon.ChatGptProfile})", StepTimeout),
				"Sign out could not be invoked. Buttons:\n" + _clicker.DescribeButtons(process, "ChatGPT") + "\nLog:\n" + _app.ReadLog());
			await WaitForAsync(() => _daemon.ServerOf(CodexE2EDaemon.ChatGptProfile).RequestsOf("account/logout").Count == 1, "account/logout on the profile's host");
			await WaitForAsync(() => _clicker.TextExists(process, "is signed out."), "the sign-out reported");

			// Sign in again by browser: the address goes to the system browser (logged by a test window), then Cancel.
			Assert.True(await _clicker.ClickAsync(process, $"Sign in again: {CodexE2EDaemon.ChatGptName} ({CodexE2EDaemon.ChatGptProfile})", StepTimeout));
			Assert.True(await _clicker.ClickInAnyWindowAsync(process, "Sign in with the browser", StepTimeout), "The sign-in choice was not shown. Log:\n" + _app.ReadLog());
			await WaitForAsync(() => _app.ReadLog().Contains("System browser (test window): https://auth.openai.com/oauth/authorize?fake=1", StringComparison.Ordinal),
				"the sign-in address opened in the system browser");
			Assert.True(await _clicker.ClickAsync(process, "Cancel sign-in", StepTimeout));
			await WaitForAsync(() => _clicker.TextExists(process, "The ChatGPT sign-in did not complete"), "the cancelled sign-in reported");

			// New ChatGPT profile by code: the code, the address and the note; the fake host completes the sign-in.
			Assert.True(await _clicker.ClickAsync(process, "New ChatGPT profile", StepTimeout));
			Assert.True(await _clicker.ClickInAnyWindowAsync(process, "Sign in with a code", StepTimeout), "The sign-in choice was not shown. Log:\n" + _app.ReadLog());
			await WaitForAsync(() => _clicker.GetText(process, "Sign-in code") == "ABCD-1234", "the device code shown");
			Assert.Equal("https://auth.openai.com/codex/device", _clicker.GetText(process, "Sign-in address"));
			Assert.True(_clicker.TextExists(process, "security settings"));
			await _daemon.CompleteLoginAsync("codex_2", "login-2", success: true);
			await WaitForAsync(() => _clicker.TextExists(process, "Signed in to ChatGPT as"), "the successful sign-in reported");
			await WaitForAsync(() => _daemon.Settings.Settings.CodexProfiles.Any(p => p.ProfileId == "codex_2"), "codex_2 stored");

			// Delete asks first, then removes the profile.
			await WaitForAsync(() => _clicker.Exists(process, "Delete: ChatGPT · u@example.com (codex_2)"), "the new profile's row");
			Assert.True(await _clicker.ClickAsync(process, "Delete: ChatGPT · u@example.com (codex_2)", StepTimeout));
			Assert.True(await _clicker.ClickInAnyWindowAsync(process, "Delete ChatGPT profile", StepTimeout), "The delete confirmation was not shown. Log:\n" + _app.ReadLog());
			await WaitForAsync(() => _daemon.Settings.Settings.CodexProfiles.All(p => p.ProfileId != "codex_2"), "codex_2 deleted");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
