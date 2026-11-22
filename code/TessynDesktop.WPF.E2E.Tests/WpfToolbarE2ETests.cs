using System.Diagnostics;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the instruction toolbar and the command-bar toggles (W4): the BUILT WPF app against a real loopback daemon with a scripted turn API
/// (<see cref="ScriptedTurnModule"/>) and a scripted options API (<see cref="ToolbarTestModule"/>). Streaming Session Mode goes through the real session module. The toggles are pressed through UI Automation; what
/// reaches the daemon (the options, the one-shot flags of <c>turn.send</c>, Terminate, Streaming Session Mode) is read from the scripted modules.
/// The hidden instruction block itself is built by the daemon (FR.11.10) and is covered by the Core tests, not here.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfToolbarE2ETests : IDisposable
{
	private const string InputName = "Prompt input";

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-toolbar-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly ScriptedTurnModule _turns;
	private readonly ToolbarTestModule _toolbar = new();
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly string _main;

	public WpfToolbarE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(Enumerable.Range(0, 4).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_main = _env.Session.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, TestEngines.Claude(new StubStreamingManager()), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		var catalog = new CatalogResult(
			[new ModelOptionDto("default", "Default", null), new ModelOptionDto("opus", "Opus 5.5", null)],
			["default", "high"],
			[new ProfileOptionDto("default", "[Sys] Default")]);
		ClientTestHost? host = null;
		_turns = new ScriptedTurnModule(_env, () => host!.Bus);
		_host = host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree, new CatalogTestModule([], catalog), _turns, _toolbar]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_main}\",\"expandedNodeIds\":[\"{_env.Base.Directory.NodeId}\"]}}");
		_app = new AppProcessHarness(_root, daemonFile);
	}

	public void Dispose()
	{
		_turns.ReleaseHeld();
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

	private async Task<Process> StartAsync(params string[] extraArguments)
	{
		var process = _app.Start(extraArguments);
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint");
		Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());
		await WaitForCallAsync("readOptions");   // the toolbar is enabled once the options are read
		return process;
	}

	private async Task WaitForCallAsync(string call)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !_toolbar.Calls.Contains(call))
			await Task.Delay(50);

		Assert.True(_toolbar.Calls.Contains(call), $"Expected the daemon to get '{call}'. Got: {string.Join(" | ", _toolbar.Calls)}\nLog:\n{_app.ReadLog()}");
	}

	private async Task ToggleAsync(Process process, string name) =>
		Assert.True(await _clicker.ToggleAsync(process, name, StepTimeout), $"The toggle '{name}' was not found. Log:\n{_app.ReadLog()}");

	private static string[] Dialogs(params string[] answers) => answers.SelectMany(a => new[] { "--test-dialog", a }).ToArray();

	[Fact]
	public async Task NewBranch_and_AutoCompact_travel_with_one_send_and_switch_off()
	{
		using var process = await StartAsync();
		try
		{
			await ToggleAsync(process, "New Branch");
			await ToggleAsync(process, "Auto-Compact");
			Assert.True(await _clicker.SetTextAsync(process, InputName, "hello", StepTimeout));
			Assert.True(await _clicker.ClickAsync(process, "Send", StepTimeout));
			await WaitUntilAsync("the first send", () => _turns.Flags.Count == 1);

			Assert.True(_turns.Flags.TryPeek(out var first));
			Assert.Equal((true, true), first);

			// Both flags switched off with the send: the next one carries none.
			Assert.True(await _clicker.SetTextAsync(process, InputName, "again", StepTimeout));
			Assert.True(await _clicker.ClickAsync(process, "Send", StepTimeout));
			await WaitUntilAsync("the second send", () => _turns.Flags.Count == 2);
			Assert.Equal((false, false), _turns.Flags.ToArray()[1]);
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task AutoCommit_cycles_and_AutoDocument_and_Sound_are_stored()
	{
		using var process = await StartAsync();
		try
		{
			// The daemon starts at Do-not-commit: the cycle goes Commit, No preference.
			Assert.True(await _clicker.ClickAsync(process, "Auto-Commit", StepTimeout));
			await WaitForCallAsync("setOptions:autoCommit=True");
			Assert.True(await _clicker.ClickAsync(process, "Auto-Commit", StepTimeout));
			await WaitForCallAsync("setOptions:autoCommit=none");
			Assert.Null(_toolbar.AutoCommit);

			await ToggleAsync(process, "Auto-Document");
			await WaitForCallAsync("setOptions:autoDocument=True");
			Assert.True(_toolbar.AutoDocument);

			await ToggleAsync(process, "Focus item sound");
			await WaitForCallAsync("setOptions:sound=False");
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Terminate_asks_and_detaches_only_when_confirmed()
	{
		using var process = await StartAsync(Dialogs("cancel", "accept"));
		try
		{
			Assert.True(await _clicker.ClickAsync(process, "Terminate session", StepTimeout));
			await Task.Delay(1500);   // a cancelled dialog sends nothing: there is nothing to wait for, so give it time to (wrongly) happen
			Assert.DoesNotContain(_toolbar.Calls, c => c.StartsWith("reset:", StringComparison.Ordinal));

			Assert.True(await _clicker.ClickAsync(process, "Terminate session", StepTimeout));
			await WaitForCallAsync("reset:" + _main);
			Assert.Contains("localTools.get", _toolbar.Calls);   // the profile of the directory is read when the session opens
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Streaming_mode_toggle_is_stored_on_the_session()
	{
		using var process = await StartAsync();
		try
		{
			Assert.False(_env.Session.IsStreamingMode);
			await ToggleAsync(process, "Streaming Session Mode");
			await WaitUntilAsync("Streaming Session Mode stored on", () => _env.Session.IsStreamingMode);

			// Off needs no question here: no process is alive (the daemon asks only when one is; that path is covered by the view model tests).
			await ToggleAsync(process, "Streaming Session Mode");
			await WaitUntilAsync("Streaming Session Mode stored off", () => !_env.Session.IsStreamingMode);
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	private static async Task WaitUntilAsync(string what, Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !condition())
			await Task.Delay(50);

		Assert.True(condition(), "Expected: " + what);
	}
}
