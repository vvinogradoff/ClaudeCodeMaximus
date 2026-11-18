using System.Diagnostics;
using System.Text.Json;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for Settings › Colours: the BUILT WPF app starts Dark with colours stored the earlier single-theme way, the Colours tab is opened through UI Automation, a swatch opens
/// the picker, a palette colour is picked (it stays pending until Apply, FR.9.19; then it is stored for the Dark theme only), and the earlier colours move into both themes; Reset
/// (pending too) and Apply take it out. A colour picked by name and applied reaches the transcript page of the running app (TC.CL.37).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfColoursE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-colours-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly string _main;
	private readonly string _settingsFile;

	public WpfColoursE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(["question", "answer"]);
		_main = _env.Session.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		_settingsFile = Path.Combine(clients.FullName, "wpf.json");
		File.WriteAllText(_settingsFile,
			$"{{\"lastSelectedNodeId\":\"{_main}\",\"expandedNodeIds\":[\"{_env.Base.Directory.NodeId}\"],\"sections\":{{"
			+ "\"appearance\":{\"version\":1,\"data\":{\"theme\":\"Dark\"}},"
			+ "\"colours\":{\"version\":1,\"data\":{\"overrides\":{\"RecentTier1\":\"#ff0000\"}}}}}");
		_app = new AppProcessHarness(_root, daemonFile);
	}

	public void Dispose()
	{
		_host.Dispose();
		_env.Dispose();
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
			// WebView2 helper processes can hold the data folder for a moment; the folder is in %TEMP%.
		}
	}

	[Fact]
	public async Task APickedColour_IsStoredOnApply_ForTheEditedThemeOnly_AndTheEarlierColoursMoveIntoBoth()
	{
		using var process = _app.Start();
		try
		{
			var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint");
			Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());

			Assert.True(await _clicker.ClickMainMenuItemAsync(process, "Settings…", StepTimeout), "The Settings entry of the main menu was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.SelectTabAsync(process, "Colours tab", StepTimeout), "The Colours tab was not found. Log:\n" + _app.ReadLog());

			// The swatch opens the picker; a palette colour is picked (the theme being edited is the current one: Dark).
			Assert.True(await _clicker.ClickAsync(process, "Colour Window background", StepTimeout), "The swatch was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Palette #1E90FF", StepTimeout), "The palette colour was not found. Log:\n" + _app.ReadLog());

			// Picked, not applied: the swatch is pending (its Reset shows) and nothing is stored yet.
			Assert.True(await _clicker.WaitForButtonAsync(process, "Reset Window background", StepTimeout), "The pending colour did not show its Reset. Log:\n" + _app.ReadLog());
			await Task.Delay(800);
			Assert.False(ReadColours().Dark.ContainsKey("WindowBackground"), "The colour was stored before Apply. wpf.json:\n" + File.ReadAllText(_settingsFile));

			Assert.True(await _clicker.ClickAsync(process, "Apply colours", StepTimeout), "Apply could not be pressed. Log:\n" + _app.ReadLog());
			var data = await WaitForColoursAsync(c => c.Dark.TryGetValue("WindowBackground", out var v) && v == "#1E90FF");
			Assert.False(data.Light.ContainsKey("WindowBackground"));
			Assert.Equal("#FF0000", data.Dark["RecentTier1"]);   // the earlier colour is in both themes now
			Assert.Equal("#FF0000", data.Light["RecentTier1"]);
			Assert.Empty(data.Overrides);

			// Reset is pending too: the colour leaves Dark when Apply is pressed.
			Assert.True(await _clicker.ClickAsync(process, "Reset Window background", StepTimeout), "The reset button was not found. Log:\n" + _app.ReadLog());
			await Task.Delay(800);
			Assert.True(ReadColours().Dark.ContainsKey("WindowBackground"), "The reset was stored before Apply.");
			Assert.True(await _clicker.ClickAsync(process, "Apply colours", StepTimeout), "Apply could not be pressed. Log:\n" + _app.ReadLog());
			await WaitForColoursAsync(c => !c.Dark.ContainsKey("WindowBackground"));
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	/// <summary>TC.CL.26: the Colours tab shows Apply and two columns; Apply after a change leaves the stored colour in the client state.</summary>
	[Fact]
	public async Task TheColoursTab_ShowsApplyAndTwoColumns_AndApplyKeepsTheStoredColour()
	{
		using var process = _app.Start();
		try
		{
			var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint");
			Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());

			Assert.True(await _clicker.ClickMainMenuItemAsync(process, "Settings…", StepTimeout), "The Settings entry of the main menu was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.SelectTabAsync(process, "Colours tab", StepTimeout), "The Colours tab was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.WaitForButtonAsync(process, "Apply colours", StepTimeout), "The Apply button was not found. Log:\n" + _app.ReadLog());

			// Apply is to the right of Reset all, in the same row.
			var apply = _clicker.GetBounds(process, "Apply colours");
			var resetAll = _clicker.GetBounds(process, "Reset all colours");
			Assert.NotNull(apply);
			Assert.NotNull(resetAll);
			Assert.True(apply.Value.Left >= resetAll.Value.Right, $"Apply {apply} is not right of Reset all {resetAll}");
			Assert.InRange(Math.Abs(apply.Value.Top - resetAll.Value.Top), 0, 12);

			// Two columns: the first two colours of the first group are side by side.
			var first = _clicker.GetBounds(process, "Colour " + FirstTwoLabels.First);
			var second = _clicker.GetBounds(process, "Colour " + FirstTwoLabels.Second);
			Assert.NotNull(first);
			Assert.NotNull(second);
			Assert.InRange(Math.Abs(first.Value.Top - second.Value.Top), 0, 4);
			Assert.True(second.Value.Left > first.Value.Right, $"The second swatch {second} is not right of the first {first}");

			// A colour is changed, Apply is pressed: the colour is stored.
			Assert.True(await _clicker.ClickAsync(process, "Colour Window background", StepTimeout), "The swatch was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Palette #1E90FF", StepTimeout), "The palette colour was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Apply colours", StepTimeout), "Apply could not be pressed. Log:\n" + _app.ReadLog());
			var data = await WaitForColoursAsync(c => c.Dark.TryGetValue("WindowBackground", out var v) && v == "#1E90FF");
			Assert.False(data.Light.ContainsKey("WindowBackground"));
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	/// <summary>
	/// TC.CL.37 (also TC.CL.34): a colour picked by its CSS name in the picker stays pending, and after Apply it is stored and the transcript page of the running app has it
	/// (the page's <c>--bg</c> variable and the computed background of its body), without a restart or a theme switch.
	/// </summary>
	[Fact]
	public async Task ANamedColour_StaysPendingUntilApply_ThenReachesTheStoreAndTheTranscriptPage()
	{
		var port = CdpPage.FreePort();
		using var process = _app.Start(CdpPage.EnvironmentFor(port));
		try
		{
			var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint");
			Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());
			using var page = await CdpPage.ConnectAsync(port, "transcript.html", StartTimeout);
			Assert.Equal(string.Empty, (await page.EvalAsync("document.documentElement.style.getPropertyValue('--bg')"))?.ToString());

			Assert.True(await _clicker.ClickMainMenuItemAsync(process, "Settings…", StepTimeout), "The Settings entry of the main menu was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.SelectTabAsync(process, "Colours tab", StepTimeout), "The Colours tab was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Colour Page background", StepTimeout), "The swatch was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.SetTextAsync(process, "Filter named colours", "palegolden", StepTimeout), "The filter box was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Named palegoldenrod", StepTimeout), "The named colour was not found. Log:\n" + _app.ReadLog());

			// Pending: not stored, not on the page.
			await Task.Delay(1200);
			Assert.False(ReadColours().Dark.ContainsKey("PageBackground"), "The colour was stored before Apply.");
			Assert.Equal(string.Empty, (await page.EvalAsync("document.documentElement.style.getPropertyValue('--bg')"))?.ToString());

			// The picker is still open (a named colour is not a final choice, so its name and harmonies stay in view): Apply commits it and closes it.
			Assert.True(await _clicker.ClickInAnyWindowAsync(process, "Apply colours", StepTimeout), "Apply could not be pressed. Log:\n" + _app.ReadLog());
			await WaitForColoursAsync(c => c.Dark.TryGetValue("PageBackground", out var v) && v == "#EEE8AA");

			var deadline = DateTime.UtcNow + StepTimeout;
			string? value = null;
			string? body = null;
			while (DateTime.UtcNow < deadline)
			{
				value = (await page.EvalAsync("document.documentElement.style.getPropertyValue('--bg')"))?.ToString();
				body = (await page.EvalAsync("getComputedStyle(document.body).backgroundColor"))?.ToString();
				if (string.Equals(value, "#EEE8AA", StringComparison.OrdinalIgnoreCase) && body == "rgb(238, 232, 170)")
					break;

				await Task.Delay(200);
			}

			Assert.Equal("#EEE8AA", value?.ToUpperInvariant());
			Assert.Equal("rgb(238, 232, 170)", body);
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	/// <summary>
	/// TC.ST.07 (FR.4.7, FR.9.19; also TC.CL.33): a colour is picked and left pending, Settings is closed, the dialog asks, Save stores the colour in the client state.
	/// Discard on a second round stores nothing.
	/// </summary>
	[Fact]
	public async Task ClosingSettingsWithAPendingColour_Asks_AndSaveStoresIt_DiscardDoesNot()
	{
		// The test window answers dialogs from a script (no modal window): first Discard, then Save.
		using var process = _app.Start("--test-dialog", "choice:discard", "--test-dialog", "choice:save");
		try
		{
			var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint");
			Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());

			// Round 1: Discard.
			Assert.True(await _clicker.ClickMainMenuItemAsync(process, "Settings…", StepTimeout), "The Settings entry of the main menu was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.SelectTabAsync(process, "Colours tab", StepTimeout), "The Colours tab was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Colour Window background", StepTimeout), "The swatch was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Palette #1E90FF", StepTimeout), "The palette colour was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickInAnyWindowAsync(process, "Close settings", StepTimeout), "Close could not be pressed. Log:\n" + _app.ReadLog());
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "answered with choice:discard"), "The unsaved-changes dialog was not asked. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.WaitForButtonGoneAsync(process, "Close settings", StepTimeout), "Settings did not close. Log:\n" + _app.ReadLog());
			await Task.Delay(800);
			Assert.False(ReadColours().Dark.ContainsKey("WindowBackground"), "Discard stored the colour.");

			// Round 2: Save.
			Assert.True(await _clicker.ClickMainMenuItemAsync(process, "Settings…", StepTimeout), "The Settings entry of the main menu was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.SelectTabAsync(process, "Colours tab", StepTimeout), "The Colours tab was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Colour Window background", StepTimeout), "The swatch was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Palette #1E90FF", StepTimeout), "The palette colour was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickInAnyWindowAsync(process, "Close settings", StepTimeout), "Close could not be pressed. Log:\n" + _app.ReadLog());
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "answered with choice:save"), "The unsaved-changes dialog was not asked. Log:\n" + _app.ReadLog());
			var data = await WaitForColoursAsync(c => c.Dark.TryGetValue("WindowBackground", out var v) && v == "#1E90FF");
			Assert.False(data.Light.ContainsKey("WindowBackground"));
			Assert.True(await _clicker.WaitForButtonGoneAsync(process, "Close settings", StepTimeout), "Settings did not close after Save. Log:\n" + _app.ReadLog());
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	private StoredColours ReadColours()
	{
		using var document = JsonDocument.Parse(File.ReadAllText(_settingsFile));
		if (document.RootElement.TryGetProperty("sections", out var sections) && sections.TryGetProperty("colours", out var colours) && colours.TryGetProperty("data", out var data))
			return data.Deserialize<StoredColours>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new StoredColours();

		return new StoredColours();
	}

	private static readonly (string First, string Second) FirstTwoLabels = ("Window background", "Panels and cards");   // the first two rows of the first group

	private sealed class StoredColours
	{
		public Dictionary<string, string> Light { get; set; } = [];

		public Dictionary<string, string> Dark { get; set; } = [];

		public Dictionary<string, string> Overrides { get; set; } = [];
	}

	private async Task<StoredColours> WaitForColoursAsync(Func<StoredColours, bool> condition)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		var text = string.Empty;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				text = File.ReadAllText(_settingsFile);
				using var document = JsonDocument.Parse(text);
				if (document.RootElement.TryGetProperty("sections", out var sections) && sections.TryGetProperty("colours", out var colours) && colours.TryGetProperty("data", out var data))
				{
					var stored = data.Deserialize<StoredColours>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new StoredColours();
					if (condition(stored))
						return stored;
				}
			}
			catch (Exception ex) when (ex is IOException or JsonException)
			{
				// The app is writing the file right now.
			}

			await Task.Delay(100);
		}

		Assert.Fail($"The stored colours never reached the expected state. wpf.json:\n{text}\nLog:\n{_app.ReadLog()}");
		return new StoredColours();
	}
}
