using System.Diagnostics;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the turn loop (W1a): the BUILT WPF app against a real loopback daemon (real <see cref="TdcpServer"/>, session module over real
/// session files, the tree) with a scripted turn API (<see cref="ScriptedTurnModule"/>) in place of the engine and the fake <c>claude</c>.
/// The prompt is typed and Send/Stop pressed through UI Automation; what the transcript page shows is read from the app's log (the page reports
/// each change of the live row, and each 1000 characters of streamed text, back to the host).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfTurnLoopE2ETests : IDisposable
{
	private const string InputName = "Prompt input";
	private const int SeedEntries = 6;

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-turn-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly ScriptedTurnModule _turns;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly string _main;

	public WpfTurnLoopE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(Enumerable.Range(0, SeedEntries).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_main = _env.Session.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		var catalog = new CatalogResult(
			[new ModelOptionDto("default", "Default", null), new ModelOptionDto("opus", "Opus 5.5", null)],
			["default", "high", "low"],
			[new ProfileOptionDto("default", "[Sys] Default"), new ProfileOptionDto("work", "Work")]);
		ClientTestHost? host = null;
		_turns = new ScriptedTurnModule(_env, () => host!.Bus);
		_host = host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree, new CatalogTestModule([], catalog), _turns]));

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

	private async Task<Process> StartAsync()
	{
		var process = _app.Start();
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint");   // the page is up: no message is queued behind its start
		Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());
		return process;
	}

	private async Task SendAsync(Process process, string text)
	{
		Assert.True(await _clicker.SetTextAsync(process, InputName, text, StepTimeout), "The prompt input was not found. Log:\n" + _app.ReadLog());
		Assert.True(await _clicker.ClickAsync(process, "Send", StepTimeout), "The Send button was not found. Log:\n" + _app.ReadLog());
	}

	private async Task ExpectAsync(Process process, string what, params string[] needles)
	{
		Assert.True(await _app.WaitForLogAsync(process, StepTimeout, needles), $"Expected: {what}. Log:\n{_app.ReadLog()}");
	}

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);

		// Plan section 6: streaming must not stall the UI thread. Startup stalls (window and WebView2 creation) are not the turn loop's, so only the log after the first paint counts.
		var afterStart = log[(log.IndexOf("click-to-first-paint", StringComparison.Ordinal))..];
		Assert.DoesNotContain("UI thread stalled", afterStart);
		Assert.DoesNotContain("UI thread not responding", afterStart);
	}

	[Fact]
	public async Task A_streamed_answer_appears_progressively_then_the_persisted_entry_replaces_it()
	{
		using var process = await StartAsync();
		try
		{
			await SendAsync(process, ScriptedTurnModule.StreamPrefix + " explain");

			await ExpectAsync(process, "the thinking row", "Live row shown by the page: Thinking...");
			await ExpectAsync(process, "the first chunk", "Live row shown by the page: Responding..., 1100 chars");
			Assert.DoesNotContain($", {SeedEntries + 2} in all", _app.ReadLog());   // the answer is not persisted yet: this is the preview
			await ExpectAsync(process, "more text after the first chunk", "Responding..., 2200 chars");
			Assert.DoesNotContain($", {SeedEntries + 2} in all", _app.ReadLog());
			await ExpectAsync(process, "the persisted answer", $", {SeedEntries + 2} in all");
			await ExpectAsync(process, "the live row removed", "Live row shown by the page: , 0 chars");

			var send = Assert.Single(_turns.Sends);
			Assert.Equal(_main, send.NodeId);
			// Restored from the session's lastUsed (the effort of its last answer), then sent with the turn. The test daemon's model and profile
			// services know no "opus" model and no "work" profile, so lastUsed names the default ones: the selectors show those.
			Assert.Equal(("default", "high", "default"), (send.Model, send.Effort, send.Profile));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Stop_interrupts_a_running_turn_and_nothing_is_appended_for_the_answer()
	{
		using var process = await StartAsync();
		try
		{
			await SendAsync(process, ScriptedTurnModule.HoldPrefix + " wait");
			await ExpectAsync(process, "the running turn", "Live row shown by the page: Responding...");
			await ExpectAsync(process, "the prompt persisted", $", {SeedEntries + 1} in all");

			Assert.True(await _clicker.ClickAsync(process, "Stop", StepTimeout), "The Stop button was not found. Log:\n" + _app.ReadLog());

			await ExpectAsync(process, "the live row removed after the stop", "Live row shown by the page: , 0 chars");
			await Task.Delay(1000);
			Assert.DoesNotContain($", {SeedEntries + 2} in all", _app.ReadLog());   // a stopped turn leaves no answer
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task A_second_send_while_a_turn_runs_is_queued_and_runs_after_it()
	{
		using var process = await StartAsync();
		try
		{
			await SendAsync(process, ScriptedTurnModule.HoldPrefix + " first");
			await ExpectAsync(process, "the first turn running", "Live row shown by the page: Responding...");

			await SendAsync(process, "second quick");
			await ExpectAsync(process, "the second send shown as a queued row", "1 tool(s), 1 queued");

			_turns.ReleaseHeld();

			await ExpectAsync(process, "both answers persisted", $", {SeedEntries + 4} in all");
			Assert.Equal(["hold: first", "second quick"], _turns.Sends.Select(s => s.Text));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task A_turn_in_flight_survives_a_reconnect_and_shows_its_final_state()
	{
		using var process = await StartAsync();
		try
		{
			await SendAsync(process, ScriptedTurnModule.StreamPrefix + " across a drop");
			await ExpectAsync(process, "the first chunk", "Live row shown by the page: Responding..., 1100 chars");

			_host.StopServer();
			await Task.Delay(1500);   // the turn goes on in the daemon while the client is away
			_host.StartServer();

			await ExpectAsync(process, "the persisted answer after the reconnect", $", {SeedEntries + 2} in all");
			await ExpectAsync(process, "the live row removed", "Live row shown by the page: , 0 chars");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
