using System.Diagnostics;
using System.Text.Json;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for Settings › Colours: the BUILT WPF app starts Dark with colours stored the earlier single-theme way, the Colours tab is opened through UI Automation, a swatch opens
/// the picker, a palette colour is picked (no Save needed: it applies and is stored at once, for the Dark theme only), and the earlier colours move into both themes; Reset takes it out.
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
	public async Task APickedColour_IsStoredAtOnce_ForTheEditedThemeOnly_AndTheEarlierColoursMoveIntoBoth()
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

			var data = await WaitForColoursAsync(c => c.Dark.TryGetValue("WindowBackground", out var v) && v == "#1E90FF");
			Assert.False(data.Light.ContainsKey("WindowBackground"));
			Assert.Equal("#FF0000", data.Dark["RecentTier1"]);   // the earlier colour is in both themes now
			Assert.Equal("#FF0000", data.Light["RecentTier1"]);
			Assert.Empty(data.Overrides);

			// Reset: the colour is taken out of Dark again.
			Assert.True(await _clicker.ClickAsync(process, "Reset Window background", StepTimeout), "The reset button was not found. Log:\n" + _app.ReadLog());
			await WaitForColoursAsync(c => !c.Dark.ContainsKey("WindowBackground"));
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

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
